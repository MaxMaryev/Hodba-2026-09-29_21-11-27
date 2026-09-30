namespace Hodba.Client.Body
{
    public enum MotionRole
    {
        /// <summary>Сам ход тела: шаг, дыхание, реакции. Слышен всегда.</summary>
        Rhythm,
        /// <summary>Фон: мелкий дрейф и покачивание стоя. Затихает, пока идёт выраженное событие.</summary>
        Background,
    }

    /// <summary>
    /// Слой движения головы. Новый слой (дрожь от холода, хромота, груз) — новый класс,
    /// добавленный в <see cref="MotionBudget"/>. Остальные слои о нём не знают.
    /// </summary>
    public interface IMotionLayer
    {
        MotionRole Role { get; }

        /// <summary>Вклад в этом кадре.</summary>
        PoseDelta Pose { get; }

        /// <summary>0..1 — насколько сейчас идёт выраженное событие (камень, спотыкание, остановка).</summary>
        float EventStrength { get; }
    }
}
