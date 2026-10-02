using System;
using Hodba.Client;
using Hodba.Core;
using Hodba.World;
using Hodba.World.Gen;
using NUnit.Framework;

namespace Hodba.Tests
{
    public class RippleFootprintTests
    {
        [Test]
        public void RippleCells_DivideTheOriginSnap()
        {
            // Шейдер считает рябь от локальных координат и номеров у центра мира — перенос центра не должен сдвигать гребни.
            foreach (long cell in new[] { SurfaceSample.RippleLengthMm, MicroRelief.CrestCellMm,
                         ClipmapTerrain.FineRippleLengthMm, ClipmapTerrain.FineCrestCellMm })
                Assert.AreEqual(0, FloatingOrigin.SnapMm % cell, $"клетка {cell} мм");
        }

        [Test]
        public void DrawnHeight_WithoutFootprint_MatchesExact()
        {
            var world = new FlatStub(3);
            for (long i = 0; i < 30; i++)
            {
                long x = i * 12_989 - 100_000;
                long z = i * 8_041;
                Assert.AreEqual(world.SampleHeightMm(x, z), world.SampleHeightMm(x, z, 0));
            }
        }

        [Test]
        public void DrawnRipple_IsLightOnly_AndHasNoSteps()
        {
            var world = new FlatStub(11);
            Assert.That(TryFindRipple(world, out long originX, out long originZ), "на рыхлом должна найтись рябь");

            long prev = world.SampleHeightMm(originX, originZ) - world.SampleHeightMm(originX, originZ, 250);
            long worst = 0;
            for (long dx = 1; dx < SurfaceSample.RippleLengthMm * 12; dx++)
            {
                long x = originX + dx;
                var surface = world.SampleSurface(x, originZ);
                long exact = world.SampleHeightMm(x, originZ);
                long drawn = world.SampleHeightMm(x, originZ, 250);
                long wave = MicroRelief.RippleHeightMm(x, originZ, 11, surface.RippleShiftMm, surface.RippleAmplitudeMm);
                Assert.AreEqual(exact, drawn + wave, $"вклад волны в ({x}, {originZ})");
                worst = Math.Max(worst, Math.Abs(wave - prev));
                prev = wave;
            }
            // Гребни разной высоты сменяют друг друга во впадине — там волна ноль, ступенек нет.
            Assert.That(worst, Is.LessThanOrEqualTo(2), "перепад на миллиметр");
        }

        [Test]
        public void Crests_BendAndBreak()
        {
            // Ровные бесконечные гребни читаются пашнёй до горизонта.
            var world = new FlatStub(11);
            long minShift = long.MaxValue, maxShift = long.MinValue;
            int ripples = 0;
            for (long z = -400_000; z < 400_000; z += 1_300)
            for (long x = -400_000; x < 400_000; x += 37_000)
            {
                var s = world.SampleSurface(x, z);
                if (s.Ripple < 60_000) continue;
                ripples++;
                minShift = Math.Min(minShift, s.RippleShiftMm);
                maxShift = Math.Max(maxShift, s.RippleShiftMm);
                Assert.That(Math.Abs(s.RippleShiftMm), Is.LessThanOrEqualTo(SurfaceSample.RippleShiftMaxMm));
            }
            Assert.That(ripples, Is.GreaterThan(500), "рябь нашлась");
            Assert.That(maxShift - minShift, Is.GreaterThan(SurfaceSample.RippleLengthMm), "гребни петляют больше чем на волну");

            int samples = 0, gaps = 0, changes = 0;
            for (long crest = 0; crest < 400; crest++)
            {
                bool wasOn = MicroRelief.CrestMask(crest, 0, 11) > 0;
                for (long z = 0; z < 60_000; z += 200)
                {
                    bool on = MicroRelief.CrestMask(crest, z, 11) > 0;
                    samples++;
                    if (!on) gaps++;
                    if (on != wasOn) changes++;
                    wasOn = on;
                }
            }
            Assert.That(gaps, Is.InRange(samples / 6, samples / 2), "гребни рвутся, но рябь остаётся рябью");
            Assert.That(changes, Is.GreaterThan(400 * 60 / 30), "обрыв — не реже раза на 30 м гребня");
        }

        static bool TryFindRipple(FlatStub world, out long x, out long z)
        {
            for (z = -200_000; z < 200_000; z += 7_000)
            for (x = -200_000; x < 200_000; x += 5_000)
                if (world.SampleSurface(x, z).RippleAmplitudeMm >= 8) return true;
            x = z = 0;
            return false;
        }
    }
}
