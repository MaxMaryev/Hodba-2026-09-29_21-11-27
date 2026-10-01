using Hodba.Core;

namespace Hodba.World.Gen
{
    /// <summary>
    /// Рельеф на десятки и сотни метров: поля барханов и холмистые равнины между огромными плато.
    /// Где это — решают медленные маски на километры: можно днями идти по столу, а можно выйти к барханам.
    /// Барханы — поперёк ветра, дующего на восток: пологий наветренный склон на запад, крутой подветренный
    /// (не круче угла естественного откоса), 6–30 м; гребни изогнуты и рвутся на отдельные барханы.
    /// Барханы сидят на спинах гигантских пологих валов (драа) в десятки метров.
    /// Только целые числа: сервер видит ту же землю. Все размеры — стартовые, подбираются.
    /// </summary>
    public static class Dunes
    {
        const int One = ValueNoise.One;

        // Где поля барханов и где холмы — километры.
        const long FieldCell = 6_000_000, RollingCell = 4_000_000;

        // Барханы: одна волна, а её длина и изгиб гребней меняются медленным сдвигом фазы.
        // (Смешивать две волны разной длины нельзя: где поровну, гребни одной гасят другую.)
        // Чем выше бархан, тем длиннее волна: подветренный склон (30% длины) не круче откоса.
        const long Length = 380_000;
        const long Rise = One * 7 / 10; // наветренный склон — 70% длины, подветренный — 30%
        const long BendCell = 900_000, BendMm = 80_000;       // изгиб гребней
        const long StretchCell = 3_000_000, StretchMm = 400_000; // длина волны гуляет на ±25%
        const long SegmentCell = 350_000;
        const long AmpCell = 2_000_000, MinMm = 6_000, MaxMm = 30_000;

        // Гигантские дюны (драа): пологие валы на километры, на их спинах — барханы.
        const long Draa = 3_000_000;
        const long DraaRise = One * 6 / 10;
        const long DraaMinMm = 15_000, DraaMaxMm = 60_000;

        // Холмистая равнина: пологие бугры в несколько метров на сотни метров.
        const long RollCell = 300_000, RollMm = 6_000;

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

            long bend = (long)ValueNoise.Fbm(x, z, BendCell, 2, seed + 311) * BendMm >> 16;
            long stretch = (long)ValueNoise.Fbm(x, z, StretchCell, 2, seed + 321) * StretchMm >> 16;
            long u = x + bend + stretch;
            long wave = MicroRelief.Wave(u, Length, Rise);

            // Гребень изредка рвётся на отдельные барханы — между ними проходы.
            int segment = MicroRelief.SmoothQ(ValueNoise.Sample(u, z, SegmentCell, seed + 331), One * 12 / 100, One * 45 / 100);
            long amp = MinMm + ((MaxMm - MinMm) * ValueNoise.Sample(x, z, AmpCell, seed + 341) >> 16);

            long dune = wave * segment >> 16;
            dune = dune * amp >> 16;

            // Под барханами — гигантский вал: пески поднимаются на десятки метров, барханы сидят на его спине.
            long draaWave = MicroRelief.Wave(u, Draa, DraaRise);
            long draaAmp = DraaMinMm + ((DraaMaxMm - DraaMinMm) * ValueNoise.Sample(x, z, 5_000_000, seed + 371) >> 16);
            long draa = draaWave * draaAmp >> 16;

            return h + ((dune + draa) * field >> 16);
        }
    }
}
