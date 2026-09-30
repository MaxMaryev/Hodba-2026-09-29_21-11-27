using System;
using Hodba.Core;
using Hodba.World;

namespace Hodba.Sim.Walk
{
    [Serializable]
    public struct WalkParams
    {
        /// <summary>Скорость по ровному, м/с. Усталый путник с грузом еле бредёт — около 3 км/ч.</summary>
        public float BaseSpeed;
        /// <summary>Сколько секунд до полного шага из стойки.</summary>
        public float AccelTime;
        /// <summary>Сколько секунд до остановки — пара шагов, не мгновенно.</summary>
        public float DecelTime;
        /// <summary>Предельная скорость разворота курса, °/с.</summary>
        public float MaxTurnRate;
        /// <summary>Длина шага, м.</summary>
        public float StepLength;
        /// <summary>Сколько скорости отнимает самый рыхлый пепел, доля. Стартовое значение, подбирается.</summary>
        public float LoosenessDrag;
        /// <summary>Сколько скорости отнимают бугры и наносы в полную силу, доля. Стартовое значение.</summary>
        public float RoughnessDrag;

        public static WalkParams Default => new WalkParams
        {
            BaseSpeed = 0.85f,
            AccelTime = 1.2f,
            DecelTime = 0.8f,
            MaxTurnRate = 25f,
            StepLength = 0.6f,
            LoosenessDrag = 0.2f,
            RoughnessDrag = 0.1f,
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

        /// <summary>Рыхлость под ногами: 0 — твёрдо, 1 — вязнешь.</summary>
        public float Looseness { get; private set; }

        /// <summary>Неровность под ногами: 0 — стекло, 1 — бугры и наносы.</summary>
        public float Roughness { get; private set; }

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
            var surface = world.SampleSurface(_position.X, _position.Z);
            Looseness = surface.Looseness / 65535f;
            Roughness = surface.Roughness / 65536f;

            float target = WantsWalk
                ? Params.BaseSpeed * ToblerFactor(Slope) * LoosenessFactor(Looseness, Params.LoosenessDrag)
                  * LoosenessFactor(Roughness, Params.RoughnessDrag) * AttentionFactor
                : 0f;
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

        /// <summary>
        /// Уклон по длинной базе: по три точки впереди и позади (1, 2, 3 м). Рябь и зерно под ногами
        /// усредняются и не дёргают ход; настоящий склон и крупные бугры — остаются.
        /// </summary>
        float SampleSlope(IWorldQuery world, double dirX, double dirZ)
        {
            long ahead = 0, behind = 0;
            for (long k = 1; k <= 3; k++)
            {
                long dx = (long)(dirX * k * 1000), dz = (long)(dirZ * k * 1000);
                ahead += world.SampleHeightMm(_position.X + dx, _position.Z + dz);
                behind += world.SampleHeightMm(_position.X - dx, _position.Z - dz);
            }
            // Среднее плечо — 2 м вперёд и 2 м назад.
            return (float)((ahead - behind) / 3.0 / 4000.0);
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

        /// <summary>В рыхлом часть усилия уходит в землю: нога проседает, отталкивание вязнет.</summary>
        public static float LoosenessFactor(float looseness, float drag) => 1f - Clamp01(looseness) * Clamp01(drag);

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
