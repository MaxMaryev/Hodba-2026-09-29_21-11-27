using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Ветер — главный инструмент игры (Docs/Design/15-sound.md). Пока нет записей, он синтезируется:
    /// коричневый шум через полосовой фильтр, центр которого ходит за порывами, плюс шипение пепла.
    /// Стерео — по тому, откуда дует относительно головы. В затишье почти ничего не слышно, так и надо.
    /// Если в конфиге есть настоящая петля ветра — синтез выключается, и звучат записи, смешанные по погоде:
    /// ровный ветер, поверх него на порывах ветер с порывами и шорох пепла, который растёт с силой.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class WindSynth : MonoBehaviour
    {
        volatile float _strength, _gust, _side, _volume;
        bool _useClip;
        int _sampleRate = 48000;
        uint _rng = 0x9E3779B9u;

        // Слои записей.
        AudioSource _calm, _gusty, _hiss;

        // Состояние фильтров по каналам.
        float _brownL, _brownR;
        float _lowL, _bandL, _lowR, _bandR;
        float _hissL, _hissR;
        float _center = 400f;

        public static WindSynth Create(Transform parent, AudioClip loop, AudioClip gustLoop = null, AudioClip hissLoop = null)
        {
            var go = new GameObject("Wind Audio");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.loop = true;
            src.playOnAwake = false;
            var synth = go.AddComponent<WindSynth>();
            synth._sampleRate = AudioSettings.outputSampleRate;
            synth._useClip = loop != null;
            if (loop == null)
            {
                src.clip = Silence.Clip;
                src.Play();
                return synth;
            }
            synth._calm = Layer(src, loop);
            if (gustLoop != null) synth._gusty = Layer(Child(go.transform, "Wind Gusts"), gustLoop);
            if (hissLoop != null) synth._hiss = Layer(Child(go.transform, "Ash Hiss"), hissLoop);
            return synth;
        }

        /// <param name="side">−1 — ветер слева, +1 — справа.</param>
        public void Set(float strength, float gust, float side, float volume, float hissVolume = 0.4f)
        {
            _strength = strength;
            _gust = gust;
            _side = side;
            _volume = volume;
            if (!_useClip) return;

            float level = volume * Mathf.Lerp(0.15f, 1f, strength);
            float pan = Mathf.Clamp(side * 0.45f, -1f, 1f);
            // Порыв ложится поверх ровного ветра: подложка под ним стихает не больше чем на 4 дБ.
            float g = _gusty != null ? gust : 0f;
            Mix(_calm, level * Mathf.Sqrt(1f - 0.6f * g), pan);
            if (_gusty != null) Mix(_gusty, level * Mathf.Sqrt(g), pan);
            if (_hiss != null) Mix(_hiss, volume * hissVolume * strength * strength, pan);
        }

        static AudioSource Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            return src;
        }

        static AudioSource Layer(AudioSource src, AudioClip clip)
        {
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.Play();
            // Каждый раз с другого места петли: иначе первые минуты игры всегда звучат одинаково.
            src.timeSamples = Random.Range(0, clip.samples);
            return src;
        }

        static void Mix(AudioSource src, float volume, float pan)
        {
            src.volume = volume;
            src.panStereo = pan;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_useClip) return;

            float strength = _strength, gust = _gust, side = _side, volume = _volume;
            float gainL = volume * (1f - 0.45f * side);
            float gainR = volume * (1f + 0.45f * side);

            float targetCenter = Mathf.Lerp(180f, 900f, gust * 0.7f + strength * 0.3f);
            float bandLevel = Mathf.Lerp(0.05f, 1f, strength * strength) * 2.2f;
            float hissLevel = strength * strength * strength * 0.08f;
            float q = 0.9f;

            for (int i = 0; i < data.Length; i += channels)
            {
                _center += (targetCenter - _center) * 0.00005f;
                float f = 2f * Mathf.Sin(Mathf.PI * _center / _sampleRate);

                float wl = Noise(), wr = Noise();
                _brownL = (_brownL + wl * 0.04f) * 0.996f;
                _brownR = (_brownR + wr * 0.04f) * 0.996f;

                // Полосовой фильтр (state variable).
                _lowL += f * _bandL; float highL = _brownL - _lowL - q * _bandL; _bandL += f * highL;
                _lowR += f * _bandR; float highR = _brownR - _lowR - q * _bandR; _bandR += f * highR;

                // Шипение: белый шум минус его медленная часть.
                _hissL += (wl - _hissL) * 0.15f;
                _hissR += (wr - _hissR) * 0.15f;
                float hl = (wl - _hissL) * hissLevel, hr = (wr - _hissR) * hissLevel;

                float l = (_bandL * bandLevel + hl) * gainL;
                float r = (_bandR * bandLevel + hr) * gainR;

                if (channels == 1) data[i] = (l + r) * 0.5f;
                else
                {
                    data[i] = l;
                    data[i + 1] = r;
                    for (int c = 2; c < channels; c++) data[i + c] = 0f;
                }
            }
        }

        float Noise()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng >> 8) * (2f / 16777216f) - 1f;
        }
    }

    /// <summary>Тишина для источников, звук которых мы пишем сами.</summary>
    static class Silence
    {
        static AudioClip _clip;

        public static AudioClip Clip
        {
            get
            {
                if (_clip != null) return _clip;
                int rate = AudioSettings.outputSampleRate;
                _clip = AudioClip.Create("Silence", rate, 2, rate, false);
                _clip.SetData(new float[rate * 2], 0);
                return _clip;
            }
        }
    }
}
