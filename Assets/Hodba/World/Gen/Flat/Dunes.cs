using Hodba.Core;

namespace Hodba.World.Gen
{
    /// <summary>
    /// Рельеф на десятки и сотни метров: поля барханов и холмистые равнины между огромными плато.
    /// Где это — решают медленные маски на километры: можно днями идти по столу, а можно выйти к барханам.
    /// Барханы — поперёк ветра, дующего на восток: пологий наветренный склон на запад, крутой подветренный
    /// (не круче угла естественного откоса). Гребни изогнуты и рвутся на отдельные барханы.
    /// Только целые числа: сервер видит ту же землю. Все размеры — стартовые, подбираются.
    /// </summary>
    public static class Dunes
    {
        const int One = ValueNoise.One;

        // Где поля барханов и где холмы — километры.
        const long FieldCell = 6_000_000, RollingCell = 4_000_000;

        // Барханы: две длины волны, смешанные по месту, — длина «гуляет» без разрыва фазы.
        const long LengthA = 140_000, LengthB = 220_000;
        const long Rise = One * 7 / 10; // наветренный склон — 70% длины, подветренный — 30%
        const long WarpCell = 400_000, WarpMm = 60_000;
        const long SegmentCell = 160_000;
        const long AmpCell = 2_000_000, MinMm = 3_000, MaxMm = 12_000;

        // Холмистая равнина: пологие бугры в пару метров на сотню-другую метров.
        const long RollCell = 150_000, RollMm = 2_500;

        /// <summary>0..One — насколько здесь поле барханов.</summary>
        public static int Field(long x, long z, uint seed) =>
            MicroRelief.SmoothQ(MicroRelief.Unsigned(ValueNoise.Fbm(x, z, FieldCell, 2, seed + 301)), One * 52 / 100, One * 66 / 100);

        /// <summary>0..One — насколько здесь холмистая равнина (а не стол).</summary>
        public static int Rolling(long x, long z, uint seed) =>
            MicroRelief.SmoothQ(MicroRelief.Unsigned(ValueNoise.Fbm(x, z, RollingCell, 2, seed + 351)), One * 45 / 100, One * 60 / 100);

        /// <summary>Добавка к высоте, мм: барханы и холмы. field — из <see cref="Field"/>.</summary>
        public static long Height(long x, long z, uint seed, int field)
        {
            long h = 0;

            int rolling = Rolling(x, z, seed);
            if (rolling > 0)
                h += ((long)ValueNoise.Fbm(x, z, RollCell, 2, seed + 361) * RollMm >> 16) * rolling >> 16;

            if (field <= 0) return h;

            long warp = (long)ValueNoise.Fbm(x, z, WarpCell, 2, seed + 311) * WarpMm >> 16;
            long u = x + warp;
            int mix = ValueNoise.Sample(x, z, 900_000, seed + 321);
            long wave = (MicroRelief.Wave(u, LengthA, Rise) * (One - mix)
                         + MicroRelief.Wave(u + 37_000, LengthB, Rise) * mix) >> 16;

            // Гребень рвётся на отдельные барханы, между ними — проходы.
            int segment = MicroRelief.SmoothQ(ValueNoise.Sample(u, z, SegmentCell, seed + 331), One / 4, One * 3 / 4);
            long amp = MinMm + ((MaxMm - MinMm) * ValueNoise.Sample(x, z, AmpCell, seed + 341) >> 16);

            long dune = wave * segment >> 16;
            dune = dune * amp >> 16;
            return h + (dune * field >> 16);
        }
    }
}
