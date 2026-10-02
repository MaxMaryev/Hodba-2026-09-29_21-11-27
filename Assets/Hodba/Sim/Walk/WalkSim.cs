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
        /// <summary>
        /// С какого спуска (уклон, доля) крутизна начинает сдерживать шаг. Положе — ноги несут: ход быстрее, чем по ровному,
        /// и не медленнее, чем на лёгком спуске. Стартовое значение, подбирается.
        /// </summary>
        public float SteepDescent;
        /// <summary>
        /// Как резко тормозит спуск круче <see cref="SteepDescent"/>: 1 — как подъём у Тоблера, 0 — не тормозит.
        /// Стартовое значение, подбирается.
        /// </summary>
        public float DownhillEase;
        /// <summary>Свой темп, запас сил и спешка.</summary>
        public PaceParams Pace;

        public static WalkParams Default => new WalkParams
        {
            BaseSpeed = 0.85f,
            AccelTime = 1.2f,
            DecelTime = 0.8f,
            MaxTurnRate = 25f,
            StepLength = 0.6f,
            LoosenessDrag = 0.2f,
            RoughnessDrag = 0.1f,
            SteepDescent = 0.15f,
            DownhillEase = 0.45f,
            Pace = PaceParams.Default,
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

        /// <summary>Усилие прямо сейчас, 0..~1.5: ровный шаг ≈ 0.3, спешка и подъём — больше.</summary>
        public float Effort { get; private set; }

        /// <summary>
        /// Усталость, 0..1 — обратная сторона запаса сил. Тратится, когда усилие выше посильного (спешка, подъём),
        /// возвращается на ровном, на спуске и стоя. Высокая — человек идёт медленнее обычного и не может спешить.
        /// </summary>
        public float Fatigue { get; private set; }

        /// <summary>Сколько просит ритм шагов, 0..1.</summary>
        public float Hurry { get; private set; }

        /// <summary>0..1 — насколько тело на самом деле подгоняет себя: просьба, урезанная запасом сил.</summary>
        public float Haste { get; private set; }

        /// <summary>Множитель своего темпа: откат от усталости и дрейф по пути (без спешки).</summary>
        public float PaceFactor { get; private set; } = 1f;

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
                case IntentKind.Hurry: Hurry = Clamp01(intent.Value); break;
            }
            if (!WantsWalk) Hurry = 0f;
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

            // Спешка: сколько просит ритм, столько и даёт запас сил — уставший не ускорится, сколько ни подгоняй.
            var pace = Params.Pace;
            float capacity = 1f - SmoothStep(pace.HurryFadeFrom, pace.HurryFadeTo, Fatigue);
            Haste = Approach(Haste, WantsWalk ? Hurry * capacity : 0f, pace.HasteRise, pace.HasteFall, dt);
            PaceFactor = DebtFactor(Fatigue, pace) * Drift(world.Info.Seed, pace);

            float target = WantsWalk
                ? Params.BaseSpeed * SlopeFactor(Slope, Params.SteepDescent, Params.DownhillEase) * LoosenessFactor(Looseness, Params.LoosenessDrag)
                  * LoosenessFactor(Roughness, Params.RoughnessDrag) * AttentionFactor
                  * PaceFactor * (1f + pace.HurryGain * Haste)
                : 0f;
            float rate = target > Speed
                ? Params.BaseSpeed / Math.Max(0.01f, Params.AccelTime)
                : Params.BaseSpeed / Math.Max(0.01f, Params.DecelTime);
            float before = Speed;
            Speed = MoveTowards(Speed, target, rate * dt);

            Effort = EffortOf(Speed, (Speed - before) / dt);
            Fatigue = Clamp01(Fatigue + FatigueRate(Effort, pace) * dt);

            double step = Speed * dt;
            if (step <= 0) return;

            _remX += dirX * step * WorldPos.MmPerMeter;
            _remZ += dirZ * step * WorldPos.MmPerMeter;
            long mx = (long)Math.Truncate(_remX);
            long mz = (long)Math.Truncate(_remZ);
            _remX -= mx;
            _remZ -= mz;
            var next = _position.Offset(mx, mz);
            if (world is IWorldMovementQuery obstacles)
            {
                var resolved = obstacles.ResolveMovement(_position, next);
                double actualX = (resolved.X - _position.X) / 1000.0;
                double actualZ = (resolved.Z - _position.Z) / 1000.0;
                Distance += resolved == next ? step : Math.Sqrt(actualX * actualX + actualZ * actualZ);
                if (resolved.X != next.X) _remX = 0;
                if (resolved.Z != next.Z) _remZ = 0;
                _position = resolved;
            }
            else
            {
                Distance += step;
                _position = next;
            }
        }

        /// <summary>Мгновенно переставить (восстановление, фоновый путь).</summary>
        public void Teleport(WorldPos p, float course, bool wantsWalk)
        {
            _position = p;
            _remX = _remZ = 0;
            Course = TargetCourse = Normalize(course);
            WantsWalk = wantsWalk;
            Speed = wantsWalk ? Params.BaseSpeed : 0f;
            // Фоновый путь — со своими привычками и привалами: вернулся отдохнувшим, без спешки.
            Hurry = Haste = Fatigue = Effort = 0f;
            PaceFactor = 1f;
        }

        /// <summary>
        /// Усилие при этой скорости и разгоне. Ровный шаг растёт как квадрат скорости — спешка дорогая;
        /// подъём, рыхлость и бугры добавляют пропорционально пройденному. Спуск не добавляет ничего:
        /// под гору ноги несут сами — скорость, которую дал склон, даром, запас не тратится.
        /// </summary>
        float EffortOf(float speed, float accel)
        {
            var p = Params.Pace;
            float r = Params.BaseSpeed > 0f ? speed / Params.BaseSpeed : 0f;
            float carried = Slope < 0f ? Math.Max(1f, SlopeFactor(Slope, Params.SteepDescent, Params.DownhillEase)) : 1f;
            float own = r / carried;
            return p.BaseEffort * own * own
                   + r * (Math.Max(0f, Slope) * p.UphillEffort + Looseness * p.LooseEffort + Roughness * p.RoughEffort)
                   + Math.Max(0f, accel) * p.AccelEffort;
        }

        /// <summary>
        /// Как меняется усталость, в секунду. Выше посильного — тратится пропорционально превышению;
        /// ниже — возвращается, и тем быстрее, чем легче идти: стоя — быстрее всего.
        /// </summary>
        public static float FatigueRate(float effort, in PaceParams p)
        {
            float sustainable = Math.Max(0.01f, p.Sustainable);
            return effort > sustainable
                ? (effort - sustainable) / Math.Max(0.01f, p.SpendTime)
                : -(sustainable - effort) / sustainable / Math.Max(0.01f, p.RecoverTime);
        }

        /// <summary>Откат: с какой-то усталости человек плетётся медленнее обычного.</summary>
        public static float DebtFactor(float fatigue, in PaceParams p) => 1f - Clamp01(p.DebtSlow) * SmoothStep(p.DebtFrom, 1f, fatigue);

        /// <summary>
        /// Свой темп плавает по пути: то бодрее, то вязнет. Шум по пройденным метрам на фиксированной точке —
        /// сервер переиграет его побитно так же.
        /// </summary>
        float Drift(uint seed, in PaceParams p)
        {
            long cellMm = (long)(p.DriftCellM * WorldPos.MmPerMeter);
            if (p.DriftAmount <= 0f || cellMm <= 0) return 1f;
            long mm = (long)(Distance * WorldPos.MmPerMeter);
            float n = ValueNoise.Fbm(mm, 0, cellMm, 3, seed ^ 0x9ACE5EEDu) / (float)ValueNoise.One;
            return 1f + n * p.DriftAmount;
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

        /// <summary>
        /// Скорость от уклона. В гору — по Тоблеру. Под гору ноги несут: к лёгкому спуску (−5%, пик Тоблера) ход
        /// быстрее ровного и дальше не падает, пока спуск не станет крутым (<paramref name="steepDescent"/>).
        /// Только круче тело начинает придерживать — мягче, чем у Тоблера: он считал быстрых ходоков по горным
        /// тропам, а усталый путник по пеплу и песку на спуске почти не тормозит.
        /// </summary>
        public static float SlopeFactor(float slope, float steepDescent, float downhillEase)
        {
            const float peak = -0.05f; // у Тоблера самый быстрый ход — на лёгком спуске
            if (slope >= peak) return ToblerFactor(slope);
            float steep = -Math.Max(-peak, steepDescent);
            float best = ToblerFactor(peak);
            if (slope >= steep) return best;
            float ease = Clamp01(downhillEase);
            return best * (float)Math.Exp(-3.5 * ease * (steep - slope));
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

        static float SmoothStep(float from, float to, float v)
        {
            if (to <= from) return v >= to ? 1f : 0f;
            float t = Clamp01((v - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Экспоненциально к цели: своё время вверх и своё вниз.</summary>
        static float Approach(float current, float target, float riseTime, float fallTime, float dt)
        {
            float tau = Math.Max(0.01f, target > current ? riseTime : fallTime);
            return current + (target - current) * (1f - (float)Math.Exp(-dt / tau));
        }
    }
}
