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

        // Рябь: две волны разной длины, смешанные по месту, — длина «гуляет» без разрыва фазы.
        const long RippleA = 650, RippleB = 850;
        const long RippleWarp = 350, RippleWarpCell = 7_000;
        const long RippleMinMm = 10, RippleMaxMm = 30;

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

        public static Relief Evaluate(long x, long z, uint seed, int looseness, int ripple, int bumps)
        {
            long h = 0;

            // Зерно корки: везде, на твёрдом заметнее.
            long grain = ValueNoise.Sample(x, z, GrainCell, seed + 261) - One / 2;
            h += grain * GrainMm * 2 * (One - looseness / 2) >> 32;

            if (ripple > 0) h += RippleHeight(x, z, seed) * ripple >> 16;

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
        /// Рябь поперёк ветра, дующего на восток: пологий наветренный склон (на запад), крутой подветренный.
        /// Гребни слегка изогнуты.
        /// </summary>
        static long RippleHeight(long x, long z, uint seed)
        {
            long warp = (long)ValueNoise.Fbm(x, z, RippleWarpCell, 2, seed + 221) * RippleWarp >> 16;
            long u = x + warp;
            int mix = ValueNoise.Sample(x, z, 60_000, seed + 231);
            long wave = (Wave(u, RippleA) * (One - mix) + Wave(u + 311, RippleB) * mix) >> 16;
            long amp = RippleMinMm + ((RippleMaxMm - RippleMinMm) * ValueNoise.Sample(x, z, 120_000, seed + 241) >> 16);
            return (wave - One / 2) * amp >> 16;
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
