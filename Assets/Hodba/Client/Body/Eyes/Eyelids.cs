using System.Collections.Generic;
using UnityEngine;

namespace Hodba.Client.Body
{
    public readonly struct EyelidState
    {
        /// <summary>0..1 — какую долю щели закрыло верхнее веко.</summary>
        public readonly float Upper;
        /// <summary>0..1 — какую долю щели закрыло нижнее веко.</summary>
        public readonly float Lower;
        /// <summary>0..1 — прищур.</summary>
        public readonly float Squint;
        /// <summary>0..1 — сомкнутость от моргания.</summary>
        public readonly float Blink;
        /// <summary>0..1 — сколько света проходит сквозь веки (против солнца — красное).</summary>
        public readonly float Glow;

        public EyelidState(float upper, float lower, float squint, float blink, float glow)
        {
            Upper = upper;
            Lower = lower;
            Squint = squint;
            Blink = blink;
            Glow = glow;
        }

        /// <summary>0..1 — какая доля щели открыта.</summary>
        public float Openness => (1f - Upper) * (1f - Lower);

        /// <summary>Веки совсем не видны — картинку можно не трогать.</summary>
        public bool FullyOpen => Upper < 0.002f && Lower < 0.002f;
    }

    /// <summary>
    /// Веки: моргание и прищур. Моргают нерегулярно (обычно через пару секунд, изредка — долго нет),
    /// закрываются быстро, открываются медленнее. Щурятся на причину — солнце, ветер в лицо, —
    /// и прищур дрожит, а не застывает. Раздражители подключаются снаружи (<see cref="IEyeIrritant"/>).
    /// </summary>
    public sealed class Eyelids
    {
        enum Phase { Open, Closing, Hold, Opening }

        readonly List<IEyeIrritant> _irritants = new List<IEyeIrritant>();
        readonly Signature _sig;
        readonly Rng _rng;
        readonly PinkNoise _flutter;
        readonly EventClock _next = new EventClock();

        Phase _phase;
        float _t, _blink, _squint, _depth = 0.9f, _close = 0.08f, _hold = 0.03f, _open = 0.18f;
        bool _scheduled, _wantBlink;

        public EyelidState State { get; private set; }

        public Eyelids(uint seed)
        {
            _sig = Signature.From(seed);
            _rng = new Rng(seed, Salts.Eyelids);
            _flutter = new PinkNoise(seed, Salts.Eyelids + 1, 0.2f, 4);
        }

        public void Add(IEyeIrritant irritant) => _irritants.Add(irritant);

        /// <summary>Моргнуть сейчас (спотыкание, большой перевод взгляда).</summary>
        public void Blink() => _wantBlink = true;

        public void OnBodyEvent(in BodyEvent e, in EyelidSettings s)
        {
            if (e.Kind == BodyEventKind.Stumble && _rng.Chance(s.stumbleBlink * e.Strength)) Blink();
        }

        /// <param name="view">Куда смотрят глаза, мировые оси.</param>
        public void Tick(in BodyContext ctx, Vector3 view, in EyelidSettings s)
        {
            float dt = ctx.Dt;
            if (!_scheduled)
            {
                _scheduled = true;
                _next.Schedule(Pause(s));
            }

            float squintTarget = 0f, extraRate = 0f, glow = 0f;
            foreach (var irritant in _irritants)
            {
                var i = irritant.Sense(ctx, view, s);
                squintTarget = Mathf.Max(squintTarget, i.Squint);
                extraRate += i.ExtraBlinkRate;
                glow = Mathf.Max(glow, i.Glow);
            }

            float tau = squintTarget > _squint ? s.squintRise : s.squintFall;
            if (dt > 0f) _squint = Mathf.Lerp(_squint, squintTarget, 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, tau)));

            float rate = _sig.BlinkRate * (1f + extraRate) * Mathf.Lerp(1f, s.focusBlinkRate, ctx.Focus);
            if (_phase == Phase.Open && (_next.Tick(dt, rate) || _wantBlink)) Start(s);
            _wantBlink = false;
            Animate(dt, s);

            float squint = Mathf.Clamp01(_squint * (1f + _flutter.Sample(ctx.Time) * s.squintFlutter * 2f));
            float upper = squint * s.squintUpper;
            upper = Mathf.Lerp(upper, Mathf.Max(upper, _depth), _blink);
            float lower = squint * s.squintLower + 0.12f * _blink;
            State = new EyelidState(Mathf.Clamp01(upper), Mathf.Clamp01(lower), squint, _blink, glow);
        }

        void Start(in EyelidSettings s)
        {
            _phase = Phase.Closing;
            _t = 0f;
            // Одно моргание глубже и дольше, другое мельче и быстрее — тоже не копии.
            _depth = Mathf.Clamp(s.blinkDepth + _rng.Signed() * 0.04f, 0.5f, 1f);
            _close = s.closeTime * _rng.Range(0.85f, 1.15f);
            _hold = s.holdTime * _rng.Range(0.6f, 1.4f);
            _open = s.openTime * _rng.Range(0.83f, 1.22f);
        }

        void Animate(float dt, in EyelidSettings s)
        {
            // Длинный кадр может пройти несколько фаз сразу.
            float left = dt;
            for (int guard = 0; guard < 4 && left > 0f && _phase != Phase.Open; guard++)
            {
                float length = Mathf.Max(0.005f, _phase == Phase.Closing ? _close : _phase == Phase.Hold ? _hold : _open);
                float need = (1f - _t) * length;
                float used = Mathf.Min(left, need);
                _t += used / length;
                left -= used;
                if (_t < 1f) break;

                _t = 0f;
                if (_phase == Phase.Closing) _phase = Phase.Hold;
                else if (_phase == Phase.Hold) _phase = Phase.Opening;
                else
                {
                    _phase = Phase.Open;
                    _next.Schedule(_rng.Chance(s.doubleBlink) ? _rng.Range(0.12f, 0.22f) : Pause(s));
                }
            }

            switch (_phase)
            {
                case Phase.Closing: _blink = _t * _t; break; // разгоняется
                case Phase.Hold: _blink = 1f; break;
                case Phase.Opening: _blink = (1f - _t) * (1f - _t); break; // открывается и дотягивает мягко
                default: _blink = 0f; break;
            }
        }

        float Pause(in EyelidSettings s) => Mathf.Clamp(_rng.LogNormal(s.blinkMedian, s.blinkSpread), 0.4f, 30f);
    }
}
