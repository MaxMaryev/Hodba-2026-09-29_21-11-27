using Hodba.Core;

namespace Hodba.World.Gen
{
    /// <summary>
    /// Заглушка для вехи «Поле»: бесконечное поле пепла.
    /// Огромные плато и холмистые равнины, поля барханов (<see cref="Dunes"/>), вдали — низкие гряды.
    /// Под ногами — где почти стол, где рябь от ветра, где бугры (<see cref="MicroRelief"/>).
    /// Пепел и корка переходят друг в друга на метрах, а не по линии.
    /// </summary>
    public sealed class FlatStub : IWorldQuery
    {
        public const string Id = "flat-stub";

        const int AshLooseness = 45_000, CrustLooseness = 20_000, DuneLooseness = 55_000;

        readonly uint _seed;

        public FlatStub(uint seed)
        {
            _seed = seed;
            Info = new WorldInfo(seed, 4, Id);
        }

        public WorldInfo Info { get; }
        public long SampleHeightMm(long xMm, long zMm) => Height(xMm, zMm, 0);

        public long SampleHeightMm(long xMm, long zMm, long footprintMm) => Height(xMm, zMm, footprintMm);

        long Height(long xMm, long zMm, long footprintMm)
        {
            // Пологие волны: сотни метров, до ~4 м. Даже на плато стол не мёртвый.
            long swell = (long)ValueNoise.Fbm(xMm, zMm, 420_000, 3, _seed) * 4_000 >> 16;

            // Мелкая неровность: десятки метров, ~0,25 м.
            long ripple = (long)ValueNoise.Fbm(xMm, zMm, 48_000, 2, _seed + 7) * 250 >> 16;

            // Гряды: километры, до ~40 м, заострённые — дают силуэт на горизонте.
            int r = ValueNoise.Fbm(xMm, zMm, 5_200_000, 3, _seed + 31);
            long ridgeQ = ValueNoise.One - System.Math.Abs(r);           // 0..One, пик на гребне
            ridgeQ = ridgeQ * ridgeQ >> 16;
            ridgeQ = ridgeQ * ridgeQ >> 16;
            long ridge = ridgeQ * 40_000 >> 16;

            // Барханы и холмы: десятки и сотни метров.
            int field = Dunes.Field(xMm, zMm, _seed);
            long dunes = Dunes.Height(xMm, zMm, _seed, field);

            int loose = Looseness(xMm, zMm, field, out _);
            Masks(xMm, zMm, loose, field, out int rippleMask, out int bumps);
            long micro = MicroRelief.Evaluate(xMm, zMm, _seed, loose, rippleMask, bumps, footprintMm).HeightMm;

            return swell + ripple + ridge + dunes + micro;
        }

        public SurfaceSample SampleSurface(long xMm, long zMm)
        {
            int field = Dunes.Field(xMm, zMm, _seed);
            int loose = Looseness(xMm, zMm, field, out bool packed);
            Masks(xMm, zMm, loose, field, out int ripple, out int bumps);
            int roughness = MicroRelief.RoughnessOf(ripple, bumps);
            MicroRelief.RippleWave(xMm, zMm, _seed, ripple, out long shift, out long amplitude);
            return new SurfaceSample(packed ? SurfaceKind.PackedAsh : SurfaceKind.FineAsh, loose, roughness, ripple, shift, amplitude);
        }

        /// <summary>На барханах — рябь, а не бугры: сыпучее не держит кочек.</summary>
        void Masks(long xMm, long zMm, int loose, int field, out int ripple, out int bumps)
        {
            MicroRelief.Masks(xMm, zMm, _seed, loose, out ripple, out bumps);
            bumps = (int)((long)bumps * (ValueNoise.One - field) >> 16);
        }

        /// <summary>
        /// Рыхлость непрерывна: между пеплом и коркой — полоса в метры. Барханы — сыпучий пепел.
        /// </summary>
        int Looseness(long xMm, long zMm, int field, out bool packed)
        {
            int n = ValueNoise.Fbm(xMm, zMm, 256_000, 2, _seed + 101);
            int t = MicroRelief.SmoothQ(n, 12_000, 20_000);
            int loose = AshLooseness + (int)((long)(CrustLooseness - AshLooseness) * t >> 16);
            loose += (int)((long)(DuneLooseness - loose) * field >> 16);
            packed = loose < ValueNoise.One / 2;
            return loose;
        }
    }
}
