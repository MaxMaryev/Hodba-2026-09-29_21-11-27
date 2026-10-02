using System;

namespace Hodba.World.Gen
{
    /// <summary>
    /// Полигон для настройки тела: один прямой маршрут на север от (0,0), где за несколько минут
    /// проходишь всё, что ходьба должна различать. Полосы поперёк курса, так что шаг с твёрдого
    /// на рыхлое делает сначала одна нога, потом другая.
    /// 0–60 м — твёрдо, стекло; 60–130 м — рыхлый пепел в ряби, с лёгкой волной; 130–210 м — подъём ~12%
    /// по корке с зерном; 210–310 м — плато в буграх и наносах; 320–520 м — бархан поперёк пути: пологий
    /// наветренный подъём на 20 м и крутой подветренный спуск (не круче откоса). Дальше и позади старта — стекло.
    /// </summary>
    public sealed class ProvingGround : IWorldQuery
    {
        public const string Id = "proving-ground";

        const long HardEnd = 60_000, LooseEnd = 130_000, ClimbEnd = 210_000;
        const double Grade = 0.12;
        const long Ease = 6_000; // мягкий перегиб в начале и в конце подъёма, мм
        const long Blend = 4_000; // характер рельефа меняется на 4 м, а не по линии
        const long BumpsEnd = 310_000, DuneStart = 320_000, DuneCrest = 470_000, DuneEnd = 520_000;
        const double DuneHeight = 20_000;

        readonly uint _seed;

        public ProvingGround(uint seed)
        {
            _seed = seed;
            Info = new WorldInfo(seed, 3, Id);
        }

        public WorldInfo Info { get; }

        public long SampleHeightMm(long xMm, long zMm) => Height(xMm, zMm, 0);

        public long SampleHeightMm(long xMm, long zMm, long footprintMm) => Height(xMm, zMm, footprintMm);

        long Height(long xMm, long zMm, long footprintMm)
        {
            long h = 0;

            // Волна на рыхлом: 15 см на 20 м — ноги её чувствуют, глаз почти нет.
            if (zMm > HardEnd && zMm < LooseEnd)
            {
                double t = (zMm - HardEnd) / 20_000.0 * Math.PI * 2.0;
                double fade = Math.Min(1.0, Math.Min(zMm - HardEnd, LooseEnd - zMm) / 5_000.0);
                h += (long)(Math.Sin(t) * 150.0 * fade);
            }

            h += Climb(zMm);
            h += Dune(zMm);

            var s = SampleSurface(xMm, zMm);
            h += MicroRelief.Evaluate(xMm, zMm, _seed, s.Looseness, s.Ripple, Bumps(zMm), footprintMm).HeightMm;
            return h;
        }

        public SurfaceSample SampleSurface(long xMm, long zMm)
        {
            if (zMm < HardEnd) return new SurfaceSample(SurfaceKind.PackedAsh, 8_000);

            if (zMm < LooseEnd)
            {
                int ripple = MicroRelief.SmoothQ(Math.Min(zMm - HardEnd, LooseEnd - zMm), 0, Blend);
                return Rippled(xMm, zMm, 55_000, ripple, 0);
            }

            if (zMm >= DuneStart - Blend && zMm < DuneEnd + Blend)
            {
                // Бархан — сыпучий, в ряби.
                int ripple = MicroRelief.SmoothQ(Math.Min(zMm - (DuneStart - Blend), DuneEnd + Blend - zMm), 0, Blend);
                return Rippled(xMm, zMm, 58_000, ripple, 0);
            }

            int bumps = Bumps(zMm);
            return new SurfaceSample(SurfaceKind.PackedAsh, 20_000, MicroRelief.RoughnessOf(0, bumps), 0);
        }

        SurfaceSample Rippled(long xMm, long zMm, int looseness, int ripple, int bumps)
        {
            MicroRelief.RippleWave(xMm, zMm, _seed, ripple, out long shift, out long amplitude);
            return new SurfaceSample(SurfaceKind.FineAsh, looseness, MicroRelief.RoughnessOf(ripple, bumps), ripple, shift, amplitude);
        }

        /// <summary>Бугры — только на плато за подъёмом.</summary>
        static int Bumps(long zMm) => MicroRelief.SmoothQ(Math.Min(zMm - ClimbEnd, BumpsEnd - zMm), 0, Blend);

        /// <summary>Бархан: пологий наветренный подъём и крутой подветренный спуск, без изломов.</summary>
        static long Dune(long z)
        {
            if (z <= DuneStart || z >= DuneEnd) return 0;
            double t = z < DuneCrest
                ? (z - DuneStart) / (double)(DuneCrest - DuneStart)
                : 1.0 - (z - DuneCrest) / (double)(DuneEnd - DuneCrest);
            return (long)(DuneHeight * t * t * (3.0 - 2.0 * t));
        }

        /// <summary>Подъём со сглаженными перегибами: интеграл от плавно нарастающего уклона.</summary>
        static long Climb(long z)
        {
            double a = LooseEnd, b = ClimbEnd;
            double e = Ease;
            double total = (b - a) * Grade;
            if (z <= a - e) return 0;
            if (z >= b + e) return (long)total;

            // Уклон растёт от 0 до Grade на [a−e, a+e] и спадает на [b−e, b+e]; площадь та же, что у ступени.
            double x = z;
            double rise = Ramp(x, a - e, a + e) - Ramp(x, b - e, b + e);
            return (long)(rise * Grade);
        }

        /// <summary>Интеграл от smoothstep-ступени между p и q: 0 до p, линейно после q.</summary>
        static double Ramp(double x, double p, double q)
        {
            if (x <= p) return 0;
            double w = q - p;
            if (x >= q) return x - (p + q) * 0.5;
            double t = (x - p) / w;
            // ∫ (3t² − 2t³) dt = t³ − t⁴/2
            return w * (t * t * t - 0.5 * t * t * t * t);
        }
    }
}
