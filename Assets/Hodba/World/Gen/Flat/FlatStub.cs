using Hodba.Core;

namespace Hodba.World.Gen
{
    /// <summary>
    /// Заглушка для вехи «Поле»: бесконечное поле пепла.
    /// Под ногами почти ровно, но не как стол — пологие волны. Вдали — низкие гряды, чтобы горизонт не был линейкой.
    /// </summary>
    public sealed class FlatStub : IWorldQuery
    {
        public const string Id = "flat-stub";

        readonly uint _seed;

        public FlatStub(uint seed)
        {
            _seed = seed;
            Info = new WorldInfo(seed, 1, Id);
        }

        public WorldInfo Info { get; }

        public long SampleHeightMm(long xMm, long zMm)
        {
            // Пологие волны: сотни метров, до ~1,5 м.
            long swell = (long)ValueNoise.Fbm(xMm, zMm, 420_000, 3, _seed) * 1_500 >> 16;

            // Мелкая неровность: десятки метров, ~0,25 м.
            long ripple = (long)ValueNoise.Fbm(xMm, zMm, 48_000, 2, _seed + 7) * 250 >> 16;

            // Гряды: километры, до ~40 м, заострённые — дают силуэт на горизонте.
            int r = ValueNoise.Fbm(xMm, zMm, 5_200_000, 3, _seed + 31);
            long ridgeQ = ValueNoise.One - System.Math.Abs(r);           // 0..One, пик на гребне
            ridgeQ = ridgeQ * ridgeQ >> 16;
            ridgeQ = ridgeQ * ridgeQ >> 16;
            long ridge = ridgeQ * 40_000 >> 16;

            return swell + ripple + ridge;
        }

        public SurfaceSample SampleSurface(long xMm, long zMm)
        {
            int n = ValueNoise.Fbm(xMm, zMm, 256_000, 2, _seed + 101);
            bool packed = n > 16_000;
            return packed
                ? new SurfaceSample(SurfaceKind.PackedAsh, 20_000)
                : new SurfaceSample(SurfaceKind.FineAsh, 45_000);
        }
    }
}
