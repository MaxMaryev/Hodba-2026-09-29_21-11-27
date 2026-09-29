"""Independent acceptance checks for the Field sound pack; plain Python + NumPy."""
import json
import wave
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Art/Field/Audio'
RATE = 48000


def read(path):
    with wave.open(str(path)) as w:
        assert w.getframerate() == RATE, f'{path.name}: {w.getframerate()} Hz'
        assert w.getsampwidth() == 2, f'{path.name}: expected 16 bit'
        x = np.frombuffer(w.readframes(w.getnframes()), '<i2').reshape(-1, w.getnchannels())
    return x.astype(np.float64) / 32768


def db(x):
    return 20 * np.log10(np.maximum(x, 1e-12))


def window_rms(x, n):
    c = np.concatenate([[0.0], np.cumsum(np.square(x))])
    return np.sqrt((c[n:] - c[:-n]) / n)


def check_loop(name, channels, seconds):
    x = read(OUT / f'{name}.wav')
    assert x.shape[1] == channels, f'{name}: {x.shape[1]} channels'
    assert abs(len(x) / RATE - seconds) < 0.01, f'{name}: {len(x) / RATE:.2f} s'
    assert db(np.abs(x).max()) <= -0.9, f'{name}: clips'
    assert np.all(np.abs(x.mean(axis=0)) < 1e-3), f'{name}: DC offset'
    # Seam: the wrap-around step must look like any other sample step, and the level must not jump.
    steps = np.abs(np.diff(x, axis=0)).max(axis=1)
    seam = np.abs(x[0] - x[-1]).max()
    assert seam <= np.percentile(steps, 99), f'{name}: click at loop seam'
    m = x.mean(axis=1)
    wrap = np.concatenate([m[-RATE:], m[:RATE]])
    level = db(window_rms(wrap, RATE // 20)[::RATE // 20])
    assert np.abs(np.diff(level)).max() < 6, f'{name}: level jump at loop seam'
    return {'seconds': len(x) / RATE, 'rms_db': round(float(db(np.sqrt(np.mean(x ** 2)))), 2),
            'seam_step': float(seam), 'p99_step': float(np.percentile(steps, 99))}


def main():
    report = json.loads((ROOT / 'ArtSource/Field/audio-validation.json').read_text())
    results = {
        'Wind_Calm_Loop': check_loop('Wind_Calm_Loop', 2, 90),
        'Wind_Gusts_Loop': check_loop('Wind_Gusts_Loop', 2, 90),
        'Ash_Hiss_Loop': check_loop('Ash_Hiss_Loop', 1, 40),
    }
    calm, gusts = results['Wind_Calm_Loop']['rms_db'], results['Wind_Gusts_Loop']['rms_db']
    assert gusts > calm + 2, 'Gust loop should be louder than the calm bed'

    steps = sorted(OUT.glob('Step_*.wav'))
    assert len(steps) == 30, f'Expected 30 steps, got {len(steps)}'
    loud = []
    for path in steps:
        x = read(path)
        assert x.shape[1] == 1, f'{path.name}: steps must be mono'
        x = x[:, 0]
        assert 0.15 <= len(x) / RATE <= 0.8, f'{path.name}: {len(x) / RATE:.2f} s'
        peak = np.abs(x).max()
        assert db(peak) <= -0.9, f'{path.name}: clips'
        edge = int(0.001 * RATE)
        assert np.abs(x[:edge]).max() < 0.05 * peak and np.abs(x[-edge:]).max() < 0.01 * peak, f'{path.name}: hard edge'
        loud.append(db(window_rms(x, RATE // 10).max()))
    loud = np.array(loud)
    assert np.all(np.abs(loud - np.median(loud)) < 4), 'Step loudness spread over 4 dB'
    assert set(report['outputs']) == set(results) | {p.stem for p in steps}, 'Report does not match files'
    print(f'Audio checks passed: 3 loops, {len(steps)} steps, step loudness {loud.min():.1f}..{loud.max():.1f} dBFS')


main()
