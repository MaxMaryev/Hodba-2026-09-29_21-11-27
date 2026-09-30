using Hodba.Core;

namespace Hodba.World
{
    /// <summary>
    /// Контракт мира. Остальной код общается с миром только через него — генератор можно заменить целиком.
    /// См. Docs/Design/02-world-generation.md. Контракт растёт осторожно: вода, линии, ориентиры, места появятся позже.
    /// </summary>
    public interface IWorldQuery
    {
        WorldInfo Info { get; }

        /// <summary>Высота земли в мм.</summary>
        long SampleHeightMm(long xMm, long zMm);

        SurfaceSample SampleSurface(long xMm, long zMm);
    }

    public readonly struct WorldInfo
    {
        public readonly uint Seed;
        public readonly int Version;
        public readonly string GeneratorId;

        public WorldInfo(uint seed, int version, string generatorId)
        {
            Seed = seed;
            Version = version;
            GeneratorId = generatorId;
        }
    }

    public enum SurfaceKind : byte
    {
        FineAsh,
        PackedAsh,
        Stone,
    }

    public readonly struct SurfaceSample
    {
        public readonly SurfaceKind Kind;

        /// <summary>Рыхлость Q16: 0 — твёрдо, 65535 — вязнешь.</summary>
        public readonly int Looseness;

        /// <summary>Неровность под ногами Q16: 0 — стекло, 65535 — бугры и наносы. Свойство места, а не шага.</summary>
        public readonly int Roughness;

        /// <summary>Рябь от ветра Q16: насколько здесь выражены волны на пепле.</summary>
        public readonly int Ripple;

        public SurfaceSample(SurfaceKind kind, int looseness, int roughness = 0, int ripple = 0)
        {
            Kind = kind;
            Looseness = looseness;
            Roughness = roughness;
            Ripple = ripple;
        }
    }

    public static class WorldQueryExtensions
    {
        public static long SampleHeightMm(this IWorldQuery world, WorldPos p) => world.SampleHeightMm(p.X, p.Z);
    }
}
