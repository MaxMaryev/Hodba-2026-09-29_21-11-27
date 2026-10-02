using Hodba.Core;

namespace Hodba.World.Gen
{
    /// <summary>Рельеф под ногами в одной точке.</summary>
    public readonly struct Relief
    {
        /// <summary>Добавка к высоте, мм.</summary>
        public readonly long HeightMm;
        /// <summary>Неровность Q16.</summary>
        public readonly int Roughness;
        /// <summary>Выраженность ряби Q16.</summary>
        public readonly int Ripple;

        public Relief(long heightMm, int roughness, int ripple)
        {
            HeightMm = heightMm;
            Roughness = roughness;
            Ripple = ripple;
        }
    }

    /// <summary>
    /// Мелкий рельеф пустыни: земля не всегда стекло. Характер задаёт место, а не шаг: где-то час идёшь
    /// почти по столу (только зерно корки), где-то пепел в ряби от ветра, где-то бугры и наносы.
    /// Вернёшься — будет то же. Только целые числа: сервер видит ту же землю, что телефон.
    /// Все размеры — стартовые, подбираются.
    /// </summary>
    public static class MicroRelief
    {
        const int One = ValueNoise.One;

        // Характер места: медленные маски.
        const long BumpZoneCell = 520_000, RippleZoneCell = 340_000;

        // Рябь: одна волна (смешивать две нельзя: где поровну, гребни гасят друг друга). Ровные бесконечные гребни
        // читаются пашнёй до горизонта, поэтому гребни петляют, шаг гуляет, а каждый гребень живёт сам по себе.
        // Сдвиг меняется вдоль гребня (z) вдвое быстрее, чем поперёк: гребни плавно изгибаются.
        const long RippleBendCell = 7_000, RippleBendStretch = 2, RippleBendMm = 580;
        // Шаг гребней гуляет на ±10%: ровный шаг — главный признак вельвета.
        const long RippleSpacingCell = 2_500, RippleSpacingMm = SurfaceSample.RippleShiftMaxMm - RippleBendMm;
        const long RippleMinMm = 10, RippleMaxMm = 30;
        // Свой у каждого гребня: высота и обрывы, шум вдоль него с клеткой 3,2 м (делит шаг переноса центра мира —
        // шейдер повторяет его по номеру гребня). Около трети гребня в каждом месте нет.
        public const long CrestCellMm = 3_200;
        public const int CrestIndexMask = 0xFFFFF;
        // Средняя высота гребней с их обрывами: рябь не поднимает землю в среднем.
        const long CrestMeanQ = One * 22 / 100;

        // Бугры и наносы.
        const long BumpCell = 2_500, BumpMm = 60, DriftCell = 9_000, DriftMm = 40;

        // Зерно корки.
        const long GrainCell = 600, GrainMm = 4;

        /// <summary>Характер места: где рябь, где бугры. Рябь живёт только на рыхлом.</summary>
        public static void Masks(long x, long z, uint seed, int looseness, out int ripple, out int bumps)
        {
            int b = Unsigned(ValueNoise.Fbm(x, z, BumpZoneCell, 2, seed + 201));
            bumps = SmoothQ(b, One * 58 / 100, One * 72 / 100);

            int r = Unsigned(ValueNoise.Fbm(x, z, RippleZoneCell, 2, seed + 211));
            int zone = SmoothQ(r, One * 45 / 100, One * 62 / 100);
            int loose = SmoothQ(looseness, One * 45 / 100, One * 75 / 100);
            ripple = (int)((long)zone * loose >> 16);
        }

        /// <param name="footprintMm">Шаг сетки, которая это рисует; 0 — точная высота. Сетка не держит рябь — её рисует свет.</param>
        public static Relief Evaluate(long x, long z, uint seed, int looseness, int ripple, int bumps, long footprintMm = 0)
        {
            long h = 0;

            // Зерно корки: везде, на твёрдом заметнее.
            long grain = ValueNoise.Sample(x, z, GrainCell, seed + 261) - One / 2;
            h += grain * GrainMm * 2 * (One - looseness / 2) >> 32;

            if (footprintMm <= 0 && ripple > 0)
            {
                RippleWave(x, z, seed, ripple, out long shift, out long amplitude);
                h += RippleHeightMm(x, z, seed, shift, amplitude);
            }

            if (bumps > 0)
            {
                long bump = (long)ValueNoise.Fbm(x, z, BumpCell, 2, seed + 251) * BumpMm >> 16;
                long drift = (long)ValueNoise.Fbm(x, z, DriftCell, 2, seed + 271) * DriftMm >> 16;
                h += (bump + drift) * bumps >> 16;
            }

            int roughness = RoughnessOf(ripple, bumps);
            return new Relief(h, roughness, ripple);
        }

