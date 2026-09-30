using System.Collections.Generic;
using Hodba.Sim.Walk;
using UnityEngine;

namespace Hodba.Client.Body
{
    public readonly struct EyeState
    {
        /// <summary>Смещение взгляда от головы, ° (плюс — вправо).</summary>
        public readonly float Yaw;
        /// <summary>Смещение взгляда от головы, ° (плюс — вниз).</summary>
        public readonly float Pitch;
        /// <summary>Как далеко то, на что смотрят, м.</summary>
        public readonly float FocusDistance;
        /// <summary>0..1 — насколько глаза сейчас живут сами (0 — уступили игроку).</summary>
        public readonly float Gain;
        public readonly GazeKind Kind;

        public EyeState(float yaw, float pitch, float focusDistance, float gain, GazeKind kind)
        {
            Yaw = yaw;
            Pitch = pitch;
            FocusDistance = focusDistance;
            Gain = gain;
            Kind = kind;
        }
    }

    /// <summary>
    /// Глаза отдельно от головы. Спокойный, немного рассеянный взгляд человека, которому ещё долго идти.
    /// Два движения складываются: маленький непрерывный дрейф (плывёт дугами, иногда задерживается)
    /// и редкий плавный перевод внимания — к дороге, к валуну, прочь от солнца, или просто к другой точке горизонта.
    /// Пустой горизонт — полноценное состояние внимания. Знакомое теряет вес (<see cref="Habituation"/>).
    /// Игрок главнее: ввод сразу ведёт взгляд, глаза уступают без скачка и никогда не меняют курс.
    /// </summary>
    public sealed class EyeWander
    {
        const int MaxAnchors = 4;

        public readonly Habituation Habituation = new Habituation();

        readonly List<IGazeTargetSource> _sources = new List<IGazeTargetSource>();
        readonly List<GazeCandidate> _candidates = new List<GazeCandidate>(32);
        readonly List<GazeCandidate> _anchors = new List<GazeCandidate>(MaxAnchors);
        readonly BodyEvents _events;
        readonly Signature _sig;
        readonly Rng _rng;
        readonly PinkNoise _driftX, _driftY;
        readonly Follower _center = new Follower();
        readonly EventClock _shift = new EventClock();
        readonly EventClock _pace = new EventClock();

        GazeCandidate _target;
        bool _hasTarget, _dwelling, _wantGround, _wasLooking, _paced;
        double _driftTime;
        float _driftSpeed = 1f, _gain = 1f, _resumeIn, _sinceCollect, _focusDistance = 30f;
        Vector2 _output;

        public EyeState State { get; private set; }
        /// <summary>Сколько раз внимание переводилось на новую цель — для отладки.</summary>
        public int Shifts { get; private set; }

        public EyeWander(uint seed, BodyEvents events)
        {
            _events = events;
            _sig = Signature.From(seed);
            _rng = new Rng(seed, Salts.EyeWander);
            _driftX = new PinkNoise(seed, Salts.EyeWander + 1, 0.04f, 4);
            _driftY = new PinkNoise(seed, Salts.EyeWander + 2, 0.04f, 4);
        }

        public void Add(IGazeTargetSource source) => _sources.Add(source);

        public void OnBodyEvent(in BodyEvent e, in EyeWanderSettings s)
        {
            // Не на каждый камень: часто тело справляется без дополнительного внимания.
            if (e.Kind == BodyEventKind.StoneAvoided && _rng.Chance(s.obstacleLook)) _wantGround = true;
            if (e.Kind == BodyEventKind.Stumble) _wantGround = true;
        }

        /// <summary>
        /// Игрок взял взгляд. Отдаёт текущее смещение глаз (его переносят в голову — картинка не двигается)
        /// и замолкает до конца ввода.
        /// </summary>
        public Vector2 YieldToPlayer()
        {
            var o = _output;
            _output = Vector2.zero;
            _center.Reset(Vector2.zero);
            _gain = 0f;
            _hasTarget = false;
            State = new EyeState(0f, 0f, _focusDistance, 0f, GazeKind.Nothing);
            return o;
        }

