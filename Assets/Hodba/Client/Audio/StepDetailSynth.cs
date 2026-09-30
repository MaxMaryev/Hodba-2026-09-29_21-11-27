using Hodba.Client.Body;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Подробности шага поверх записей: осыпь пепла, когда нога шаркнула в рыхлом; щелчок, когда встала
    /// на камень; тяжёлый шорох, когда споткнулся. Всё — у той стопы, которая это сделала.
    /// </summary>
    public sealed class StepDetailSynth : ProceduralAudio
    {
        volatile int _scuffReq, _clickReq;
        volatile float _scuffGain, _scuffLen, _clickGain, _pan, _dull;
        int _scuffSeen, _clickSeen;

        int _scuffPos = -1, _scuffLength, _clickPos = -1, _clickLength;
        float _scuffG, _clickG, _clickHz, _clickPhase, _p, _low, _grain;

        public static StepDetailSynth Create(Transform parent) => Create<StepDetailSynth>(parent, "Step Detail Audio", 0x5C0FFu);

        public void OnStep(in StepEvent e, float volume)
        {
            if (!e.Felt) return;
            _pan = e.Left ? -0.12f : 0.12f;
            _dull = e.Looseness;
            if (e.Scuff)
            {
                _scuffGain = volume * (e.Stumble ? 1.5f : 0.6f) * Mathf.Lerp(0.5f, 1f, e.Looseness);
                _scuffLen = e.Stumble ? 0.45f : Random.Range(0.15f, 0.32f);
                _scuffReq++;
            }
            if (e.OnStone)
            {
                _clickGain = volume * 0.8f;
                _clickReq++;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_scuffReq != _scuffSeen)
            {
                _scuffSeen = _scuffReq;
                _scuffPos = 0;
                _scuffLength = (int)(_scuffLen * SampleRate);
                _scuffG = _scuffGain;
            }
            if (_clickReq != _clickSeen)
            {
                _clickSeen = _clickReq;
                _clickPos = 0;
                _clickLength = (int)(0.06f * SampleRate);
                _clickG = _clickGain;
                _clickHz = Mathf.Lerp(1800f, 3200f, Rand01());
                _clickPhase = 0f;
            }

            float pan = _pan;
            float a = OnePole(Mathf.Lerp(5000f, 2200f, _dull));
            for (int i = 0; i < data.Length; i += channels)
            {
                float s = 0f;
                if (_scuffPos >= 0 && _scuffPos < _scuffLength)
                {
                    // Осыпь: рыхлый шорох, который сыплется крупинками и стихает.
                    float t = _scuffPos / (float)_scuffLength;
                    float env = Mathf.Min(1f, t * 12f) * (1f - t) * (1f - t);
                    if (Rand01() < 0.004f) _grain = Mathf.Lerp(0.5f, 1.4f, Rand01());
                    _grain *= 0.998f;
                    float n = Noise();
                    _p = 0.97f * _p + 0.03f * n; // розоватый
                    _low += (n + _p * 4f - _low) * a;
                    s += _low * (0.25f + _grain) * env * _scuffG * 0.4f;
                    _scuffPos++;
                }
                if (_clickPos >= 0 && _clickPos < _clickLength)
                {
                    float t = _clickPos / (float)_clickLength;
                    _clickPhase += 2f * Mathf.PI * _clickHz / SampleRate;
                    s += (Mathf.Sin(_clickPhase) * 0.6f + Noise() * 0.4f) * Mathf.Exp(-t * 9f) * _clickG * 0.15f;
                    _clickPos++;
                }
                Write(data, i, channels, s * (1f - pan), s * (1f + pan));
            }
        }
    }
}
