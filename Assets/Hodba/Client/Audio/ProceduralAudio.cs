using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Основа для звуков, которые мы пишем сами: источник на тишине, свой шум, запись в стерео.
    /// Тело звучит мелочами — дыхание, ткань, осыпь; каждая мелочь — свой наследник.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public abstract class ProceduralAudio : MonoBehaviour
    {
        protected int SampleRate = 48000;
        uint _rng = 0x9E3779B9u;

        protected static T Create<T>(Transform parent, string name, uint seed) where T : ProceduralAudio
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.loop = true;
            src.playOnAwake = false;
            src.clip = Silence.Clip;
            var audio = go.AddComponent<T>();
            audio.SampleRate = AudioSettings.outputSampleRate;
            audio._rng = seed | 1u;
            src.Play();
            return audio;
        }

        protected float Rand01()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng >> 8) * (1f / 16777216f);
        }

        /// <summary>Белый шум −1..1.</summary>
        protected float Noise() => Rand01() * 2f - 1f;

        /// <summary>Коэффициент однополюсного фильтра для частоты среза, Гц.</summary>
        protected float OnePole(float hz) => 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(1f, hz) / SampleRate);

        protected static void Write(float[] data, int i, int channels, float l, float r)
        {
            if (channels == 1)
            {
                data[i] = (l + r) * 0.5f;
                return;
            }
            data[i] = l;
            data[i + 1] = r;
            for (int c = 2; c < channels; c++) data[i + c] = 0f;
        }
    }

    /// <summary>
    /// Полосовой фильтр (state variable), один канал. Тембр дыхания, шороха ткани, скрипа лямки.
    /// </summary>
    public struct BandPass
    {
        float _low, _band;

        public float Process(float x, float centerHz, float q, int sampleRate)
        {
            float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(centerHz, sampleRate * 0.2f) / sampleRate);
            _low += f * _band;
            float high = x - _low - q * _band;
            _band += f * high;
            return _band;
        }
    }
}
