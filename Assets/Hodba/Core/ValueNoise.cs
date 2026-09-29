namespace Hodba.Core
{
    /// <summary>
    /// Шум значений на фиксированной точке: вход в мм, выход Q16 (0..65535).
    /// Никакого float — результат побитно одинаков на сервере и на телефоне.
    /// </summary>
    public static class ValueNoise
    {
        public const int One = 65536;

        public static int Sample(long xMm, long zMm, long cellMm, uint seed)
        {
            long cx = WorldPos.FloorDiv(xMm, cellMm);
            long cz = WorldPos.FloorDiv(zMm, cellMm);
            long fx = (xMm - cx * cellMm) * One / cellMm;
            long fz = (zMm - cz * cellMm) * One / cellMm;

            long tx = Smooth(fx);
            long tz = Smooth(fz);

            long v00 = Hash.Q16(Hash.Cell(cx, cz, seed));
            long v10 = Hash.Q16(Hash.Cell(cx + 1, cz, seed));
            long v01 = Hash.Q16(Hash.Cell(cx, cz + 1, seed));
            long v11 = Hash.Q16(Hash.Cell(cx + 1, cz + 1, seed));

            long a = v00 + (((v10 - v00) * tx) >> 16);
            long b = v01 + (((v11 - v01) * tx) >> 16);
            return (int)(a + (((b - a) * tz) >> 16));
        }

        /// <summary>
        /// Сумма октав. Каждая следующая октава вдвое мельче и вдвое слабее.
        /// Результат Q16 со знаком: -65536..65536.
        /// </summary>
        public static int Fbm(long xMm, long zMm, long cellMm, int octaves, uint seed)
        {
            long sum = 0;
            long norm = 0;
            long amp = One;
            long cell = cellMm;
            for (int i = 0; i < octaves && cell > 0; i++)
            {
                long n = Sample(xMm, zMm, cell, seed + (uint)i * 1013u) * 2 - One;
                sum += (n * amp) >> 16;
                norm += amp;
                amp >>= 1;
                cell >>= 1;
            }
            return norm == 0 ? 0 : (int)(sum * One / norm);
        }

        /// <summary>Сглаживание 3t² − 2t³ на Q16.</summary>
        static long Smooth(long t) => (t * t >> 16) * (3 * One - 2 * t) >> 16;
    }
}
