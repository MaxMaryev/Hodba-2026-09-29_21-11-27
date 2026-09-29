namespace Hodba.Core
{
    /// <summary>Целочисленные хэши. Одинаковы на любой платформе — основа детерминированного мира.</summary>
    public static class Hash
    {
        public static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }

        public static uint Cell(long x, long z, uint seed)
        {
            uint h = seed * 0x9E3779B9u;
            h = Mix(h ^ (uint)x ^ (uint)(x >> 32) * 0x85EBCA6Bu);
            h = Mix(h ^ (uint)z * 0xC2B2AE35u ^ (uint)(z >> 32));
            return h;
        }

        public static uint Next(uint h, uint salt) => Mix(h ^ (salt * 0x27D4EB2Fu));

        /// <summary>Значение 0..65535 (Q16).</summary>
        public static int Q16(uint h) => (int)(h >> 16);

        /// <summary>Только для клиента: 0..1.</summary>
        public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
    }
}
