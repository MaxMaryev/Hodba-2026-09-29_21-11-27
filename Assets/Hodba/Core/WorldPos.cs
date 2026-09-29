using System;

namespace Hodba.Core
{
    /// <summary>
    /// Точка мира в миллиметрах. Мир огромный (десятки тысяч км), float тут не хватает.
    /// Всё авторитетное хранится в WorldPos; клиент переводит в локальные float относительно плавающего центра.
    /// </summary>
    [Serializable]
    public readonly struct WorldPos : IEquatable<WorldPos>
    {
        public const long MmPerMeter = 1000;

        public readonly long X;
        public readonly long Z;

        public WorldPos(long xMm, long zMm)
        {
            X = xMm;
            Z = zMm;
        }

        public static WorldPos FromMeters(double x, double z) =>
            new WorldPos((long)Math.Round(x * MmPerMeter), (long)Math.Round(z * MmPerMeter));

        public double XMeters => X / (double)MmPerMeter;
        public double ZMeters => Z / (double)MmPerMeter;

        public WorldPos Offset(long dxMm, long dzMm) => new WorldPos(X + dxMm, Z + dzMm);

        /// <summary>Привязка к сетке с шагом cellMm (вниз, в том числе для отрицательных).</summary>
        public WorldPos Snap(long cellMm) => new WorldPos(FloorDiv(X, cellMm) * cellMm, FloorDiv(Z, cellMm) * cellMm);

        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        public static long FloorMod(long a, long b)
        {
            long m = a % b;
            if (m != 0 && ((m < 0) != (b < 0))) m += b;
            return m;
        }

        public bool Equals(WorldPos other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is WorldPos p && Equals(p);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Z.GetHashCode();
        public static bool operator ==(WorldPos a, WorldPos b) => a.Equals(b);
        public static bool operator !=(WorldPos a, WorldPos b) => !a.Equals(b);
        public override string ToString() => $"({XMeters:0.000} м, {ZMeters:0.000} м)";
    }
}
