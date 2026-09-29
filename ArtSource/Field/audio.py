"""Field sound pack from CC0 field recordings. Blender 4.5 (audaspace decodes OGG/WAV) / NumPy.

Wind: two seamless 90 s stereo loops from one fran_marenco session (calm bed + gusts, same gain),
plus a mono ash-hiss loop from Vrymaa's dune recording. Steps: 30 single mono footsteps cut
automatically from two felix.blume dry-sand walks (same Schoeps MS rig).

Sources live in ArtSource/Field/Audio/Source/<freesound id>.(wav|flac|ogg); an original WAV,
if present, wins over the HQ preview. Windows below are seconds on the original timeline and were
chosen by audio analysis: no birds or tonal events, no plane, no road rumble. See Audio/SOURCES.md.
"""
import json
import urllib.request
import wave
from pathlib import Path
import numpy as np
import aud

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'ArtSource/Field/Audio/Source'
OUT = ROOT / 'Assets/Art/Field/Audio'
REPORT = ROOT / 'ArtSource/Field/audio-validation.json'
RATE = 48000
LOOP = 90.0
XFADE = 6.0

# Freesound id -> (uploader id for the preview URL, author, title). All CC0 1.0.
SOURCES = {
    '852881': ('3557494', 'fran_marenco', 'Low wind in a desert canyon 2'),
    '852882': ('3557494', 'fran_marenco', 'Low wind guts vegetation'),
    '825860': ('13973196', 'Vrymaa', 'Desert sand - Dune atmosphere'),
    '705513': ('1661766', 'felix.blume', 'Footsteps with sneakers, slowly walking on sand in the desert'),
    '705743': ('1661766', 'felix.blume', 'Footsteps wearing sneakers and jeans walking on dry sand in the desert'),
}
# Loop windows: (source, [(start, end), ...]); every part carries its own crossfade tail.
CALM = ('852881', [(213.0, 213.0 + LOOP + XFADE)])
GUSTS = ('852882', [(25.0, 25.0 + LOOP + XFADE)])
HISS = ('825860', [(1.0, 22.0), (97.0, 120.0)])
HISS_XFADE = 2.0
# Steps: source -> (count, excluded ranges in seconds).
STEPS = {
    '705513': (16, [(165.0, 1e9)]),          # low rumble from 165 s on
    '705743': (14, [(35.0, 105.0)]),         # distant plane
}
CALM_RMS = -28.0     # dBFS; gusts share the calm gain so their extra loudness stays real
HISS_RMS = -26.0
STEP_RMS = -18.0     # over the loudest 100 ms of each step
STEP_CREST = 22.0    # dB peak over that; spikier cuts are clicks, not footfalls
PEAK = -1.0


def db(x):
    return 20 * np.log10(np.maximum(x, 1e-12))


def rms(x):
    return float(np.sqrt(np.mean(np.square(x))))


def load(sid):
    for ext in ('.wav', '.flac', '.ogg'):
        path = SRC / f'{sid}{ext}'
        if path.exists():
            sound = aud.Sound(str(path))
            if int(sound.specs[0]) != RATE:
                raise ValueError(f'{path.name}: expected {RATE} Hz, got {sound.specs[0]}')
            return sound.data().astype(np.float64), path.name
    # No original: fetch the public HQ preview (the original WAV needs a Freesound login).
    uploader = SOURCES[sid][0]
    url = f'https://cdn.freesound.org/previews/{sid[:3]}/{sid}_{uploader}-hq.ogg'
    SRC.mkdir(parents=True, exist_ok=True)
    print(f'Fetching {url}')
    request = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    with urllib.request.urlopen(request, timeout=600) as r:
        (SRC / f'{sid}.ogg').write_bytes(r.read())
    return load(sid)


def highpass(x, fc):
    """Zero-phase FFT high-pass with a half-octave cosine shoulder."""
    spec = np.fft.rfft(x, axis=0)
    f = np.fft.rfftfreq(len(x), 1 / RATE)
    g = np.clip((np.log2(np.maximum(f, 1e-6) / fc) + 0.5) / 0.5, 0, 1)
    g = 0.5 - 0.5 * np.cos(np.pi * g)
    return np.fft.irfft(spec * (g if x.ndim == 1 else g[:, None]), len(x), axis=0)


def loop(data, parts, xfade):
    """Concatenate parts with equal-power crossfades; the last part wraps into the first."""
    x = int(xfade * RATE)
    segs = [data[int(a * RATE):int(b * RATE)] for a, b in parts]
    t = np.linspace(0, 1, x, endpoint=False)
    if data.ndim > 1:
        t = t[:, None]
    out = []
    for i, seg in enumerate(segs):
        prev = segs[i - 1]
        body = seg[:len(seg) - x].copy()
        body[:x] = seg[:x] * np.sqrt(t) + prev[len(prev) - x:] * np.sqrt(1 - t)
        out.append(body)
    return np.concatenate(out)


def envelope(x, hop):
    n = len(x) // hop
    return db(np.sqrt(np.mean(np.square(x[:n * hop].reshape(n, hop)), axis=1)))


