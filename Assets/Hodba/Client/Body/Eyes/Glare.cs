using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Ослепление в двух величинах — чтобы прищур не качался.
    /// Стимул — внешняя сила: солнце, куда смотришь, высота. На него веки не влияют, на него и щурятся.
    /// Воспринятое — что осталось после век, и оно же медленно отпускает: его видит картинка.
    /// Прищурился — картинке легче, но причина та же, поэтому глаз не открывается обратно.
    /// </summary>
    public static class Glare
    {
        /// <summary>0..1: насколько солнце бьёт в глаз.</summary>
        /// <param name="power">Острота конуса: больше — слепит, только когда смотришь почти на солнце.</param>
        public static float Stimulus(Vector3 view, Vector3 sunDirection, float sunElevation, float power, float sunVisibility = 1f)
        {
            float facing = Mathf.Clamp01(Vector3.Dot(view.normalized, sunDirection.normalized));
            float visible = Mathf.Clamp01((sunElevation + 1f) / 4f);
            return Mathf.Pow(facing, power) * visible * Mathf.Clamp01(sunVisibility);
        }
    }

    /// <summary>
    /// Адаптация глаза к ослеплению: слепит быстро, отпускает медленно — ослепление держится.
    /// </summary>
    public sealed class GlareAdaptation
    {
        public float Perceived { get; private set; }

        /// <param name="relief">0..1: сколько снимает полный прищур.</param>
        public float Tick(float stimulus, float squint, float relief, float riseTime, float fallTime, float dt)
        {
            float target = stimulus * (1f - Mathf.Clamp01(squint) * Mathf.Clamp01(relief));
            float tau = target > Perceived ? riseTime : fallTime;
            Perceived = Mathf.Lerp(Perceived, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, tau)));
            return Perceived;
        }
    }
}
