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

        /// <summary>
        /// Рисуемая высота для сетки с этим шагом: без ряби (сетка её не держит, рябь рисует свет по
        /// <see cref="SurfaceSample.RippleShiftMm"/>). Шаг 0 — точная высота.
        /// </summary>
        long SampleHeightMm(long xMm, long zMm, long footprintMm) => SampleHeightMm(xMm, zMm);

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

        /// <summary>Длина крупной ряби, мм. Делит период текстур 4096 м.</summary>
        public const long RippleLengthMm = 640;

        /// <summary>Сдвиг гребней, мм: гребень там, где (x + сдвиг) mod длины на пике профиля 70/30. Ветер на восток.</summary>
        public readonly long RippleShiftMm;

        /// <summary>Сдвиг не выходит за ±это, мм: кольца земли пишут его в байт.</summary>
        public const long RippleShiftMaxMm = 700;

        /// <summary>Амплитуда ряби, мм, уже умноженная на маску. 0 — ряби нет. От впадины до гребня.</summary>
        public readonly long RippleAmplitudeMm;

        public SurfaceSample(SurfaceKind kind, int looseness, int roughness = 0, int ripple = 0,
            long rippleShiftMm = 0, long rippleAmplitudeMm = 0)
        {
            Kind = kind;
            Looseness = looseness;
            Roughness = roughness;
            Ripple = ripple;
            RippleShiftMm = rippleShiftMm;
            RippleAmplitudeMm = rippleAmplitudeMm;
        }
    }

    public static class WorldQueryExtensions
    {
        public static long SampleHeightMm(this IWorldQuery world, WorldPos p) => world.SampleHeightMm(p.X, p.Z);
    }
}
