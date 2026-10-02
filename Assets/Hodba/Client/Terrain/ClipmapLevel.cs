using System;
using Hodba.Core;
using Hodba.World;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Одно кольцо земли: высоты и поверхность в квадрате вокруг путника, с кольцевой адресацией.
    /// Когда путник сдвигается, досчитываются только въехавшие строки и столбцы — остальное уже лежит на месте.
    /// Чистые данные без Unity-текстур: их грузит <see cref="ClipmapTerrain"/>, а проверяют тесты.
    /// </summary>
    public sealed class ClipmapLevel
    {
        /// <summary>Текселей по стороне. Степень двойки — для кольцевой адресации маской.</summary>
        public const int Size = 128;
        /// <summary>Вершин по стороне. Вида 4k+3: тогда внутреннее кольцо ложится ровно, с полосой в клетку.</summary>
        public const int Grid = 123;
        /// <summary>Клеток внутреннего кольца в клетках этого: (Grid − 1) / 2.</summary>
        public const int Inner = (Grid - 1) / 2;
        /// <summary>Где начинается дырка под внутреннее кольцо: здесь или клеткой дальше.</summary>
        public const int HoleStart = (Grid - 1 - Inner) / 2;

        public readonly int Index;
        public readonly long SpacingMm;
        /// <summary>Высота, м, по кольцевому адресу <see cref="Texel"/>.</summary>
        public readonly float[] Heights = new float[Size * Size];
        /// <summary>Что мир говорит о поверхности: r — рыхлость, g — рябь, b — сдвиг гребней, a — амплитуда.</summary>
        public readonly Color32[] Surface = new Color32[Size * Size];

        /// <summary>Мировой индекс вершины в левом нижнем углу, в шагах этого кольца.</summary>
        public long OriginX { get; private set; }
        public long OriginZ { get; private set; }
        public bool Valid { get; private set; }

        public ClipmapLevel(int index, long spacingMm)
        {
            Index = index;
            SpacingMm = spacingMm;
        }

        /// <summary>
        /// Встать углом в (ox, oz). Считается только то, чего ещё нет; с полосой в вершину вокруг — для нормалей.
        /// </summary>
        /// <returns>Изменилось ли что-то.</returns>
        public bool MoveTo(long ox, long oz, IWorldQuery world)
        {
            if (Valid && ox == OriginX && oz == OriginZ) return false;

            long dx = ox - OriginX, dz = oz - OriginZ;
            if (!Valid || Math.Abs(dx) >= Grid || Math.Abs(dz) >= Grid)
                Fill(ox - 1, ox + Grid, oz - 1, oz + Grid, world);
            else
            {
                // Въехавшие столбцы — на всю новую высоту, въехавшие строки — на всю новую ширину.
                if (dx > 0) Fill(OriginX + Grid + 1, ox + Grid, oz - 1, oz + Grid, world);
                else if (dx < 0) Fill(ox - 1, OriginX - 2, oz - 1, oz + Grid, world);
                if (dz > 0) Fill(ox - 1, ox + Grid, OriginZ + Grid + 1, oz + Grid, world);
                else if (dz < 0) Fill(ox - 1, ox + Grid, oz - 1, OriginZ - 2, world);
            }

            OriginX = ox;
            OriginZ = oz;
            Valid = true;
            return true;
        }

        void Fill(long x0, long x1, long z0, long z1, IWorldQuery world)
        {
            for (long gz = z0; gz <= z1; gz++)
            for (long gx = x0; gx <= x1; gx++)
            {
                long x = gx * SpacingMm, z = gz * SpacingMm;
                int t = Texel(gx, gz);
                Heights[t] = world.SampleHeightMm(x, z, SpacingMm) / 1000f;
                Surface[t] = SurfaceColor(world.SampleSurface(x, z));
            }
        }

        public static int Texel(long gx, long gz) =>
            (int)(WorldPos.FloorMod(gz, Size) * Size + WorldPos.FloorMod(gx, Size));

        public float HeightAt(long gx, long gz) => Heights[Texel(gx, gz)];

        /// <summary>То, что знает мир о поверхности, — в цвет для шейдера земли.</summary>
        public static Color32 SurfaceColor(in SurfaceSample s) =>
            new Color32(Byte(s.Looseness), Byte(s.Ripple), ShiftByte(s.RippleShiftMm), AmplitudeByte(s.RippleAmplitudeMm));

        static byte Byte(int q16) => (byte)Math.Min(255, Math.Max(0, q16 >> 8));

        /// <summary>±<see cref="SurfaceSample.RippleShiftMaxMm"/> в байт, шаг около 5,5 мм.</summary>
        public static byte ShiftByte(long shiftMm)
        {
            const long max = SurfaceSample.RippleShiftMaxMm;
            long v = ((shiftMm + max) * 255 + max) / (2 * max);
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
        }

        /// <summary>0..30 мм в байт.</summary>
        public static byte AmplitudeByte(long amplitudeMm)
        {
            long v = amplitudeMm * 255 / 30;
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
        }

        /// <summary>
        /// Где встают кольца, чтобы каждое внутреннее лежало в своём ровно, с полосой в клетку.
        /// Путник — в середине самого мелкого; углы — на чётных вершинах своего кольца.
        /// </summary>
        public static void Origins(long focusMm, long spacing0Mm, long[] origins)
        {
            long cam = WorldPos.FloorDiv(focusMm, spacing0Mm);
            origins[0] = 2 * WorldPos.FloorDiv(cam - Inner, 2);
            for (int l = 1; l < origins.Length; l++)
                origins[l] = 2 * WorldPos.FloorDiv(origins[l - 1] / 2 - HoleStart, 2);
        }

        /// <summary>Сдвиг дырки кольца l (от его угла) по оси: <see cref="HoleStart"/> или на клетку дальше.</summary>
        public static int Hole(long originInner, long origin) => (int)(originInner / 2 - origin);
    }
}
