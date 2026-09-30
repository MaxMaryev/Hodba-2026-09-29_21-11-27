using Hodba.Client.Body;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Шаги по пеплу. Слышны часами, поэтому ни один не повторяет другой.
    /// Звучит тот же шаг, что качнул тело (<see cref="StepEvent"/>): сила опоры — громкость,
    /// поверхность под этой стопой — тембр (рыхлое глуше и ниже), а через долю секунды — перекат подошвы.
    /// Синтез: мягкий хруст (шум с зернистой огибающей и плавающим фильтром), глухой удар пятки и перекат.
    /// Если в конфиге есть настоящие записи — играют они: у каждой ноги свой источник, чтобы
    /// высота и тембр шага не менялись, пока звучит предыдущий, и один клип не звучит дважды подряд.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FootstepSynth : MonoBehaviour
    {
        /// <summary>Перекат подошвы — после удара, тише и выше.</summary>
        const float RollDelayMin = 0.08f, RollDelayMax = 0.12f, RollGain = 0.22f;

        AudioSource _source, _leftSource, _rollRight, _rollLeft;
        AudioLowPassFilter _lowRight, _lowLeft;
        AudioClip[] _clips;
        int _lastClip = -1;
        int _sampleRate = 48000;
        uint _rng = 0x2545F491u;

        volatile int _requested;
        volatile float _reqGain, _reqPan, _reqCutoff, _reqPitch;
        int _handled;

        // Текущий шаг.
        int _pos = -1, _length, _rollAt;
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
                synth._lowRight = go.AddComponent<AudioLowPassFilter>();
                synth._leftSource = Foot(go.transform, "Left Foot", out synth._lowLeft);
                synth._rollRight = Foot(go.transform, "Right Roll", out _);
                synth._rollLeft = Foot(go.transform, "Left Roll", out _);
            }
            return synth;
        }

        static AudioSource Foot(Transform parent, string name, out AudioLowPassFilter low)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            low = go.AddComponent<AudioLowPassFilter>();
            return s;
        }

        public void Trigger(in StepEvent e, in SurfaceFeel feel, float volume)
        {
            float gain = volume * Mathf.Clamp(e.Force, 0.2f, 1.6f) * feel.soundGain;
            float pan = e.Left ? -0.12f : 0.12f;
            float pitch = feel.soundPitch * (e.Force < 0.5f ? 1.04f : 1f);
            // Рыхлое глохнет сильнее, чем обещает табличная строка: нога уходит в пепел.
            float cutoff = feel.soundCutoff * Mathf.Lerp(1f, 0.7f, e.Looseness);

            if (_clips != null)
            {
                var src = e.Left ? _leftSource : _source;
                var low = e.Left ? _lowLeft : _lowRight;
                low.cutoffFrequency = cutoff;
                src.pitch = pitch * Random.Range(0.94f, 1.06f);
                src.panStereo = pan;
                src.PlayOneShot(NextClip(), gain);

                var roll = e.Left ? _rollLeft : _rollRight;
                roll.clip = NextClip();
                roll.pitch = pitch * Random.Range(1.1f, 1.22f);
                roll.panStereo = pan;
                roll.volume = gain * RollGain;
                roll.PlayScheduled(AudioSettings.dspTime + Random.Range(RollDelayMin, RollDelayMax));
                return;
            }
            _reqGain = gain;
            _reqPan = pan;
            _reqCutoff = cutoff;
            _reqPitch = pitch;
            _requested++;
        }

        AudioClip NextClip()
        {
            int index = Random.Range(0, _clips.Length);
            if (index == _lastClip && _clips.Length > 1) index = (index + Random.Range(1, _clips.Length)) % _clips.Length;
            _lastClip = index;
            return _clips[index];
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_clips != null) return;

            if (_requested != _handled)
            {
                _handled = _requested;
                _pos = 0;
                _length = (int)(_sampleRate * Mathf.Lerp(0.18f, 0.26f, Rand01()));
                _rollAt = (int)(_sampleRate * Mathf.Lerp(RollDelayMin, RollDelayMax, Rand01()));
                _gain = _reqGain * Mathf.Lerp(0.8f, 1.1f, Rand01());
                _pan = _reqPan;
                _cutoff = Mathf.Min(_reqCutoff * 0.45f, 12000f) * Mathf.Lerp(0.8f, 1.2f, Rand01());
                _thumpFreq = Mathf.Lerp(65f, 95f, Rand01()) * _reqPitch;
                _thumpPhase = 0f;
            }

            float a = 1f - Mathf.Exp(-2f * Mathf.PI * _cutoff / _sampleRate);
            for (int i = 0; i < data.Length; i += channels)
            {
                float s = 0f;
                if (_pos >= 0 && _pos < _length)
                {
                    float t = _pos / (float)_length;
                    float heel = Mathf.Min(1f, _pos / (_sampleRate * 0.006f)) * Mathf.Exp(-t * 9f);
                    // Перекат: второй, тихий бугорок огибающей.
                    float r = (_pos - _rollAt) / (_sampleRate * 0.05f);
                    float roll = r > 0f ? RollGain * r * Mathf.Exp(1f - r) : 0f;
                    float env = heel + roll;

                    // Зёрна: пепел хрустит не ровно, а крупинками.
                    if (Rand01() < 0.02f) _grain = Mathf.Lerp(0.3f, 1f, Rand01());
                    _grain *= 0.995f;

                    float n = Rand01() * 2f - 1f;
                    _low += (n - _low) * a;
                    float crunch = _low * (0.35f + _grain) * env;

                    _thumpPhase += 2f * Mathf.PI * _thumpFreq / _sampleRate;
                    float thump = Mathf.Sin(_thumpPhase) * Mathf.Exp(-t * 25f) * 0.5f;

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
