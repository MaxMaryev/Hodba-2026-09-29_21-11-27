using Hodba.Core;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>Как разложен один слой камней: мелкие или валуны.</summary>
    public readonly struct StoneLayout
    {
        public readonly float CellSize;
        /// <summary>Среднее число камней на клетку (мелкие).</summary>
        public readonly float PerCell;
        /// <summary>Вероятность валуна в клетке.</summary>
        public readonly float BoulderChance;
        public readonly bool Boulders;
        /// <summary>Номинальный размер, м: от самого частого мелкого до редкого крупного.</summary>
        public readonly Vector2 SizeRange;

        public StoneLayout(float cellSize, float perCell, float boulderChance, bool boulders, Vector2 sizeRange)
        {
            CellSize = cellSize;
            PerCell = perCell;
            BoulderChance = boulderChance;
            Boulders = boulders;
            SizeRange = sizeRange;
        }

        public uint Salt => Boulders ? StoneField.BoulderSalt : StoneField.StoneSalt;
        public long CellMm => (long)(CellSize * 1000f);
    }

    public readonly struct StonePlacement
    {
        public readonly long X, Z;
        /// <summary>Хэш камня: из него рендер берёт поворот, наклон, разброс размера.</summary>
        public readonly uint Hash;
        /// <summary>0..1, чаще мелкие: номер варианта и номинальный размер.</summary>
        public readonly float Skew;

        public StonePlacement(long x, long z, uint hash, float skew)
        {
            X = x;
            Z = z;
            Hash = hash;
            Skew = skew;
        }
    }

    /// <summary>
    /// Где лежат камни — чистая функция от seed мира. Одна на всех: рендер их рисует,
    /// тело через них переступает, взгляд за них цепляется. Раскладку не дублировать.
    /// </summary>
    public static class StoneField
    {
        public const uint StoneSalt = 500, BoulderSalt = 900;

        public static uint CellHash(long cx, long cz, uint worldSeed, in StoneLayout layout) =>
            Hash.Cell(cx, cz, worldSeed + layout.Salt);

        public static int Count(uint cellHash, in StoneLayout layout) =>
            layout.Boulders
                ? (Hash.Unit(cellHash) < layout.BoulderChance ? 1 : 0)
                : Mathf.FloorToInt(Hash.Unit(cellHash) * 2f * layout.PerCell + 0.5f);

        public static StonePlacement Place(long cx, long cz, uint cellHash, int k, in StoneLayout layout)
        {
            long cellMm = layout.CellMm;
            uint s = Hash.Next(cellHash, (uint)k + 1);
            long px = cx * cellMm + (long)(Hash.Unit(Hash.Next(s, 1)) * cellMm);
            long pz = cz * cellMm + (long)(Hash.Unit(Hash.Next(s, 2)) * cellMm);
            float skew = Mathf.Pow(Hash.Unit(Hash.Next(s, 3)), layout.Boulders ? 1.5f : 2.5f);
            return new StonePlacement(px, pz, s, skew);
        }

        public static float NominalSize(in StonePlacement stone, in StoneLayout layout) =>
            Mathf.Lerp(layout.SizeRange.x, layout.SizeRange.y, stone.Skew);

        /// <summary>Ближайший камень, край которого ближе radius к точке.</summary>
        public static bool FindNear(long xMm, long zMm, float radius, uint worldSeed, in StoneLayout layout,
            out StonePlacement found, out float edgeDistance)
        {
            found = default;
            edgeDistance = float.MaxValue;
            long cellMm = layout.CellMm;
            if (cellMm <= 0) return false;

            long reach = (long)((radius + layout.SizeRange.y) * 1000f);
            long x0 = WorldPos.FloorDiv(xMm - reach, cellMm), x1 = WorldPos.FloorDiv(xMm + reach, cellMm);
            long z0 = WorldPos.FloorDiv(zMm - reach, cellMm), z1 = WorldPos.FloorDiv(zMm + reach, cellMm);

            bool any = false;
            for (long cz = z0; cz <= z1; cz++)
            for (long cx = x0; cx <= x1; cx++)
            {
                uint h = CellHash(cx, cz, worldSeed, layout);
                int n = Count(h, layout);
                for (int k = 0; k < n; k++)
                {
                    var p = Place(cx, cz, h, k, layout);
                    float dx = (p.X - xMm) / 1000f, dz = (p.Z - zMm) / 1000f;
                    float d = Mathf.Sqrt(dx * dx + dz * dz) - NominalSize(p, layout) * 0.5f;
                    if (d > radius || d >= edgeDistance) continue;
                    edgeDistance = d;
                    found = p;
                    any = true;
                }
            }
            return any;
        }
    }

    public readonly struct Obstacle
    {
        public readonly long X, Z;
        /// <summary>Поперечник, м.</summary>
        public readonly float Size;

        public Obstacle(long x, long z, float size)
        {
            X = x;
            Z = z;
            Size = size;
        }
    }

    /// <summary>
    /// Что лежит под ногами. Сейчас это камни; потом кусты, обломки, кости — новая реализация,
    /// походка не меняется.
    /// </summary>
    public interface IObstacleQuery
    {
        bool FindNear(WorldPos p, float radius, out Obstacle obstacle, out float edgeDistance);
    }

    public sealed class StoneObstacles : IObstacleQuery
    {
        readonly uint _seed;
        readonly StoneLayout _layout;

        public StoneObstacles(uint worldSeed, in StoneLayout layout)
        {
            _seed = worldSeed;
            _layout = layout;
        }

        public bool FindNear(WorldPos p, float radius, out Obstacle obstacle, out float edgeDistance)
        {
            if (StoneField.FindNear(p.X, p.Z, radius, _seed, _layout, out var s, out edgeDistance))
            {
                obstacle = new Obstacle(s.X, s.Z, StoneField.NominalSize(s, _layout));
                return true;
            }
            obstacle = default;
            return false;
        }
    }
}
