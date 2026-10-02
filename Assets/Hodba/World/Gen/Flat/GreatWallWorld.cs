using System;
using Hodba.Core;

namespace Hodba.World.Gen
{
    /// <summary>The ancient wall is fixed in world coordinates; the desert stays untouched.</summary>
    public sealed class GreatWallWorld : IWorldQuery, IWorldMovementQuery, IWorldSunQuery
    {
        public const float HeightMeters = 500f;
        public const long CenterXMm = 1_800_000;
        public const long HalfThicknessMm = 60_000;
        public const long BayLengthMm = 256_000;
        public const long ButtressHalfWidthMm = 12_000;
        public const long ButtressDepthMm = 32_000;
        const long ClearanceMm = 350;
        readonly IWorldQuery _ground;

        public GreatWallWorld(IWorldQuery ground) => _ground = ground;
        public WorldInfo Info => _ground.Info;
        public long SampleHeightMm(long x, long z) => _ground.SampleHeightMm(x, z);
        public long SampleHeightMm(long x, long z, long footprint) => _ground.SampleHeightMm(x, z, footprint);
        public SurfaceSample SampleSurface(long x, long z) => _ground.SampleSurface(x, z);

        public bool IsSunOccluded(WorldPos position, double heightMeters, double dx, double dy, double dz)
        {
            double x = (position.X - CenterXMm) / 1000.0;
            if (dx * dx + dy * dy + dz * dz < 1e-12) return false;
            if (RayInterval(x,heightMeters,dx,dy,HalfThicknessMm/1000.0,HeightMeters-18,out _,out _)) return true;
            if (!RayInterval(x,heightMeters,dx,dy,(HalfThicknessMm+ButtressDepthMm)/1000.0,HeightMeters,out double near,out double far)) return false;
            double z = position.Z / 1000.0, period = BayLengthMm/1000.0, half = ButtressHalfWidthMm/1000.0;
            if (Math.Abs(dz) < 1e-12) return Math.Abs(z - Math.Round(z/period)*period) <= half;
            if (double.IsPositiveInfinity(far)) return true;
            double a = z+dz*near, b = z+dz*far;
            double lo = Math.Min(a,b), hi = Math.Max(a,b);
            // Test intersection with periodic piers without a loop, even at a grazing angle.
            return Math.Ceiling((lo-half)/period)*period <= hi+half;
        }

        static bool RayInterval(double x, double y, double dx, double dy, double half, double top,
            out double near, out double far)
        {
            near = 0; far = double.PositiveInfinity;
            return RayAxis(x,dx,-half,half,ref near,ref far)
                && RayAxis(y,dy,-100,top,ref near,ref far) && far > 1e-6;
        }

        static bool RayAxis(double p, double d, double min, double max, ref double near, ref double far)
        {
            if (Math.Abs(d) < 1e-12) return p >= min && p <= max;
            double a = (min-p)/d, b = (max-p)/d;
            if (a > b) { double swap=a; a=b; b=swap; }
            near=Math.Max(near,a); far=Math.Min(far,b);
            return near <= far;
        }

        public WorldPos ResolveMovement(WorldPos from, WorldPos to)
        {
            double x = from.X, z = from.Z, dx = to.X - from.X, dz = to.Z - from.Z;
            // At most two perpendicular faces: slide on the first, stop on the second.
            for (int pass = 0; pass < 2; pass++)
            {
                double first = 1;
                bool blockX = false, found = false;
                Check(CenterXMm - HalfThicknessMm - ClearanceMm,
                    CenterXMm + HalfThicknessMm + ClearanceMm, double.NegativeInfinity, double.PositiveInfinity,
                    x, z, dx, dz, ref first, ref blockX, ref found);
                if (Math.Max(x, x + dx) >= CenterXMm - HalfThicknessMm - ButtressDepthMm - ClearanceMm
                    && Math.Min(x, x + dx) <= CenterXMm + HalfThicknessMm + ButtressDepthMm + ClearanceMm)
                {
                    long lo = WorldPos.FloorDiv((long)Math.Min(z, z + dz) - ButtressHalfWidthMm - ClearanceMm, BayLengthMm);
                    long hi = WorldPos.FloorDiv((long)Math.Max(z, z + dz) + ButtressHalfWidthMm + ClearanceMm, BayLengthMm);
                    for (long bay = lo; bay <= hi; bay++)
                        Check(CenterXMm - HalfThicknessMm - ButtressDepthMm - ClearanceMm,
                            CenterXMm + HalfThicknessMm + ButtressDepthMm + ClearanceMm,
                            bay * BayLengthMm - ButtressHalfWidthMm - ClearanceMm,
                            bay * BayLengthMm + ButtressHalfWidthMm + ClearanceMm,
                            x, z, dx, dz, ref first, ref blockX, ref found);
                }
                if (!found) { x += dx; z += dz; break; }
                double safe = Math.Max(0, first - 1.0 / Math.Max(1, Math.Max(Math.Abs(dx), Math.Abs(dz))));
                x += dx * safe; z += dz * safe;
                dx *= 1 - safe; dz *= 1 - safe;
                if (blockX) dx = 0; else dz = 0;
            }
            return new WorldPos((long)Math.Round(x), (long)Math.Round(z));
        }

        static void Check(double minX, double maxX, double minZ, double maxZ,
            double x, double z, double dx, double dz, ref double first, ref bool blockX, ref bool found)
        {
            double enter = 0, exit = 1;
            bool normalX = true;
            if (!Axis(x, dx, minX, maxX, true, ref enter, ref exit, ref normalX)
                || !Axis(z, dz, minZ, maxZ, false, ref enter, ref exit, ref normalX)) return;
            // Existing saves inside the newly introduced wall are handled at spawn.
            if (enter < first && exit > 0) { first = enter; blockX = normalX; found = true; }
        }

        static bool Axis(double p, double d, double min, double max, bool axisX,
            ref double enter, ref double exit, ref bool normalX)
        {
            if (Math.Abs(d) < 0.001) return p > min && p < max;
            double a = (min - p) / d, b = (max - p) / d;
            if (a > b) { double swap = a; a = b; b = swap; }
            if (a > enter) { enter = a; normalX = axisX; }
            exit = Math.Min(exit, b);
            return enter <= exit && exit >= 0 && enter <= 1;
        }

        public static WorldPos OutsideWall(WorldPos p)
        {
            long extent = HalfThicknessMm + ButtressDepthMm + ClearanceMm + 1;
            if (Math.Abs(p.X - CenterXMm) >= extent) return p;
            // Only displace saves actually inside masonry, not those beside a bay.
            long phase = WorldPos.FloorMod(p.Z + BayLengthMm / 2, BayLengthMm) - BayLengthMm / 2;
            long half = Math.Abs(phase) <= ButtressHalfWidthMm + ClearanceMm ? extent : HalfThicknessMm + ClearanceMm + 1;
            if (Math.Abs(p.X - CenterXMm) >= half) return p;
            return new WorldPos(CenterXMm + (p.X > CenterXMm ? half : -half), p.Z);
        }
    }
}