        /// <summary>
        /// Сдвиг и амплитуда ряби. Амплитуда уже включает маску места: 0 там, где ряби нет. Плавные — их рисуют вершины колец.
        /// Гребень — где (x + сдвиг) по модулю длины волны на пике профиля 70/30.
        /// </summary>
        public static void RippleWave(long x, long z, uint seed, int mask, out long shiftMm, out long amplitudeMm)
        {
            if (mask <= 0)
            {
                shiftMm = 0;
                amplitudeMm = 0;
                return;
            }

            long bend = ValueNoise.Fbm(WorldPos.FloorDiv(x, RippleBendStretch), z, RippleBendCell, 2, seed + 221);
            long spacing = ValueNoise.Fbm(x, WorldPos.FloorDiv(z, RippleBendStretch), RippleSpacingCell, 2, seed + 225);
            shiftMm = (bend * RippleBendMm + spacing * RippleSpacingMm) >> 16;
            long amp = RippleMinMm + ((RippleMaxMm - RippleMinMm) * ValueNoise.Sample(x, z, 120_000, seed + 241) >> 16);
            amplitudeMm = amp * mask >> 16;
        }

        /// <summary>
        /// Вклад ряби в высоту, мм. Впадина — на нуле профиля, гребень — на своей высоте (<see cref="CrestMask"/>):
        /// гребни сменяют друг друга во впадине, где волна — ноль, поэтому ступенек нет.
        /// </summary>
        public static long RippleHeightMm(long x, long z, uint seed, long shiftMm, long amplitudeMm)
        {
            if (amplitudeMm == 0) return 0;
            long u = x + shiftMm;
            long wave = Wave(u, SurfaceSample.RippleLengthMm);
            long crest = WorldPos.FloorDiv(u, SurfaceSample.RippleLengthMm);
            long shape = (wave * CrestMask(crest, z, seed) >> 16) - CrestMeanQ;
            return shape * amplitudeMm >> 16;
        }

        /// <summary>
        /// 0..One: высота гребня номер crest в точке z — шум вдоль гребня; около трети гребня оборвано.
        /// Шейдер земли повторяет это по тем же номерам (HodbaRipple.hlsl).
        /// </summary>
        public static int CrestMask(long crest, long z, uint seed)
        {
            long cz = WorldPos.FloorDiv(z, CrestCellMm);
            long t = (z - cz * CrestCellMm) * One / CrestCellMm;
            t = (t * t >> 16) * (3 * One - 2 * t) >> 16;
            long k = crest & CrestIndexMask;
            long a = Hash.Q16(Hash.Cell(k, cz & CrestIndexMask, seed + 281));
            long b = Hash.Q16(Hash.Cell(k, (cz + 1) & CrestIndexMask, seed + 281));
            long n = a + ((b - a) * t >> 16);
            long on = SmoothQ(n, One * 28 / 100, One * 45 / 100);
            return (int)(on * (One / 2 + n / 2) >> 16);
        }

        /// <summary>Асимметричная волна 0..One: подъём на 70% длины, спад на 30%, без изломов.</summary>
        static long Wave(long u, long length) => Wave(u, length, One * 7 / 10);

        /// <summary>Асимметричная волна 0..One: подъём на долю rise (Q16) длины, спад на остаток, без изломов.</summary>
        public static long Wave(long u, long length, long rise)
        {
            long p = WorldPos.FloorMod(u, length) * One / length; // 0..One
            return p < rise
                ? SmoothQ(p * One / rise, 0, One)
                : SmoothQ(One - (p - rise) * One / (One - rise), 0, One);
        }

        /// <summary>Плавная ступень на Q16: 0 до lo, One после hi, 3t² − 2t³ между.</summary>
        public static int SmoothQ(long v, long lo, long hi)
        {
            if (v <= lo) return 0;
            if (v >= hi) return One;
            long t = (v - lo) * One / (hi - lo);
            return (int)((t * t >> 16) * (3 * One - 2 * t) >> 16);
        }

        /// <summary>Неровность места: бугры целиком, рябь — меньше (по ней идти легче).</summary>
        public static int RoughnessOf(int ripple, int bumps) => System.Math.Max(bumps, ripple * 2 / 5);

        /// <summary>−One..One → 0..One.</summary>
        public static int Unsigned(int signed) => (signed + One) / 2;
    }
}