def find_steps(mono, excluded):
    """Isolated footfalls: a loud burst preceded by quiet, ending when the envelope settles."""
    hop = RATE // 100
    env = envelope(highpass(mono, 250.0), hop)
    t = np.arange(len(env)) * hop / RATE
    allowed = np.ones(len(env), bool)
    for a, b in excluded:
        allowed &= ~((t >= a) & (t < b))
    floor = float(np.percentile(env[allowed], 20))
    found, i = [], 20
    while i < len(env) - 80:
        if not (allowed[i] and env[i] > floor + 10):
            i += 1
            continue
        start = i
        while start > i - 6 and env[start - 1] > floor + 5:
            start -= 1
        # Quiet is relative to the step too: a studio floor is far below the sand shuffling between steps.
        peak = float(env[start:start + 40].max())
        settle = max(floor + 5, peak - 26)
        end = i
        while end < start + 70 and not np.all(env[end:end + 8] < settle):
            end += 1
        ok = (peak > floor + 18 and 15 <= end - start < 70 and np.all(env[start - 15:start] < max(floor + 8, peak - 20))
              and allowed[start - 15:end + 8].all())
        if ok:
            found.append((peak - floor, start * hop, end * hop))
        i = end + 8
    return found, floor


def loudness(x, window=0.1):
    """RMS of the loudest window, dBFS."""
    n = int(window * RATE)
    c = np.concatenate([[0.0], np.cumsum(np.square(x))])
    return db(np.sqrt((c[n:] - c[:-n]).max() / n))


def limit(x, ceiling=PEAK, circular=True, hold=0.01):
    """Offline peak limiter: the gain dips around a peak just enough, never above the ceiling.
    Circular for loops, so the gain curve is continuous across the seam."""
    h = int(hold * RATE)
    level = np.abs(x) if x.ndim == 1 else np.abs(x).max(axis=1)
    need = np.maximum(level / 10 ** (ceiling / 20), 1.0)
    if not circular:
        need = np.pad(need, 2 * h, mode='edge')
    env, s = need.copy(), 1
    while s <= h:  # max over +h..-h by doubling
        env = np.maximum(env, np.maximum(np.roll(env, s), np.roll(env, -s)))
        s *= 2
    gain = 1 / env
    k = h // 2
    c = np.concatenate([[0.0], np.cumsum(np.concatenate([gain[-k:], gain, gain[:k]]))])
    gain = (c[2 * k:] - c[:-2 * k])[:len(gain)] / (2 * k)
    if not circular:
        gain = gain[2 * h:-2 * h]
    return x * (gain if x.ndim == 1 else gain[:, None])


def cut_step(mono, a, b):
    seg = mono[max(0, a - RATE // 100):b + RATE // 20].copy()
    fin, fout = int(0.004 * RATE), int(0.06 * RATE)
    seg[:fin] *= np.linspace(0, 1, fin)
    seg[-fout:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, fout))
    seg *= 10 ** ((STEP_RMS - loudness(seg)) / 20)
    return limit(seg, circular=False)


def write(path, x):
    x = np.atleast_2d(x.T).T
    pcm = np.clip(np.rint(x * 32767), -32768, 32767).astype('<i2')
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(x.shape[1])
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def describe(x):
    return {'seconds': round(len(x) / RATE, 3), 'channels': 1 if x.ndim == 1 else x.shape[1],
            'rms_db': round(db(rms(x)), 2), 'peak_db': round(db(np.abs(x).max()), 2)}


def main():
    report = {'rate': RATE, 'sources': {}, 'outputs': {}}
    for old in OUT.glob('*.wav'):
        old.unlink()

    calm_src, calm_name = load(CALM[0])
    gust_src, gust_name = load(GUSTS[0])
    calm = loop(highpass(calm_src, 30.0), CALM[1], XFADE)
    gusts = loop(highpass(gust_src, 30.0), GUSTS[1], XFADE)
    gain = 10 ** ((CALM_RMS - db(rms(calm))) / 20)
    loops = {'Wind_Calm_Loop': limit(calm * gain), 'Wind_Gusts_Loop': limit(gusts * gain)}
    report['sources'][CALM[0]] = calm_name
    report['sources'][GUSTS[0]] = gust_name

    hiss_src, hiss_name = load(HISS[0])
    hiss = loop(highpass(hiss_src.mean(axis=1), 120.0), HISS[1], HISS_XFADE)
    hiss *= 10 ** ((HISS_RMS - db(rms(hiss))) / 20)
    loops['Ash_Hiss_Loop'] = hiss
    report['sources'][HISS[0]] = hiss_name

    for name, x in loops.items():
        write(OUT / f'{name}.wav', x)
        report['outputs'][name] = describe(x)

    index = 1
    for sid, (count, excluded) in STEPS.items():
        data, src_name = load(sid)
        report['sources'][sid] = src_name
        mono = highpass(data.mean(axis=1), 50.0)
        found, floor = find_steps(mono, excluded)
        clean = [s for s in found if db(np.abs(mono[s[1]:s[2]]).max()) - loudness(mono[s[1]:s[2]]) <= STEP_CREST]
        if len(clean) < count:
            raise RuntimeError(f'{sid}: only {len(clean)} clean isolated steps, need {count}')
        best = sorted(clean, reverse=True)[:count]
        for snr, a, b in sorted(best, key=lambda s: s[1]):
            name = f'Step_{index:02d}'
            step = cut_step(mono, a, b)
            write(OUT / f'{name}.wav', step)
            report['outputs'][name] = dict(describe(step), source=sid, at=round(a / RATE, 3), snr_db=round(snr, 1))
            index += 1
        print(f'{sid}: {len(found)} isolated steps (floor {floor:.1f} dB), {len(clean)} clean, kept {count}')

    REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n')
    print(f'Wrote {len(report["outputs"])} clips to {OUT}')


main()
