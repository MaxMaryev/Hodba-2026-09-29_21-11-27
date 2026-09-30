using System;
using Hodba.Client;
using Hodba.World;
using Hodba.World.Gen;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class WorldReliefTests
    {
        [Test]
        public void Relief_IsTheSame_WhenYouComeBack()
        {
            var a = new FlatStub(3);
            var b = new FlatStub(3);
            for (long i = 0; i < 2000; i++)
            {
                long x = i * 7_919 - 3_000_000, z = i * 104_729 % 5_000_000;
                Assert.AreEqual(a.SampleHeightMm(x, z), b.SampleHeightMm(x, z));
                var sa = a.SampleSurface(x, z);
                var sb = b.SampleSurface(x, z);
                Assert.AreEqual(sa.Looseness, sb.Looseness);
                Assert.AreEqual(sa.Roughness, sb.Roughness);
                Assert.AreEqual(sa.Ripple, sb.Ripple);
            }
        }

        [Test]
        public void Relief_HasNoSteps_AndStaysSmall()
        {
            var w = new FlatStub(3);
            long worstStep = 0, worstMicro = 0;
            for (int line = 0; line < 40; line++)
            {
                long x0 = line * 97_000 - 2_000_000, z = line * 131_000;
                long prev = w.SampleHeightMm(x0, z);
                for (long x = x0 + 50; x < x0 + 60_000; x += 50)
                {
                    long h = w.SampleHeightMm(x, z);
                    worstStep = Math.Max(worstStep, Math.Abs(h - prev));
                    prev = h;

                    var s = w.SampleSurface(x, z);
                    MicroRelief.Masks(x, z, 3, s.Looseness, out int ripple, out int bumps);
                    worstMicro = Math.Max(worstMicro, Math.Abs(MicroRelief.Evaluate(x, z, 3, s.Looseness, ripple, bumps).HeightMm));
                }
            }
            TestContext.WriteLine($"худший перепад на 5 см: {worstStep} мм, самый большой мелкий рельеф: {worstMicro} мм");
            Assert.That(worstStep, Is.LessThanOrEqualTo(25), "ступенек нет");
            Assert.That(worstMicro, Is.LessThanOrEqualTo(150));
        }

        /// <summary>Где-то час идёшь почти по столу, где-то рябь, где-то бугры: зоны есть, и заметные.</summary>
        [Test]
        public void Places_HaveCharacter()
        {
            var w = new FlatStub(3);
            int glass = 0, ripple = 0, bumps = 0, n = 0, rippleOnCrust = 0;
            for (long z = -20_000_000; z < 20_000_000; z += 97_000)
            for (long x = -20_000_000; x < 20_000_000; x += 97_000)
            {
                var s = w.SampleSurface(x, z);
                n++;
                if (s.Roughness < 3_000 && s.Ripple < 3_000) glass++;
                if (s.Ripple > 32_000) ripple++;
                if (s.Roughness > 32_000 && s.Ripple < s.Roughness) bumps++;
                if (s.Kind == SurfaceKind.PackedAsh && s.Ripple > 0) rippleOnCrust++;
            }
            TestContext.WriteLine($"почти стол {100f * glass / n:0}%, рябь {100f * ripple / n:0}%, бугры {100f * bumps / n:0}%");
            Assert.That(glass, Is.InRange(n / 5, n * 7 / 10), "гладкого много, но не всё");
            Assert.That(ripple, Is.GreaterThan(n / 20));
            Assert.That(bumps, Is.GreaterThan(n / 20));
            Assert.AreEqual(0, rippleOnCrust, "рябь живёт только на рыхлом");
        }

        [Test]
        public void AshAndCrust_BlendOverMeters()
        {
            var w = new FlatStub(3);
            int worst = 0;
            for (long x = -5_000_000; x < 5_000_000; x += 1_000)
                worst = Math.Max(worst, Math.Abs(w.SampleSurface(x + 1_000, 7_000).Looseness - w.SampleSurface(x, 7_000).Looseness));
            TestContext.WriteLine($"худший скачок рыхлости на метр: {worst}");
            Assert.That(worst, Is.LessThan(10_000), "не по линии, а полосой");
        }

        /// <summary>Картинка = ощущение: в кольце земли ровно то, что мир говорит о высоте и поверхности.</summary>
        [Test]
        public void Ground_ShowsWhatTheFeetFeel()
        {
            var w = new FlatStub(3);
            var level = new ClipmapLevel(0, 250);
            level.MoveTo(4_000, -2_000, w);
            for (long gz = -2_001; gz <= -2_000 + ClipmapLevel.Grid; gz += 7)
            for (long gx = 3_999; gx <= 4_000 + ClipmapLevel.Grid; gx += 5)
            {
                Assert.AreEqual(w.SampleHeightMm(gx * 250, gz * 250) / 1000f, level.HeightAt(gx, gz));
                Assert.AreEqual(ClipmapLevel.SurfaceColor(w.SampleSurface(gx * 250, gz * 250)),
                    level.Surface[ClipmapLevel.Texel(gx, gz)]);
            }
        }

        /// <summary>Досчитывая только въехавшее, кольцо получается ровно таким же, как посчитанное заново.</summary>
        [Test]
        public void Ground_ScrollingEqualsRebuilding()
        {
            var w = new FlatStub(5);
            var scrolled = new ClipmapLevel(0, 250);
            long ox = 100, oz = 200;
            scrolled.MoveTo(ox, oz, w);
            int[] dx = { 1, 3, -2, 0, 17, -40, 2 }, dz = { 0, -1, 4, 9, -3, 5, -60 };
            for (int k = 0; k < dx.Length; k++)
            {
                ox += dx[k];
                oz += dz[k];
                scrolled.MoveTo(ox, oz, w);
                var fresh = new ClipmapLevel(0, 250);
                fresh.MoveTo(ox, oz, w);
                for (long gz = oz - 1; gz <= oz + ClipmapLevel.Grid; gz++)
                for (long gx = ox - 1; gx <= ox + ClipmapLevel.Grid; gx++)
                    Assert.AreEqual(fresh.HeightAt(gx, gz), scrolled.HeightAt(gx, gz), $"сдвиг {k}: ({gx},{gz})");
            }
        }

        /// <summary>Каждое кольцо ложится в следующее ровно: с полосой в клетку, дырка — в одном из двух положений.</summary>
        [Test]
        public void Rings_NestExactly()
        {
            var ox = new long[9];
            var rng = new System.Random(7);
            for (int k = 0; k < 2000; k++)
            {
                long focus = (long)(rng.NextDouble() * 2e9) - 1_000_000_000;
                ClipmapLevel.Origins(focus, 250, ox);
                for (int l = 0; l < ox.Length; l++)
                {
                    Assert.AreEqual(0, ox[l] & 1, "угол — на чётной вершине");
                    long spacing = 250L << l;
                    Assert.That(focus, Is.InRange(ox[l] * spacing, (ox[l] + ClipmapLevel.Grid - 1) * spacing), "путник внутри кольца");
                    if (l == 0) continue;
                    int hole = ClipmapLevel.Hole(ox[l - 1], ox[l]);
                    Assert.That(hole, Is.InRange(ClipmapLevel.HoleStart, ClipmapLevel.HoleStart + 1));
                }
            }
        }

        [Test]
        public void ProvingGround_WalksThroughEveryCharacter()
        {
            var w = new ProvingGround(1);
            Assert.That(w.SampleSurface(0, 30_000).Roughness, Is.EqualTo(0), "стекло");
            Assert.That(w.SampleSurface(0, 100_000).Ripple, Is.GreaterThan(60_000), "рябь");
            Assert.That(w.SampleSurface(0, 170_000).Roughness, Is.EqualTo(0), "корка на подъёме");
            Assert.That(w.SampleSurface(0, 260_000).Roughness, Is.GreaterThan(60_000), "бугры");
        }
    }
}