        public void Tick(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, float duck)
        {
            float dt = ctx.Dt;
            // Всмотрелся — глаза замирают на месте.
            float live = dt * (1f - Mathf.Clamp01(ctx.Focus));
            _sinceCollect += dt;

            UpdateGain(ctx.LookInput, dt, s);

            if (!_paced)
            {
                _paced = true;
                _pace.Schedule(_rng.LogNormal(s.moveMedian, s.dwellSpread));
            }

            var targetOffset = _hasTarget ? OffsetOf(_target, ctx, s) : Vector2.zero;
            bool lost = _hasTarget && (Mathf.Abs(targetOffset.x) >= s.maxYaw || targetOffset.y >= s.maxDown || targetOffset.y <= -s.maxUp);
            if (_gain > 0f && (_shift.Tick(live) || _wantGround || !_hasTarget || lost))
            {
                Choose(ctx, inputs, s, lost);
                targetOffset = OffsetOf(_target, ctx, s);
            }

            // Дрейф: плывёт, иногда задерживается; время шума течёт то быстрее, то почти стоит.
            if (_pace.Tick(live))
            {
                _dwelling = !_dwelling;
                _pace.Schedule(_rng.LogNormal(_dwelling ? s.dwellMedian : s.moveMedian, s.dwellSpread));
            }
            float speedTarget = _dwelling ? s.dwellSpeed : 1f;

            var drift = new Vector2(_driftX.Sample(_driftTime) * s.driftYaw, _driftY.Sample(_driftTime) * s.driftPitch)
                        * Mathf.Lerp(0.7f, 1.3f, _sig.Restlessness) * (1f - 0.7f * Mathf.Clamp01(duck));

            // Предмет рядом слегка притягивает дрейф и замедляет его.
            var gaze = _center.Value + drift;
            float pull = 0f;
            Vector2 toAnchor = Vector2.zero;
            foreach (var a in _anchors)
            {
                var d = OffsetOf(a, ctx, s) - gaze;
                float w = Mathf.Clamp01(1f - d.magnitude / 4f) * Mathf.Clamp01(a.Weight * 2f);
                if (w <= pull) continue;
                pull = w;
                toAnchor = d;
            }
            drift += toAnchor * (s.attraction * pull);
            speedTarget *= 1f - s.attraction * pull;

            if (live > 0f) _driftSpeed = Mathf.Lerp(_driftSpeed, speedTarget, 1f - Mathf.Exp(-live / 0.5f));
            _driftTime += live * _driftSpeed;

            _center.Step(targetOffset, live, s.centerHz, s.centerDamping, s.centerMaxSpeed);

            var raw = _center.Value + drift;
            _output = new Vector2(
                SoftLimit.Apply(raw.x, s.maxYaw),
                raw.y >= 0f ? SoftLimit.Apply(raw.y, s.maxDown) : SoftLimit.Apply(raw.y, s.maxUp)) * _gain;

            if (live > 0f)
                _focusDistance = Mathf.Lerp(_focusDistance, _hasTarget ? _target.Distance : 30f, 1f - Mathf.Exp(-live / 0.4f));
            State = new EyeState(_output.x, _output.y, _focusDistance, _gain, _hasTarget ? _target.Kind : GazeKind.Nothing);
        }

        void UpdateGain(bool looking, float dt, in EyeWanderSettings s)
        {
            if (looking)
            {
                _gain = 0f;
                _wasLooking = true;
                return;
            }
            if (_wasLooking)
            {
                // Отпустил — глаза оживают не сразу и не рывком.
                _wasLooking = false;
                _resumeIn = Mathf.Clamp(_rng.LogNormal(s.resumeMedian, s.resumeSpread), 1.5f, 10f);
                _center.Reset(Vector2.zero);
                _hasTarget = false;
            }
            if (_resumeIn > 0f)
            {
                _resumeIn -= dt;
                return;
            }
            _gain = Mathf.MoveTowards(_gain, 1f, dt / Mathf.Max(0.05f, s.resumeRamp));
        }

