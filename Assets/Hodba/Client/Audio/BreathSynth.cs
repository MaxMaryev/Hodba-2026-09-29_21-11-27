using Hodba.Client.Body;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Дыхание. Та же фаза, что поднимает глаза на вдохе (<see cref="Exertion"/>): слышно и видно одно.
    /// На ровном почти не слышно, после подъёма — тяжело и через рот, и отпускает постепенно.
    /// Вдох короче, выше и тише, выдох длиннее, ниже и громче.
    /// </summary>
    public sealed class BreathSynth : ProceduralAudio
    {
        volatile float _targetPhase, _rate, _depth, _load, _volume;
        float _phase;
        BandPass _nose, _mouth;
        float _air;

        public static BreathSynth Create(Transform parent) => Create<BreathSynth>(parent, "Breath Audio", 0xB4EA7E1u);

        public void Set(in ExertionState ex, float volume)
        {
            _targetPhase = ex.BreathPhase;
            _rate = ex.BreathRate;
            _depth = ex.BreathDepth;
            _load = ex.Load;
            _volume = volume;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float rate = _rate, depth = _depth, load = _load, volume = _volume;
            float step = rate / SampleRate;

            // Фаза идёт своим ходом и мягко подтягивается к фазе тела — без щелчков на границах буфера.
            float err = Mathf.Repeat(_targetPhase - _phase + 0.5f, 1f) - 0.5f;
            float pull = err / Mathf.Max(1, data.Length / channels) * 0.5f;

            // На ровном почти тишина; с одышкой громче и уходит в рот (шире, ниже).
            float level = volume * depth * Mathf.Lerp(0.04f, 1f, load * load) * 0.25f;
            float mouth = Mathf.SmoothStep(0.2f, 0.8f, load);
            float smooth = OnePole(30f);

            for (int i = 0; i < data.Length; i += channels)
            {
                _phase += step + pull;
                _phase -= Mathf.Floor(_phase);

                bool inhale = _phase < Exertion.InhaleShare;
                float flow = inhale
                    ? 0.6f * Mathf.Sin(Mathf.PI * _phase / Exertion.InhaleShare)
                    : Mathf.Sin(Mathf.PI * (_phase - Exertion.InhaleShare) / (1f - Exertion.InhaleShare));
                _air += (flow - _air) * smooth;

                float n = Noise();
                float nose = _nose.Process(n, inhale ? 1800f : 1100f, 1.2f, SampleRate);
                float wide = _mouth.Process(n, inhale ? 900f : 550f, 1.6f, SampleRate);
                float s = Mathf.Lerp(nose, wide * 1.3f, mouth) * _air * level;
                Write(data, i, channels, s, s);
            }
        }
    }
}
