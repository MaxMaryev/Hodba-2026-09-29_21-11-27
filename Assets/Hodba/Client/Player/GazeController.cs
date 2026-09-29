using Hodba.Sim.Walk;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Взгляд отдельно от курса. Коротко глянул в сторону — просто посмотрел.
    /// Смотришь долго — человек поворачивает туда. Так настоящие люди и ходят (Docs/Design/05-walking.md).
    /// </summary>
    public sealed class GazeController
    {
        /// <summary>Куда смотрит, ° по миру (0 — север).</summary>
        public float Yaw { get; private set; }
        /// <summary>Наклон головы, ° (плюс — вниз, как в Unity).</summary>
        public float Pitch { get; private set; }
        /// <summary>0..1 — насколько всмотрелся.</summary>
        public float FocusBlend { get; private set; }

        float _targetYaw, _targetPitch;
        float _dwell;

        public GazeController(float yaw)
        {
            Yaw = _targetYaw = yaw;
            Pitch = _targetPitch = 4f;
        }

        public void Tick(InputReader input, WalkSim sim, FieldConfig config, float dt)
        {
            _targetYaw = WalkSim.Normalize(_targetYaw + input.LookDegrees.x);
            _targetPitch = Mathf.Clamp(_targetPitch - input.LookDegrees.y, config.pitchMin, config.pitchMax);

            float k = config.lookSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-dt / config.lookSmoothing);
            Yaw = WalkSim.Normalize(Mathf.LerpAngle(Yaw, _targetYaw, k));
            Pitch = Mathf.Lerp(Pitch, _targetPitch, k);

            // Шея не выворачивается: дальше предела поворачивается всё тело.
            float offset = WalkSim.DeltaAngle(sim.Course, _targetYaw);
            if (Mathf.Abs(offset) > config.neckYawLimit)
                sim.Apply(Intent.Course(_targetYaw - Mathf.Sign(offset) * config.neckYawLimit * 0.9f));

            // Пошёл — пошёл туда, куда смотришь.
            if (input.ToggleWalk && !sim.WantsWalk &&
                Mathf.Abs(WalkSim.DeltaAngle(sim.TargetCourse, _targetYaw)) > config.courseFollowAngle)
                sim.Apply(Intent.Course(_targetYaw));

            // Долгий взгляд в сторону на ходу — поворот.
            float fromTarget = WalkSim.DeltaAngle(sim.TargetCourse, _targetYaw);
            if (sim.WantsWalk && Mathf.Abs(fromTarget) > config.courseFollowAngle) _dwell += dt;
            else _dwell = 0f;
            if (_dwell > config.courseFollowDelay) sim.Apply(Intent.Course(_targetYaw));

            float focusTarget = input.Focus ? 1f : 0f;
            FocusBlend = Mathf.MoveTowards(FocusBlend, focusTarget, dt / Mathf.Max(0.05f, config.focusTime));
            float smooth = Mathf.SmoothStep(0f, 1f, FocusBlend);
            sim.Apply(Intent.Attention(Mathf.Lerp(1f, config.focusSpeedFactor, smooth)));
        }

        public float Fov(FieldConfig config) =>
            Mathf.Lerp(config.fov, config.focusFov, Mathf.SmoothStep(0f, 1f, FocusBlend));
    }
}
