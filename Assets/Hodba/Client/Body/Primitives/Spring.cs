using System;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Пружина с затуханием — масса, которая догоняет цель с запаздыванием и лёгким перелётом.
    /// Голова на шее, снаряжение на спине, веко — всё это пружины.
    /// Интегрируется фиксированным подшагом, поэтому при 30 и 60 fps и в длинном кадре ведёт себя одинаково.
    /// </summary>
    public sealed class Spring
    {
        /// <summary>Шаг интегрирования, с. Меньше — точнее, больше — дешевле.</summary>
        public const float Substep = 1f / 120f;
        /// <summary>Длиннее 0.25 с кадр не догоняем: тело не должно «выстрелить» после просадки.</summary>
        public const int MaxSubsteps = 30;

        public float Value;
        public float Velocity;

        /// <param name="hz">Собственная частота, Гц.</param>
        /// <param name="damping">1 — без перелёта, меньше — с перелётом, больше — вязко.</param>
        public void Step(float target, float dt, float hz, float damping)
        {
            if (dt <= 0f) return;
            int n = Math.Min(MaxSubsteps, Math.Max(1, (int)Math.Ceiling(dt / Substep)));
            float h = Math.Min(dt, MaxSubsteps * Substep) / n;
            float w = 2f * (float)Math.PI * Math.Max(0.01f, hz);
            float k = w * w, c = 2f * damping * w;
            for (int i = 0; i < n; i++)
            {
                // Полунеявный Эйлер: устойчив для пружин, дёшев.
                Velocity += (k * (target - Value) - c * Velocity) * h;
                Value += Velocity * h;
            }
        }

        /// <summary>Толчок: мгновенно добавить скорость (удар пятки, спотыкание).</summary>
        public void Kick(float velocity) => Velocity += velocity;

        public void Reset(float value = 0f)
        {
            Value = value;
            Velocity = 0f;
        }
    }
}
