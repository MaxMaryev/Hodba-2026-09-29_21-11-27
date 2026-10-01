using UnityEngine;

namespace Hodba.Client.Body
{
    public readonly struct ExertionState
    {
        /// <summary>0..1 — усилие прямо сейчас.</summary>
        public readonly float Effort;
        /// <summary>0..1 — накопленная одышка: растёт за секунды, отпускает за минуту.</summary>
        public readonly float Load;
        /// <summary>0..1 — осторожность: шаг короче, чаще под ноги.</summary>
        public readonly float Caution;
        /// <summary>Вдохов в секунду.</summary>
        public readonly float BreathRate;
        public readonly float BreathDepth;
        /// <summary>0..1 по циклу: вдох, потом выдох.</summary>
        public readonly float BreathPhase;
        /// <summary>0..1 — насколько полна грудь.</summary>
        public readonly float Lungs;

        public ExertionState(float effort, float load, float caution, float breathRate, float breathDepth,
            float breathPhase, float lungs)
        {
            Effort = effort;
            Load = load;
            Caution = caution;
            BreathRate = breathRate;
            BreathDepth = breathDepth;
            BreathPhase = breathPhase;
            Lungs = lungs;
        }

        /// <summary>Выдох — громче, вдох — тише.</summary>
        public bool Exhaling => BreathPhase >= Exertion.InhaleShare;
    }

    /// <summary>
    /// Память усилия. Пройденное остаётся в теле: после подъёма дыхание успокаивается не сразу,
    /// после рыхлого и спуска осторожность держится ещё какое-то время, а не выключается на границе участка.
    /// Пока живёт на клиенте; потом переедет в Hodba.Sim.Body (Docs/Design/17-body.md) — поэтому
    /// сюда не проникает ничего, кроме контекста и событий.
    /// </summary>
    public sealed class Exertion
    {
        /// <summary>Доля цикла на вдох: вдох короче выдоха.</summary>
        public const float InhaleShare = 0.4f;

        readonly Signature _sig;
        readonly PinkNoise _rateDrift, _depthDrift;

        float _effort, _load, _caution, _lastSpeed;
        float _phase, _rate, _depth, _hitch;
        bool _hasSpeed;

        public ExertionState State { get; private set; }

        public Exertion(uint seed)
        {
            _sig = Signature.From(seed);
            _rateDrift = new PinkNoise(seed, Salts.Breath, 0.01f, 5);
            _depthDrift = new PinkNoise(seed, Salts.BreathDrift, 0.02f, 4);
            _phase = 0.3f;
            _rate = 0.23f;
            _depth = 0.6f;
            State = new ExertionState(0f, 0f, 0f, _rate, _depth, _phase, Lungs(_phase));
        }

        public void Tick(in BodyContext ctx, in ExertionSettings s, in GaitState gait)
        {
            float dt = ctx.Dt;
            if (dt <= 0f) return;
            var sim = ctx.Sim;

            float accel = _hasSpeed ? (sim.Speed - _lastSpeed) / dt : 0f;
            _lastSpeed = sim.Speed;
            _hasSpeed = true;

            float effort = ctx.SpeedNorm * (s.baseEffort + Mathf.Max(0f, sim.Slope) * s.uphillEffort + sim.Looseness * s.looseEffort
                                            + sim.Roughness * s.roughEffort)
                           + Mathf.Max(0f, accel) * s.accelEffort;
            _effort = Approach(_effort, Mathf.Clamp01(effort), 0.5f, 0.5f, dt);
            _load = Approach(_load, _effort, s.loadRise, s.loadFall, dt);

            float cautionTarget = Mathf.Clamp01(gait.GroundCaution
                                                + Mathf.Max(0f, -sim.Slope - s.downhillFrom) * s.downhillCaution
                                                + gait.AheadChange * s.aheadCaution);
            _caution = Approach(_caution, cautionTarget, s.cautionRise, s.cautionFall, dt);

            // Дыхание: свой темп, который подтягивается к шагу, но не прилипает к нему.
            float own = Mathf.Lerp(s.breathRest, s.breathHard, _load) * _sig.BreathRate
                        * (1f + _rateDrift.Sample(ctx.Time) * 0.12f);
            float target = own;
            if (gait.StepFrequency > 0.3f)
            {
                float ratio = Mathf.Lerp(s.stepsPerBreathCalm, s.stepsPerBreathHard, _load);
                target = Mathf.Lerp(own, gait.StepFrequency / Mathf.Max(1f, ratio), s.coupling * gait.Blend);
            }
            _rate = Approach(_rate, target, 3f, 3f, dt);
            _phase += _rate * dt;
            _phase -= Mathf.Floor(_phase);

            _hitch = Mathf.Max(0f, _hitch - dt / 1.5f);
            _depth = Mathf.Lerp(s.depthRest, s.depthHard, _load) * (1f + _depthDrift.Sample(ctx.Time) * 0.15f)
                     * (1f + _hitch * 0.8f);

            State = new ExertionState(_effort, _load, _caution, _rate, _depth, _phase, Lungs(_phase));
        }

        /// <summary>Шаг подтягивает фазу дыхания к ближайшей «своей» доле цикла — частично.</summary>
        public void OnStep(in StepEvent e, in ExertionSettings s)
        {
            if (!e.Felt || e.Force < 0.4f) return;
            float ratio = Mathf.Lerp(s.stepsPerBreathCalm, s.stepsPerBreathHard, _load);
            float slot = 1f / Mathf.Max(1f, Mathf.Round(ratio));
            float off = Mathf.Repeat(_phase, slot);
            float error = off > slot * 0.5f ? off - slot : off;
            _phase -= error * s.phaseNudge;
            _phase -= Mathf.Floor(_phase);
        }

        public void OnBodyEvent(in BodyEvent e, in ExertionSettings s)
        {
            if (e.Kind != BodyEventKind.Stumble) return;
            _caution = Mathf.Clamp01(_caution + s.stumbleCaution * e.Strength);
            _hitch = 1f; // дыхание сбилось: пара глубоких вдохов
            _load = Mathf.Clamp01(_load + 0.1f * e.Strength);
        }

        /// <summary>Наполненность груди по фазе: короткий вдох, долгий выдох, без изломов.</summary>
        public static float Lungs(float phase)
        {
            phase -= Mathf.Floor(phase);
            return phase < InhaleShare
                ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * phase / InhaleShare)
                : 0.5f + 0.5f * Mathf.Cos(Mathf.PI * (phase - InhaleShare) / (1f - InhaleShare));
        }

        /// <summary>Экспоненциально к цели: своё время вверх и своё вниз.</summary>
        static float Approach(float current, float target, float riseTime, float fallTime, float dt)
        {
            float tau = target > current ? riseTime : fallTime;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau)));
        }
    }

    /// <summary>Грудь поднимает глаза на вдохе, голова чуть запрокидывается. Идёт и стоя, и на ходу.</summary>
    public sealed class BreathLayer : IMotionLayer
    {
        public MotionRole Role => MotionRole.Rhythm;
        public PoseDelta Pose { get; private set; }
        public float EventStrength => 0f;

        public void Tick(in ExertionState ex, in ExertionSettings s)
        {
            float chest = (ex.Lungs - 0.5f) * ex.BreathDepth;
            Pose = new PoseDelta(chest * s.breathAmplitude, 0f, 0f, -chest * s.breathPitch, 0f, 0f);
        }
    }
}
