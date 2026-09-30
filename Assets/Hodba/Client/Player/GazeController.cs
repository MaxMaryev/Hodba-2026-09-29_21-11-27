using Hodba.Sim.Walk;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Голова отдельно от курса. Коротко глянул в сторону — просто посмотрел.
    /// Смотришь долго — человек поворачивает туда. Так настоящие люди и ходят (Docs/Design/05-walking.md).
    /// Куда смотрит картинка и куда намерен игрок — разные вещи: глаза, уступая ввод, передают свой взгляд
    /// голове (<see cref="Absorb"/>), но это не намерение. Курс слушает только намерение.
    /// </summary>
    public sealed class GazeController
    {
        /// <summary>Сколько надо провести пальцем, чтобы взгляд стал намерением, °.</summary>
        const float IntentDrag = 2f;

        /// <summary>Куда смотрит голова, ° по миру (0 — север).</summary>
        public float Yaw { get; private set; }
        /// <summary>Наклон головы, ° (плюс — вниз, как в Unity).</summary>
        public float Pitch { get; private set; }
        /// <summary>0..1 — насколько всмотрелся.</summary>
        public float FocusBlend { get; private set; }
        /// <summary>Куда намерен смотреть игрок, °. По нему поворачивает курс.</summary>
        public float IntentYaw => _intentYaw;

        float _targetYaw, _targetPitch, _intentYaw;
        float _dwell, _dragged;

        public GazeController(float yaw)
        {
            Yaw = _targetYaw = _intentYaw = yaw;
            Pitch = _targetPitch = 4f;
        }

        /// <summary>
        /// Принять в голову то, куда смотрели глаза. Картинка не двигается; намерение и курс не меняются.
        /// </summary>
        public void Absorb(float yaw, float pitch, FieldConfig config)
        {
            Yaw = WalkSim.Normalize(Yaw + yaw);
            _targetYaw = WalkSim.Normalize(_targetYaw + yaw);
            float p = Mathf.Clamp(_targetPitch + pitch, config.pitchMin, config.pitchMax);
            Pitch += p - _targetPitch;
            _targetPitch = p;
        }

        public void Tick(InputReader input, WalkSim sim, FieldConfig config, float dt) =>
            Tick(input.LookDegrees, input.Looking, input.ToggleWalk, input.Focus, sim, config, dt);

        public void Tick(Vector2 look, bool looking, bool toggleWalk, bool focus, WalkSim sim, FieldConfig config, float dt)
        {
            _targetYaw = WalkSim.Normalize(_targetYaw + look.x);
            _intentYaw = WalkSim.Normalize(_intentYaw + look.x);
            _targetPitch = Mathf.Clamp(_targetPitch - look.y, config.pitchMin, config.pitchMax);

            // Провёл пальцем по-настоящему — теперь то, что видно, и есть намерение.
            if (looking)
            {
                _dragged += Mathf.Abs(look.x) + Mathf.Abs(look.y);
                if (_dragged > IntentDrag) _intentYaw = _targetYaw;
            }
            else _dragged = 0f;

            float k = config.lookSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-dt / config.lookSmoothing);
            Yaw = WalkSim.Normalize(Mathf.LerpAngle(Yaw, _targetYaw, k));
            Pitch = Mathf.Lerp(Pitch, _targetPitch, k);

            // Шея не выворачивается: дальше предела поворачивается всё тело.
            float offset = WalkSim.DeltaAngle(sim.Course, _intentYaw);
            if (Mathf.Abs(offset) > config.neckYawLimit)
                sim.Apply(Intent.Course(_intentYaw - Mathf.Sign(offset) * config.neckYawLimit * 0.9f));

            // Пошёл — пошёл туда, куда смотришь.
            if (toggleWalk && !sim.WantsWalk &&
                Mathf.Abs(WalkSim.DeltaAngle(sim.TargetCourse, _intentYaw)) > config.courseFollowAngle)
                sim.Apply(Intent.Course(_intentYaw));

            // Долгий взгляд в сторону на ходу — поворот.
            float fromTarget = WalkSim.DeltaAngle(sim.TargetCourse, _intentYaw);
            if (sim.WantsWalk && Mathf.Abs(fromTarget) > config.courseFollowAngle) _dwell += dt;
            else _dwell = 0f;
            if (_dwell > config.courseFollowDelay) sim.Apply(Intent.Course(_intentYaw));

            float focusTarget = focus ? 1f : 0f;
            FocusBlend = Mathf.MoveTowards(FocusBlend, focusTarget, dt / Mathf.Max(0.05f, config.focusTime));
            float smooth = Mathf.SmoothStep(0f, 1f, FocusBlend);
            sim.Apply(Intent.Attention(Mathf.Lerp(1f, config.focusSpeedFactor, smooth)));
        }

        /// <summary>Накоплено «смотрю в сторону», с — для проверки, что глаза его не копят.</summary>
        public float Dwell => _dwell;

        public float Fov(FieldConfig config) =>
            Mathf.Lerp(config.fov, config.focusFov, Mathf.SmoothStep(0f, 1f, FocusBlend));
    }
}
