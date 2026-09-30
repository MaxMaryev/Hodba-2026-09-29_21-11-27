using System;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Мягкий предел: малое проходит нетронутым, большое плавно упирается в потолок.
    /// Жёсткий clamp дал бы «стенку», которую видно; здесь её нет.
    /// </summary>
    public static class SoftLimit
    {
        /// <summary>Доля предела, до которой значение не сжимается вовсе.</summary>
        public const float Knee = 0.6f;

        public static float Apply(float x, float limit)
        {
            if (limit <= 0f) return 0f;
            float a = Math.Abs(x);
            float k = limit * Knee;
            if (a <= k) return x;
            float room = limit - k;
            float y = k + room * (float)Math.Tanh((a - k) / room);
            return x < 0f ? -y : y;
        }

        /// <summary>Мягко ограничить скорость изменения: из prev к next не быстрее maxRate в секунду.</summary>
        public static float Rate(float prev, float next, float maxRate, float dt)
        {
            if (maxRate <= 0f || dt <= 0f) return next;
            return prev + Apply(next - prev, maxRate * dt);
        }
    }
}
