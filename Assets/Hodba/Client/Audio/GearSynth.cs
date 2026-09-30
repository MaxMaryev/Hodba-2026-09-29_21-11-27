using Hodba.Client.Body;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Ткань и лямки. Шуршат чуть позже шага — снаряжение догоняет тело; изредка скрипит лямка;
    /// при остановке рюкзак оседает, и что-то внутри тихо звякает.
    /// Всё от тех же событий тела: своего ритма у снаряжения нет.
    /// </summary>
    public sealed class GearSynth : ProceduralAudio
    {
        const int Voices = 4;

        struct Voice
        {
            public int Kind; // 0 — ткань, 1 — лямка, 2 — звяк
            public int Delay, Pos, Length;
            public float Gain, Pan, Hz, Hz2, PhaseA, PhaseB;
            public BandPass Band;
        }

        readonly Voice[] _voices = new Voice[Voices];
        int _next;

        // Заявки из главного потока: по слоту на вид, чтобы звяк не затёр шорох.
        volatile int _rustleReq, _creakReq, _clinkReq;
        volatile float _rustleGain, _creakGain, _clinkGain, _pan;
        int _rustleSeen, _creakSeen, _clinkSeen;

        public static GearSynth Create(Transform parent) => Create<GearSynth>(parent, "Gear Audio", 0x6EA85u);

        public void OnStep(in StepEvent e, float volume)
        {
            if (!e.Felt) return;
            _pan = e.Left ? -0.08f : 0.08f;
            _rustleGain = volume * Mathf.Clamp01(e.Force) * (e.Stumble ? 1.8f : 1f);
            _rustleReq++;
            if (Random.value < (e.Stumble ? 0.6f : 0.07f))
            {
                _creakGain = volume * 0.6f;
                _creakReq++;
            }
        }

        public void OnBodyEvent(in BodyEvent e, float volume)
        {
            if (e.Kind == BodyEventKind.Settled)
            {
                _clinkGain = volume * 0.5f;
                _clinkReq++;
                _rustleGain = volume * 0.7f;
                _rustleReq++;
            }
            else if (e.Kind == BodyEventKind.Start)
            {
                _rustleGain = volume * 0.6f;
                _rustleReq++;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_rustleReq != _rustleSeen) { _rustleSeen = _rustleReq; Spawn(0, _rustleGain, Rand(0.04f, 0.09f)); }
            if (_creakReq != _creakSeen) { _creakSeen = _creakReq; Spawn(1, _creakGain, Rand(0.02f, 0.12f)); }
            if (_clinkReq != _clinkSeen) { _clinkSeen = _clinkReq; Spawn(2, _clinkGain, Rand(0.22f, 0.34f)); }

            for (int i = 0; i < data.Length; i += channels)
            {
                float l = 0f, r = 0f;
                for (int v = 0; v < Voices; v++)
                {
                    ref var o = ref _voices[v];
                    if (o.Length <= 0) continue;
                    if (o.Delay > 0) { o.Delay--; continue; }
                    float t = o.Pos / (float)o.Length;
                    float s = Sample(ref o, t);
                    l += s * (1f - o.Pan);
                    r += s * (1f + o.Pan);
                    if (++o.Pos >= o.Length) o.Length = 0;
                }
                Write(data, i, channels, l, r);
            }
        }

        float Sample(ref Voice o, float t)
        {
            switch (o.Kind)
            {
                case 0: // ткань: мягкий шорох с быстрым входом
                {
                    float env = Mathf.Sin(Mathf.PI * Mathf.Sqrt(t)) * (1f - t);
                    return o.Band.Process(Noise(), o.Hz, 1.4f, SampleRate) * env * o.Gain * 0.5f;
                }
                case 1: // лямка: трение с «прилипанием» — узкая полоса, дрожащая по амплитуде
                {
                    o.PhaseA += 2f * Mathf.PI * o.Hz2 / SampleRate;
                    float stick = 0.5f + 0.5f * Mathf.Sin(o.PhaseA);
                    float env = Mathf.Sin(Mathf.PI * t);
                    return o.Band.Process(Noise(), o.Hz, 0.15f, SampleRate) * stick * env * o.Gain * 0.35f;
                }
                default: // звяк: два негармоничных тона, быстро гаснут
                {
                    o.PhaseA += 2f * Mathf.PI * o.Hz / SampleRate;
                    o.PhaseB += 2f * Mathf.PI * o.Hz2 / SampleRate;
                    float env = Mathf.Exp(-t * 7f) * Mathf.Min(1f, o.Pos / (SampleRate * 0.002f));
                    return (Mathf.Sin(o.PhaseA) + 0.6f * Mathf.Sin(o.PhaseB)) * env * o.Gain * 0.08f;
                }
            }
        }

        void Spawn(int kind, float gain, float delay)
        {
            ref var o = ref _voices[_next];
            _next = (_next + 1) % Voices;
            o = default;
            o.Kind = kind;
            o.Gain = gain * Rand(0.8f, 1.1f);
            o.Pan = _pan;
            o.Delay = (int)(delay * SampleRate);
            switch (kind)
            {
                case 0:
                    o.Length = (int)(Rand(0.12f, 0.22f) * SampleRate);
                    o.Hz = Rand(2200f, 4200f);
                    break;
                case 1:
                    o.Length = (int)(Rand(0.1f, 0.25f) * SampleRate);
                    o.Hz = Rand(700f, 1100f);
                    o.Hz2 = Rand(28f, 45f);
                    break;
                default:
                    o.Length = (int)(0.35f * SampleRate);
                    o.Hz = Rand(1700f, 2300f);
                    o.Hz2 = o.Hz * Rand(1.55f, 1.72f);
                    break;
            }
        }

        float Rand(float a, float b) => a + (b - a) * Rand01();
    }
}
