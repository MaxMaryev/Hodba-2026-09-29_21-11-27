using System;
using Hodba.Core;
using Hodba.World;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Походка, собранная пошагово. У каждого шага своя длина, высота, раскачка и момент верхней точки;
    /// параметры плавно перетекают через границу шага, поэтому ни щелчков, ни копий.
    /// Характер складывается слоями: подпись человека + медленный 1/f-дрейф + небольшой разброс шага.
    /// Сильные отклонения — только с причиной: земля под этой стопой, уклон, камень, трогание и остановка.
    /// </summary>
    public sealed class Gait : IMotionLayer
    {
        /// <summary>Один шаг: как он пойдёт от удара одной пятки до удара другой.</summary>
        struct Shape
        {
            public float Length, Amp, Sway, Roll, Pitch, Surge, Skew;

            public static Shape Lerp(in Shape a, in Shape b, float t) => new Shape
            {
                Length = Mathf.Lerp(a.Length, b.Length, t),
                Amp = Mathf.Lerp(a.Amp, b.Amp, t),
                Sway = Mathf.Lerp(a.Sway, b.Sway, t),
                Roll = Mathf.Lerp(a.Roll, b.Roll, t),
                Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t),
                Surge = Mathf.Lerp(a.Surge, b.Surge, t),
                Skew = Mathf.Lerp(a.Skew, b.Skew, t),
            };
        }

        /// <summary>Касание земли, пока не отданное наружу.</summary>
        struct Touchdown
        {
            public bool Left;
            public WorldPos Contact;
            public SurfaceFeel Feel;
            public SurfaceKind Kind;
            public float Looseness;
            public bool OnStone, Stumble;
            public float Roughness;
            /// <summary>Высота земли под этой стопой, м.</summary>
            public float Height;
        }

        const int MaxStepsPerFrame = 64;

        readonly Signature _sig;
        readonly Rng _rng;
        readonly PinkNoise _driftAmp, _driftLen, _driftSway;
        readonly BodyEvents _events;
        readonly IObstacleQuery _obstacles;
        readonly Transitions _transitions = new Transitions();

        readonly Spring _impactUp = new Spring(), _impactPitch = new Spring();
        readonly Spring _eventUp = new Spring(), _eventPitch = new Spring(), _eventRoll = new Spring();
        readonly Spring _lean = new Spring(), _sink = new Spring();
        readonly Spring _support = new Spring(), _footRoll = new Spring(), _footPitch = new Spring();

        Shape _prev, _cur;
        double _stepStart;
        bool _started, _stanceLeft = true;
        int _firstSteps, _disturb;
        bool _planStone, _planStumble;
        float _blend, _lastSpeed, _sinkDepth, _event, _gearTimer = -1f;
        float _groundCaution, _aheadChange;
        float _heightLeft, _heightRight, _supportTarget;

        public MotionRole Role => MotionRole.Rhythm;
        public PoseDelta Pose { get; private set; }
        public float EventStrength => _event;
        public GaitState State { get; private set; }

        public Gait(uint seed, BodyEvents events, IObstacleQuery obstacles)
        {
            _sig = Signature.From(seed);
            _rng = new Rng(seed, Salts.GaitRhythm);
            _driftAmp = new PinkNoise(seed, Salts.GaitDrift, 0.004f, 8);
            _driftLen = new PinkNoise(seed, Salts.GaitDrift + 1, 0.004f, 8);
            _driftSway = new PinkNoise(seed, Salts.GaitDrift + 2, 0.004f, 8);
            _events = events;
            _obstacles = obstacles;
        }

        /// <summary>После телепорта или долгого отсутствия: шаги считаются заново, без шквала.</summary>
        public void Reset(double distance, float groundHeight)
        {
            _stepStart = distance;
            _planStone = _planStumble = false;
            SnapSupport(groundHeight);
        }

        /// <summary>Обе стопы — на земле под человеком (старт, телепорт).</summary>
        void SnapSupport(float height)
        {
            _heightLeft = _heightRight = _supportTarget = height;
            _support.Reset(height);
        }

        public void Tick(in BodyContext ctx, in GaitSettings s, in ExertionState ex)
        {
            var sim = ctx.Sim;
            float dt = ctx.Dt;
            float stepLength = Mathf.Max(0.1f, sim.Params.StepLength);

            if (!_started)
            {
                _started = true;
                _stepStart = sim.Distance;
                _cur = _prev = Standing(stepLength);
                _lastSpeed = sim.Speed;
                SnapSupport(ctx.World.SampleHeightMm(sim.Position.X, sim.Position.Z) / 1000f);
            }

            float accel = dt > 0f ? (sim.Speed - _lastSpeed) / dt : 0f;
            _lastSpeed = sim.Speed;
            _blend = Mathf.MoveTowards(_blend, ctx.SpeedNorm, dt * 2f);

            var ahead = Anticipation.Look(ctx, s.lookAhead);
            _aheadChange = ahead.Change(sim.Slope);

            var flags = _transitions.Tick(sim, s.shuffleAngle);
            if (flags.Started) OnStart(ctx, s, ex, ahead, stepLength);
            if (flags.StopRequested) Bump(0.3f, BodyEventKind.Stop);
            if (flags.Settled) OnSettle(ctx, s);
            if (flags.Shuffle) OnShuffle(ctx, s);

            // Шаги по пройденному пути. За длинный кадр может пройти несколько — прочувствован только последний.
            double d = sim.Distance;
            for (int i = 0; i < MaxStepsPerFrame && d - _stepStart >= _cur.Length; i++)
            {
                _stepStart += _cur.Length;
                var touch = Land(ctx, s, d);
                _prev = _cur;
                _stanceLeft = touch.Left;
                _cur = Next(ctx, s, ex, touch.Feel, touch.Roughness, ahead, stepLength);
                Plan(ctx, s, ex, touch.Looseness, d);
                bool more = d - _stepStart >= _cur.Length;
                Commit(ctx, s, touch, felt: !more);
            }

            // Снаряжение догоняет тело чуть позже остановки.
            if (_gearTimer > 0f)
            {
                _gearTimer -= dt;
                if (_gearTimer <= 0f) _eventPitch.Kick(s.gearNod);
            }

            Pose = Compose(ctx, s, d, accel);
            _event *= Mathf.Exp(-dt / 0.6f);

            float freq = _cur.Length > 0f ? sim.Speed / _cur.Length : 0f;
            float u = Phase(d);
            State = new GaitState(_blend, u, _stanceLeft, freq, _groundCaution, _aheadChange, _lean.Value, _event, _support.Value);
        }

        float Phase(double d) => _cur.Length > 0f ? Mathf.Clamp01((float)((d - _stepStart) / _cur.Length)) : 0f;

        PoseDelta Compose(in BodyContext ctx, in GaitSettings s, double d, float accel)
        {
            var sim = ctx.Sim;
            float dt = ctx.Dt;
            float u = Phase(d);
            var shape = Shape.Lerp(_prev, _cur, u * u * (3f - 2f * u));

            // Верх в середине опоры, низ — в двойной опоре. Перекос сдвигает верхнюю точку раньше или позже.
            float w = u + shape.Skew * Mathf.Sin(Mathf.PI * u);
            float c = Mathf.Cos(2f * Mathf.PI * w);
            float stance = _stanceLeft ? -1f : 1f;
            float sway = Mathf.Sin(Mathf.PI * u);

            float up = -0.5f * shape.Amp * c * _blend;
            float side = stance * shape.Sway * sway * _blend;
            float roll = stance * shape.Roll * sway * _blend;
            float pitch = 0.5f * shape.Pitch * c * _blend;
            float surge = -shape.Surge * Mathf.Sin(2f * Mathf.PI * w) * _blend;

            // Рыхлое: нога проседает не сразу, а чуть после удара, и отпускает к отталкиванию.
            _sink.Step(u < 0.45f ? -_sinkDepth * _blend : 0f, dt, 4f, 0.9f);
            _impactUp.Step(0f, dt, s.impactHz, 0.45f);
            _impactPitch.Step(0f, dt, s.impactHz, 0.45f);
            _eventUp.Step(0f, dt, s.eventHz, 0.5f);
            _eventPitch.Step(0f, dt, s.eventHz, 0.5f);
            _eventRoll.Step(0f, dt, s.eventHz, 0.5f);

            float lean = sim.Slope * s.slopeLean + Mathf.Clamp(accel, -3f, 3f) * s.accelLean;
            _lean.Step(lean, dt, s.leanHz, 0.7f);

            // Неровная земля: тело стоит на опорной стопе и переходит на новую за долю шага; разница высот
            // левой и правой стопы кренит корпус, опорной и прошлой — чуть наклоняет. Тело это частично гасит.
            _support.Step(_supportTarget, dt, s.supportHz, 0.9f);
            float across = Mathf.Atan2(_heightRight - _heightLeft, 2f * Mathf.Max(0.05f, s.footOffset)) * Mathf.Rad2Deg;
            float stanceH = _stanceLeft ? _heightLeft : _heightRight, otherH = _stanceLeft ? _heightRight : _heightLeft;
            float along = Mathf.Atan2(stanceH - otherH, Mathf.Max(0.2f, _cur.Length)) * Mathf.Rad2Deg;
            _footRoll.Step(-across * s.footRoll, dt, s.leanHz * 2f, 0.8f);
            _footPitch.Step(-along * s.footPitch, dt, s.leanHz * 2f, 0.8f);

            roll += -sim.TurnRate * s.turnLean + _sig.HeadTilt;

            return new PoseDelta(
                up + _sink.Value + _impactUp.Value + _eventUp.Value,
                side,
                surge,
                pitch + _impactPitch.Value + _eventPitch.Value + _lean.Value + _footPitch.Value,
                0f,
                roll + _eventRoll.Value + _footRoll.Value);
        }

        Touchdown Land(in BodyContext ctx, in GaitSettings s, double d)
        {
            bool left = !_stanceLeft;
            // Тело уже прошло дальше границы шага (длинный кадр): стопа осталась позади на эту разницу.
            float reach = s.footReach * _cur.Length - (float)(d - _stepStart);
            var contact = FootPoint(ctx, left, reach, s.footOffset);
            var surface = ctx.World.SampleSurface(contact.X, contact.Z);

            var touch = new Touchdown
            {
                Left = left,
                Contact = contact,
                Kind = surface.Kind,
                Feel = SurfaceFeel.Find(s.surfaces, surface.Kind),
                Looseness = surface.Looseness / 65535f,
                Roughness = surface.Roughness / 65536f,
                Height = ctx.World.SampleHeightMm(contact.X, contact.Z) / 1000f,
                OnStone = _planStone,
                Stumble = _planStumble,
            };
            _planStone = _planStumble = false;
            return touch;
        }

        void Commit(in BodyContext ctx, in GaitSettings s, in Touchdown t, bool felt)
        {
            var sim = ctx.Sim;
            float force = Mathf.Lerp(0.45f, 1f, ctx.SpeedNorm) * (1f + Gauss() * 0.08f) * (1f + Mathf.Max(0f, sim.Slope) * 1.5f);
            if (t.Stumble) force *= 1.5f;
            bool scuff = t.Stumble || _rng.Chance(t.Feel.scuff * (0.5f + t.Looseness) + s.roughScuff * t.Roughness);

            // Стопа встала на свою высоту — тело переносит вес на неё (даже если шаг не прочувствован: следы и
            // опора не должны расходиться после длинного кадра).
            if (t.Left) _heightLeft = t.Height;
            else _heightRight = t.Height;
            _supportTarget = t.Height;

            _groundCaution = Mathf.Max(t.Feel.caution, t.Roughness * s.roughCaution);
            if (felt)
            {
                // Спуск: тело придерживает, удар мягче.
                float soften = sim.Slope < -0.03f ? 0.7f : 1f;
                _impactUp.Kick(-s.impactKick * t.Feel.impact * force * soften);
                _impactPitch.Kick(s.impactNod * t.Feel.impact * force * soften);
                _sinkDepth = t.Feel.sink * t.Looseness;

                if (t.OnStone)
                {
                    _eventRoll.Kick((t.Left ? 1f : -1f) * _rng.Range(6f, 12f)); // подвернулся голеностоп
                    _event = Mathf.Max(_event, 0.25f);
                }

                if (t.Stumble)
                {
                    _eventUp.Kick(-s.stumbleKick);
                    _eventPitch.Kick(s.stumbleNod);
                    _eventRoll.Kick(_rng.Signed() * 15f);
                    _disturb = 2;
                    Bump(1f, BodyEventKind.Stumble);
                }
            }

            float duration = _cur.Length / Mathf.Max(0.3f, sim.Speed);
            _events?.Emit(new StepEvent(t.Left, t.Contact, sim.Course, force, t.Kind, t.Looseness,
                t.OnStone, t.Stumble, scuff, duration, felt));
        }

        Shape Next(in BodyContext ctx, in GaitSettings s, in ExertionState ex, in SurfaceFeel feel, float roughness, in GroundAhead ahead, float stepLength)
        {
            var sim = ctx.Sim;
            double t = ctx.Time;
            float legSign = _stanceLeft ? -1f : 1f; // отталкивается опорная нога
            float leg = 1f + _sig.LegAsymmetry * 0.05f * legSign;
            float jitter = s.stepJitter * (_disturb > 0 ? 3f : 1f);
            float caution = ex.Caution;
            float dA = _driftAmp.Sample(t), dL = _driftLen.Sample(t), dS = _driftSway.Sample(t);

            float length = stepLength * _sig.Stride * feel.stride
                           * (1f + dL * s.drift * 0.25f + Gauss() * s.strideJitter * (_disturb > 0 ? 3f : 1f))
                           * (1f + _sig.LegAsymmetry * 0.02f * legSign)
                           * (1f - 0.12f * caution)
                           * (1f - s.roughStride * roughness)
                           * (1f - s.anticipation * ahead.Change(sim.Slope))
                           * (1f - 0.8f * Mathf.Max(0f, sim.Slope));
            if (_firstSteps > 0)
            {
                length *= _firstSteps >= 2 ? s.firstStep : Mathf.Lerp(s.firstStep, 1f, 0.6f);
                _firstSteps--;
            }
            if (_disturb > 0) _disturb--;

            return new Shape
            {
                Length = Mathf.Clamp(length, stepLength * 0.35f, stepLength * 1.25f),
                Amp = Mathf.Max(0f, s.bobVertical * _sig.Bounce * feel.bounce * leg
                                   * (1f + dA * s.drift + Gauss() * jitter) * (1f - 0.3f * caution)),
                Sway = Mathf.Max(0f, s.bobLateral * 0.5f * _sig.Sway * leg * (1f + dS * s.drift + Gauss() * jitter)),
                Roll = Mathf.Max(0f, s.bobRoll * _sig.Sway * (1f + dS * s.drift * 0.8f + Gauss() * jitter)),
                Pitch = s.bobPitch * (1f + Gauss() * jitter),
                Surge = s.bobSurge * (1f + Gauss() * jitter),
                // Вялое отталкивание в рыхлом — верхняя точка позже.
                Skew = Mathf.Clamp(_sig.PeakBias * 0.04f + Gauss() * s.peakSkew * 0.5f + (1f - feel.bounce) * 0.3f,
                    -0.28f, 0.28f),
            };
        }

        /// <summary>
        /// Куда встанет следующая стопа и нет ли там камня. Чаще тело подстраивает шаг заранее;
        /// иногда наступает; споткнуться можно только о камень и только если не насторожен.
        /// </summary>
        void Plan(in BodyContext ctx, in GaitSettings s, in ExertionState ex, float looseness, double d)
        {
            if (_obstacles == null) return;
            bool left = !_stanceLeft;
            float toEnd = (float)(_stepStart - d) + _cur.Length;
            var p = FootPoint(ctx, left, toEnd + s.footReach * _cur.Length, s.footOffset);
            if (!_obstacles.FindNear(p, s.footRadius, out var stone, out _)) return;

            float avoid = stone.Size >= s.alwaysAvoidSize ? 1f : Mathf.Lerp(s.avoidChance, 1f, ex.Caution);
            if (_rng.Chance(avoid))
            {
                bool longer = _rng.Chance(0.5f);
                for (int k = 0; k < 2; k++, longer = !longer)
                {
                    float f = longer ? 1.18f : 0.8f;
                    var q = FootPoint(ctx, left, toEnd + (f - 1f) * _cur.Length + s.footReach * _cur.Length * f, s.footOffset);
                    if (_obstacles.FindNear(q, s.footRadius, out _, out _)) continue;
                    _cur.Length *= f;
                    _cur.Amp *= 1.35f; // ногу выше
                    _eventPitch.Kick(10f); // короткий взгляд под ноги телом
                    Bump(0.35f, BodyEventKind.StoneAvoided);
                    return;
                }
                _cur.Amp *= 1.2f;
                _planStone = true; // некуда деться — наступает, но аккуратно
                return;
            }

            float stumble = s.stumbleChance * (1f - ex.Caution) * (0.6f + looseness);
            if (_rng.Chance(stumble)) _planStumble = true;
            else _planStone = true;
        }

        void OnStart(in BodyContext ctx, in GaitSettings s, in ExertionState ex, in GroundAhead ahead, float stepLength)
        {
            _stepStart = ctx.Sim.Distance;
            _firstSteps = 2;
            _prev = Standing(stepLength);
            var here = ctx.World.SampleSurface(ctx.Sim.Position.X, ctx.Sim.Position.Z);
            var feel = SurfaceFeel.Find(s.surfaces, here.Kind);
            _cur = Next(ctx, s, ex, feel, here.Roughness / 65536f, ahead, stepLength);
            _lean.Kick(s.startLean);
            Bump(0.3f, BodyEventKind.Start);
        }

        void OnSettle(in BodyContext ctx, in GaitSettings s)
        {
            // Приставил ногу, если застыл посреди шага.
            if (Phase(ctx.Sim.Distance) > 0.25f) EmitSoftStep(ctx, s, 0.35f);
            _lean.Kick(s.stopSettle);
            _gearTimer = s.gearLag;
            Bump(0.5f, BodyEventKind.Settled);
        }

        void OnShuffle(in BodyContext ctx, in GaitSettings s)
        {
            EmitSoftStep(ctx, s, 0.3f);
            _eventUp.Kick(-0.03f);
        }

        void EmitSoftStep(in BodyContext ctx, in GaitSettings s, float force)
        {
            bool left = !_stanceLeft;
            _stanceLeft = left;
            var contact = FootPoint(ctx, left, 0.05f, s.footOffset);
            var surface = ctx.World.SampleSurface(contact.X, contact.Z);
            // Приставил или переступил: стоит на обеих — опора посередине.
            float h = ctx.World.SampleHeightMm(contact.X, contact.Z) / 1000f;
            if (left) _heightLeft = h;
            else _heightRight = h;
            _supportTarget = (_heightLeft + _heightRight) * 0.5f;
            _events?.Emit(new StepEvent(left, contact, ctx.Sim.Course, force, surface.Kind, surface.Looseness / 65535f,
                false, false, false, 0.5f, true));
        }

        void Bump(float strength, BodyEventKind kind)
        {
            _event = Mathf.Max(_event, strength);
            _events?.Emit(new BodyEvent(kind, strength));
        }

        static WorldPos FootPoint(in BodyContext ctx, bool left, float forward, float offset)
        {
            var f = ctx.Forward;
            var r = ctx.Right;
            float side = left ? -offset : offset;
            float x = f.x * forward + r.x * side;
            float z = f.z * forward + r.z * side;
            return ctx.Sim.Position.Offset((long)Math.Round(x * 1000f), (long)Math.Round(z * 1000f));
        }

        static Shape Standing(float stepLength) => new Shape { Length = stepLength };

        float Gauss() => Mathf.Clamp(_rng.Gaussian(), -2.5f, 2.5f);
    }
}