        void Choose(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, bool lost)
        {
            Habituation.Tick(_sinceCollect, s);
            _sinceCollect = 0f;

            _candidates.Clear();
            foreach (var src in _sources) src.Collect(ctx, inputs, s, Habituation, _rng, _candidates);

            _anchors.Clear();
            foreach (var c in _candidates)
                if ((c.Kind == GazeKind.Stone || c.Kind == GazeKind.Boulder) && _anchors.Count < MaxAnchors) _anchors.Add(c);

            GazeCandidate next = default;
            bool picked = false, stay = false;

            if (_wantGround)
            {
                _wantGround = false;
                foreach (var c in _candidates)
                    if (c.Kind == GazeKind.Ground) { next = c; picked = true; break; }
            }

            if (!picked)
            {
                // Остаться можно только там, куда глаза ещё достают: за ушедшей из поля целью не держатся.
                float stayWeight = _hasTarget && !lost ? s.stayWeight : 0f;
                float total = stayWeight;
                foreach (var c in _candidates) total += Mathf.Max(0f, c.Weight);
                float r = _rng.Next01() * total;
                if (r < stayWeight) stay = true;
                else
                {
                    r -= stayWeight;
                    foreach (var c in _candidates)
                    {
                        r -= Mathf.Max(0f, c.Weight);
                        if (r > 0f) continue;
                        next = c;
                        picked = true;
                        break;
                    }
                }
            }

            if (stay || !picked)
            {
                // Задержался там, где был, — даже если там ничего нет.
                _shift.Schedule(_rng.LogNormal(s.shiftMedian, s.shiftSpread));
                if (_hasTarget && !lost) return;
                next = new GazeCandidate(GazeKind.Nothing, GazeSpace.Head, 0f, 0f, 0f, 30f, s.shiftMedian);
            }

            var before = _center.Value;
            _target = next;
            _hasTarget = true;
            Shifts++;
            if (next.Kind == GazeKind.Stone || next.Kind == GazeKind.Boulder) Habituation.Fixate(next.Kind, s);
            _shift.Schedule(Mathf.Clamp(_rng.LogNormal(next.Dwell, s.shiftSpread), 0.4f, 60f));

            float jump = (OffsetOf(next, ctx, s) - before).magnitude;
            if (jump > s.shiftBlinkAngle)
                _events?.Emit(new BodyEvent(BodyEventKind.GazeShift, Mathf.Clamp01(jump / (s.shiftBlinkAngle * 3f))));
        }

        /// <summary>Где цель относительно головы сейчас, °: мировые цели держатся, пока человек идёт.</summary>
        static Vector2 OffsetOf(in GazeCandidate c, in BodyContext ctx, in EyeWanderSettings s)
        {
            float yaw, pitch;
            switch (c.Space)
            {
                case GazeSpace.World:
                    yaw = WalkSim.DeltaAngle(ctx.HeadYaw, c.Yaw);
                    pitch = c.Pitch - ctx.HeadPitch;
                    break;
                case GazeSpace.Body:
                    yaw = WalkSim.DeltaAngle(ctx.HeadYaw, ctx.Sim.Course + c.Yaw);
                    pitch = c.Pitch - ctx.HeadPitch;
                    break;
                default:
                    yaw = c.Yaw;
                    pitch = c.Pitch;
                    break;
            }
            return new Vector2(Mathf.Clamp(yaw, -s.maxYaw, s.maxYaw), Mathf.Clamp(pitch, -s.maxUp, s.maxDown));
        }
    }
}
