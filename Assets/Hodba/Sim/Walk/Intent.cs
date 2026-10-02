namespace Hodba.Sim.Walk
{
    public enum IntentKind : byte
    {
        Walk,
        Stop,
        ToggleWalk,
        /// <summary>Задать курс (градусы, 0 — север, по часовой).</summary>
        SetCourse,
        /// <summary>Замедлиться, присматриваясь: Value — множитель скорости 0..1.</summary>
        SetAttention,
        /// <summary>Подгонять себя: Value — сколько просит ритм шагов, 0..1. Тело даёт, сколько может (запас сил).</summary>
        Hurry,
    }

    /// <summary>
    /// Намерение игрока или автономии. Ввод никогда не двигает человека напрямую —
    /// только намерения, которые одинаково понимает клиент и (позже) сервер. См. Docs/Design/05-walking.md.
    /// </summary>
    public readonly struct Intent
    {
        public readonly IntentKind Kind;
        public readonly float Value;

        public Intent(IntentKind kind, float value = 0f)
        {
            Kind = kind;
            Value = value;
        }

        public static Intent Walk() => new Intent(IntentKind.Walk);
        public static Intent Stop() => new Intent(IntentKind.Stop);
        public static Intent Toggle() => new Intent(IntentKind.ToggleWalk);
        public static Intent Course(float degrees) => new Intent(IntentKind.SetCourse, degrees);
        public static Intent Attention(float speedFactor) => new Intent(IntentKind.SetAttention, speedFactor);
        public static Intent Hurry(float drive) => new Intent(IntentKind.Hurry, drive);
    }
}
