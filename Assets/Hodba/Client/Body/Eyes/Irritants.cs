using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>Что раздражитель делает с глазом прямо сейчас.</summary>
    public readonly struct Irritation
    {
        /// <summary>0..1 — насколько хочется прищуриться.</summary>
        public readonly float Squint;
        /// <summary>Сколько добавить к частоте моргания (0 — не влияет).</summary>
        public readonly float ExtraBlinkRate;
        /// <summary>0..1 — сколько света проходит сквозь сомкнутые веки (красное).</summary>
        public readonly float Glow;

        public Irritation(float squint, float extraBlinkRate, float glow)
        {
            Squint = squint;
            ExtraBlinkRate = extraBlinkRate;
            Glow = glow;
        }
    }

    /// <summary>
    /// Раздражитель глаза. Сейчас солнце и ветер; дым, буря, пепел — новые классы, веки не меняются.
    /// Раздражитель чувствует причину, а не то, как глаз с ней уже справился, — иначе прищур качался бы.
    /// </summary>
    public interface IEyeIrritant
    {
        Irritation Sense(in BodyContext ctx, Vector3 view, in EyelidSettings s);
    }

    public sealed class SunIrritant : IEyeIrritant
    {
        /// <summary>Стимул в последнем кадре — для отладки и экспозиции.</summary>
        public float Stimulus { get; private set; }

        public Irritation Sense(in BodyContext ctx, Vector3 view, in EyelidSettings s)
        {
            Stimulus = Glare.Stimulus(view, ctx.SunDirection, ctx.SunElevation, s.sunPower, ctx.SunVisibility);
            float squint = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.sunThreshold, 0.8f, Stimulus));

            // В полдень щуришься и не глядя на солнце: вокруг всё белое.
            float noon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 70f, ctx.SunElevation));
            squint = Mathf.Max(squint, noon * s.middaySquint * ctx.SunVisibility);

            // Сквозь веки светит по-настоящему только солнце в лицо; днём в сторону — лишь тёмно-бурое.
            float daylight = Mathf.Clamp01((ctx.SunElevation + 2f) / 12f);
            float glow = daylight * (0.04f + 0.96f * Stimulus * Stimulus);
            return new Irritation(squint, Stimulus * s.sunBlink, glow);
        }
    }

    public sealed class WindIrritant : IEyeIrritant
    {
        /// <summary>
        /// Сколько ветра бьёт в глаза, 0..~1.3: на него щурятся, и ровно столько же песка летит в лицо (<see cref="Dust"/>).
        /// Дует в лицо — ветер навстречу взгляду. Сбоку — наполовину, в спину — никак.
        /// </summary>
        public static float Stimulus(Vector3 windVelocity, Vector3 view, float strength, float gust)
        {
            var wind = new Vector3(windVelocity.x, 0f, windVelocity.z);
            var flat = new Vector3(view.x, 0f, view.z);
            if (wind.sqrMagnitude < 1e-4f || flat.sqrMagnitude < 1e-4f) return 0f;

            float face = Mathf.Clamp01(Vector3.Dot(-wind.normalized, flat.normalized) * 0.7f + 0.3f);
            face *= face;
            return face * strength * strength * (0.7f + 0.6f * gust);
        }

        public Irritation Sense(in BodyContext ctx, Vector3 view, in EyelidSettings s)
        {
            float stimulus = Stimulus(ctx.Wind, view, ctx.WindStrength, ctx.WindGust);
            float squint = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.8f, stimulus)) * s.windSquint;
            return new Irritation(squint, stimulus * s.windBlink, 0f);
        }
    }
}
