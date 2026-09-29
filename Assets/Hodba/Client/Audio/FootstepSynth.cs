using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Шаги по пеплу. Слышны часами, поэтому ни один не повторяет другой.
    /// Синтез: мягкий хруст (шум с зернистой огибающей и плавающим фильтром) и глухой удар пятки.
    /// Если в конфиге есть настоящие записи — играют они: у каждой ноги свой источник, чтобы
    /// высота и панорама шага не менялись, пока звучит предыдущий, и один клип не звучит дважды подряд.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FootstepSynth : MonoBehaviour
    {
        AudioSource _source, _leftSource;
        AudioClip[] _clips;
        int _lastClip = -1;
        int _sampleRate = 48000;
        uint _rng = 0x2545F491u;

        volatile int _requested;
        volatile float _reqGain, _reqPan;
        int _handled;

        // Текущий шаг.
        int _pos = -1, _length;
        float _gain, _pan, _cutoff, _low, _thumpPhase, _thumpFreq, _grain;

        public static FootstepSynth Create(Transform parent, AudioClip[] clips)
        {
            var go = new GameObject("Footsteps Audio");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.loop = true;
            src.playOnAwake = false;
            var synth = go.AddComponent<FootstepSynth>();
            synth._source = src;
            synth._sampleRate = AudioSettings.outputSampleRate;
            synth._clips = clips != null && clips.Length > 0 ? clips : null;
            if (synth._clips == null)
            {
                src.clip = Silence.Clip;
                src.Play();
            }
            else
            {
                src.loop = false;
                var left = new GameObject("Left Foot");
                left.transform.SetParent(go.transform, false);
                synth._leftSource = left.AddComponent<AudioSource>();
                synth._leftSource.spatialBlend = 0f;
                synth._leftSource.playOnAwake = false;
            }
            return synth;
        }

        public void Trigger(bool left, float gain)
        {
            float pan = left ? -0.12f : 0.12f;
            if (_clips != null)
            {
                int index = Random.Range(0, _clips.Length);
                if (index == _lastClip && _clips.Length > 1) index = (index + Random.Range(1, _clips.Length)) % _clips.Length;
                _lastClip = index;
                var src = left ? _leftSource : _source;
                src.pitch = Random.Range(0.94f, 1.06f);
                src.panStereo = pan;
                src.PlayOneShot(_clips[index], gain);
                return;
            }
            _reqGain = gain;
            _reqPan = pan;
            _requested++;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_clips != null) return;

            if (_requested != _handled)
            {
                _handled = _requested;
                _pos = 0;
                _length = (int)(_sampleRate * Mathf.Lerp(0.09f, 0.15f, Rand01()));
                _gain = _reqGain * Mathf.Lerp(0.8f, 1.1f, Rand01());
                _pan = _reqPan;
                _cutoff = Mathf.Lerp(1800f, 3800f, Rand01());
                _thumpFreq = Mathf.Lerp(65f, 95f, Rand01());
                _thumpPhase = 0f;
            }

            float a = 1f - Mathf.Exp(-2f * Mathf.PI * _cutoff / _sampleRate);
            for (int i = 0; i < data.Length; i += channels)
            {
                float s = 0f;
                if (_pos >= 0 && _pos < _length)
                {
                    float t = _pos / (float)_length;
                    float env = Mathf.Min(1f, _pos / (_sampleRate * 0.006f)) * Mathf.Exp(-t * 5f);

                    // Зёрна: пепел хрустит не ровно, а крупинками.
                    if (Rand01() < 0.02f) _grain = Mathf.Lerp(0.3f, 1f, Rand01());
                    _grain *= 0.995f;

                    float n = Rand01() * 2f - 1f;
                    _low += (n - _low) * a;
                    float crunch = _low * (0.35f + _grain) * env;

                    _thumpPhase += 2f * Mathf.PI * _thumpFreq / _sampleRate;
                    float thump = Mathf.Sin(_thumpPhase) * Mathf.Exp(-t * 14f) * 0.5f;

                    s = (crunch + thump) * _gain;
                    _pos++;
                }

                if (channels == 1) data[i] = s;
                else
                {
                    data[i] = s * (1f - _pan);
                    data[i + 1] = s * (1f + _pan);
                    for (int c = 2; c < channels; c++) data[i + c] = 0f;
                }
            }
        }

        float Rand01()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng >> 8) * (1f / 16777216f);
        }
    }
}
