using System;
using Hodba.Core;
using Hodba.World;

namespace Hodba.Sim.Walk
{
    [Serializable]
    public struct WalkParams
    {
        /// <summary>Скорость по ровному, м/с. Настоящий пеший ход ~5 км/ч.</summary>
        public float BaseSpeed;
        /// <summary>Сколько секунд до полного шага из стойки.</summary>
        public float AccelTime;
        /// <summary>Сколько секунд до остановки — пара шагов, не мгновенно.</summary>
        public float DecelTime;
        /// <summary>Предельная скорость разворота курса, °/с.</summary>
        public float MaxTurnRate;
        /// <summary>Длина шага, м.</summary>
        public float StepLength;

        public static WalkParams Default => new WalkParams
        {
            BaseSpeed = 1.35f,
            AccelTime = 1.2f,
            DecelTime = 0.8f,
            MaxTurnRate = 25f,
            StepLength = 0.72f,
        };
    }

    /// <summary>
    /// Ходьба как чистая симуляция: без Unity, без рендера. Сейчас крутится на клиенте,
    /// позже та же логика будет считать доверенный путь на сервере (Docs/Design/01-background-walk.md).
    /// </summary>
    public sealed class WalkSim
    {
        public WalkParams Params;

        public WorldPos Position => _position;
        public float Course { get; private set; }
        public float TargetCourse { get; private set; }
        public float Speed { get; private set; }
        public bool WantsWalk { get; private set; }
        public float AttentionFactor { get; private set; } = 1f;

        /// <summary>Пройдено всего, м. Отсюда фаза шага.</summary>
        public double Distance { get; private set; }

        /// <summary>Сколько шагов сделано за жизнь симуляции.</summary>
        public long Steps => (long)Math.Floor(Distance / Params.StepLength);

        /// <summary>Уклон под ногами по курсу (dh/dx), для тела и отладки.</summary>
        public float Slope { get; private set; }

        /// <summary>Скорость поворота курса в этом кадре, °/с (для крена камеры).</summary>
        public float TurnRate { get; private set; }

        WorldPos _position;
        double _remX, _remZ; // доли миллиметра, чтобы медленная ходьба не терялась при округлении

        public WalkSim(WalkParams p, WorldPos start, float course)
        {
            Params = p;
            _position = start;
            Course = TargetCourse = Normalize(course);
        }

        public void Apply(in Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Walk: WantsWalk = true; break;
                case IntentKind.Stop: WantsWalk = false; break;
                case IntentKind.ToggleWalk: WantsWalk = !WantsWalk; break;
                case IntentKind.SetCourse: TargetCourse = Normalize(intent.Value); break;
                case IntentKind.SetAttention: AttentionFactor = Clamp01(intent.Value); break;
            }
        }

        public void Step(float dt, IWorldQuery world)
        {
            if (dt <= 0f) return;

            // Курс: не быстрее MaxTurnRate.
            float delta = DeltaAngle(Course, TargetCourse);
            float maxTurn = Params.MaxTurnRate * dt;
            float turn = Math.Abs(delta) <= maxTurn ? delta : Math.Sign(delta) * maxTurn;
            Course = Normalize(Course + turn);
            TurnRate = turn / dt;

            double rad = Course * (Math.PI / 180.0);
            double dirX = Math.Sin(rad);
            double dirZ = Math.Cos(rad);

            Slope = SampleSlope(world, dirX, dirZ);

            float target = WantsWalk ? Params.BaseSpeed * ToblerFactor(Slope) * AttentionFactor : 0f;
            float rate = target > Speed
                ? Params.BaseSpeed / Math.Max(0.01f, Params.AccelTime)
                : Params.BaseSpeed / Math.Max(0.01f, Params.DecelTime);
            Speed = MoveTowards(Speed, target, rate * dt);

            double step = Speed * dt;
            if (step <= 0) return;

            Distance += step;
            _remX += dirX * step * WorldPos.MmPerMeter;
            _remZ += dirZ * step * WorldPos.MmPerMeter;
            long mx = (long)Math.Truncate(_remX);
            long mz = (long)Math.Truncate(_remZ);
            _remX -= mx;
            _remZ -= mz;
            _position = _position.Offset(mx, mz);
        }

        /// <summary>Мгновенно переставить (восстановление, фоновый путь).</summary>
        public void Teleport(WorldPos p, float course, bool wantsWalk)
        {
            _position = p;
            _remX = _remZ = 0;
            Course = TargetCourse = Normalize(course);
            WantsWalk = wantsWalk;
            Speed = wantsWalk ? Params.BaseSpeed : 0f;
        }

        float SampleSlope(IWorldQuery world, double dirX, double dirZ)
        {
            const long probe = 1000; // 1 м вперёд и назад
            long ax = _position.X - (long)(dirX * probe), az = _position.Z - (long)(dirZ * probe);
            long bx = _position.X + (long)(dirX * probe), bz = _position.Z + (long)(dirZ * probe);
            long dh = world.SampleHeightMm(bx, bz) - world.SampleHeightMm(ax, az);
            return (float)(dh / (2.0 * probe));
        }

        /// <summary>
        /// Функция пешего хода Тоблера, нормированная к ровному месту:
        /// v = 6·e^(−3.5·|s + 0.05|) км/ч.
        /// </summary>
        public static float ToblerFactor(float slope)
        {
            double flat = Math.Exp(-3.5 * 0.05);
            return (float)(Math.Exp(-3.5 * Math.Abs(slope + 0.05)) / flat);
        }

        public static float Normalize(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        public static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        static float MoveTowards(float current, float target, float maxDelta) =>
            Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
