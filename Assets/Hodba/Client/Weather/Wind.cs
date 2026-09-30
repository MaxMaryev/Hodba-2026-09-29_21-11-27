using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Единый ветер для пыли, звука и следов. Направление бродит вокруг преобладающего, порывы приходят волнами.
    /// </summary>
    public sealed class Wind
    {
        /// <summary>Куда дует, ° от севера по часовой.</summary>
        public float Direction { get; private set; }
        /// <summary>0..1.</summary>
        public float Strength { get; private set; }
        /// <summary>0..1 — сколько в силе ветра приходится на порыв.</summary>
        public float Gust { get; private set; }
        /// <summary>Скорость воздуха, м/с.</summary>
        public Vector3 Velocity { get; private set; }

        /// <param name="course">Курс путника, °: нужен только отладочному встречному ветру.</param>
        public void Tick(FieldConfig config, float time, float course = 0f)
        {
            float wander = (Mathf.PerlinNoise(time * 0.011f, 0.37f) - 0.5f) * 2f * config.windWander;
            Direction = config.prevailingWind + wander;

            float period = Mathf.Max(1f, config.gustPeriod);
            float g = Mathf.PerlinNoise(time / period, 5.1f);
            g = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.85f, g));
            float slow = Mathf.PerlinNoise(time * 0.004f, 9.3f); // долгие затишья и ветреные часы
            Gust = g;
            Strength = Mathf.Clamp01(config.windBase * Mathf.Lerp(0.4f, 1.3f, slow) + config.windGust * g);

            // Полигон: сильный ветер прямо в лицо, порывы остаются живыми.
            if (config.debugHeadwind)
            {
                Direction = course + 180f + wander * 0.2f;
                Strength = Mathf.Clamp01(0.75f + 0.25f * g);
            }

            float rad = Direction * Mathf.Deg2Rad;
            Velocity = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * Mathf.Lerp(1.5f, 9f, Strength);
        }
    }
}
