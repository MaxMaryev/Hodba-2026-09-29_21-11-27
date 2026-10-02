using System;
using System.Collections.Generic;
using System.Linq;
using Hodba.Client.Body;
using Hodba.Core;
using Hodba.Sim.Walk;
using Hodba.World;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class GaitTests
    {
        static BodyHarness Walking(IWorldQuery world = null, IObstacleQuery obstacles = null, uint seed = 1)
        {
            var h = new BodyHarness(world ?? new FlatWorld(), obstacles, seed);
            h.Sim.Apply(Intent.Walk());
            return h;
        }

        /// <summary>Параметры шага меняются, но через границу шага — без щелчка (было: до 4 мм).</summary>
        [Test]
        public void Rhythm_IsContinuousAcrossStepBoundaries()
        {
            var h = Walking();
            h.GaitSettings.impactKick = 0f;
            h.GaitSettings.impactNod = 0f;
            const float dt = 1f / 500f;
            h.Run(3f, dt); // разошёлся

            float prev = h.Gait.Pose.Up, prevDelta = 0f, worst = 0f;
            int steps = h.Steps.Count;
            for (int i = 0; i < 10000; i++)
            {
                h.Tick(dt);
                float up = h.Gait.Pose.Up;
                float delta = up - prev;
                if (i > 0) worst = Mathf.Max(worst, Mathf.Abs(delta - prevDelta));
                prevDelta = delta;
                prev = up;
            }
            TestContext.WriteLine($"худшая вторая разность вертикали: {worst * 1000f:0.0000} мм");
            Assert.That(h.Steps.Count - steps, Is.GreaterThan(20), "шаги были");
            Assert.That(worst * 1000f, Is.LessThan(0.3f), "вторая разность вертикали, мм");
        }

        [Test]
        public void Steps_AreNeverCopies_AndDriftSlowly()
        {
            var h = Walking();
            h.Run(20f * 60f, 1f / 30f);

            var t = h.StepTimes.Skip(20).ToArray();
            var d = new double[t.Length - 1];
            for (int i = 0; i < d.Length; i++) d[i] = t[i + 1] - t[i];

            double mean = d.Average();
            double sd = Math.Sqrt(d.Select(x => (x - mean) * (x - mean)).Average());
            TestContext.WriteLine($"шагов {d.Length}, средний {mean:0.000} с, разброс {sd / mean * 100:0.00}%, " +
                                  $"автокорреляция: 1 → {Autocorrelation(d, 1):0.00}, 2 → {Autocorrelation(d, 2):0.00}, " +
                                  $"10 → {Autocorrelation(d, 10):0.00}, 20 → {Autocorrelation(d, 20):0.00}, 100 → {Autocorrelation(d, 100):0.00}");
            Assert.That(sd / mean, Is.InRange(0.01, 0.12), "шаги разные, но это походка, а не хромота");

            // Медленный дрейф: шаги через десяток всё ещё похожи друг на друга (1/f, не белый шум).
            Assert.That(Autocorrelation(d, 10), Is.GreaterThan(0.05));
            Assert.That(Autocorrelation(d, 20), Is.GreaterThan(0.02));
        }

        static double Autocorrelation(double[] x, int lag)
        {
            double m = x.Average();
            double num = 0, den = 0;
            for (int i = 0; i < x.Length; i++)
            {
                den += (x[i] - m) * (x[i] - m);
                if (i + lag < x.Length) num += (x[i] - m) * (x[i + lag] - m);
            }
            return num / den;
        }

        [Test]
        public void SameWalk_At30And60Fps_LooksTheSame()
        {
            var a = Walking();
            var b = Walking();
            double diff = 0, energy = 0;
            int n = 0;
            for (int i = 0; i < 30 * 40; i++)
            {
                a.Tick(1f / 30f);
                b.Tick(1f / 60f);
                b.Tick(1f / 60f);
                if (i < 30 * 5) continue;
                diff += Sq(a.Pose.Up - b.Pose.Up);
                energy += Sq(a.Pose.Up);
                n++;
            }
            TestContext.WriteLine($"30 против 60 fps: расхождение {Math.Sqrt(diff / n) * 1000:0.00} мм при сигнале {Math.Sqrt(energy / n) * 1000:0.00} мм");
            Assert.That(Math.Sqrt(diff / n), Is.LessThan(0.35 * Math.Sqrt(energy / n)));
        }

        static double Sq(double x) => x * x;

        [Test]
        public void LongFrame_GivesAtMostOneFeltStep()
        {
            var h = Walking();
            h.Run(10f, 1f / 30f);
            int before = h.Steps.Count;
            int feltBefore = h.Steps.Count(s => s.Felt);

            h.Tick(1.6f); // провал кадра: ~1,4 м пути, два шага

            Assert.That(h.Steps.Count - before, Is.GreaterThanOrEqualTo(2), "следы не теряются");
            Assert.That(h.Steps.Count(s => s.Felt) - feltBefore, Is.LessThanOrEqualTo(1), "прочувствован один");
        }

        /// <summary>После просадки кадра голова не «звенит»: дальше движение такое же плавное, как до неё.</summary>
        [TestCase(0.1f)]
        [TestCase(0.25f)]
        public void AfterLongFrame_MotionStaysCalm(float hitch)
        {
            var h = Walking();
            h.Run(10f, 1f / 30f);
            float calm = MaxFrameDelta(h, 60);
            h.Tick(hitch);
            Assert.IsFalse(float.IsNaN(h.Pose.Up) || float.IsNaN(h.Pose.Pitch));
            float after = MaxFrameDelta(h, 60);
            Assert.That(after, Is.LessThan(calm * 1.6f + 0.001f), "мм за кадр после просадки");
        }

        static float MaxFrameDelta(BodyHarness h, int frames)
        {
            float prev = h.Pose.Up, worst = 0f;
            for (int i = 0; i < frames; i++)
            {
                h.Tick(1f / 30f);
                worst = Mathf.Max(worst, Mathf.Abs(h.Pose.Up - prev));
                prev = h.Pose.Up;
            }
            return worst;
        }

        [Test]
        public void NoStumbles_WithoutStones()
        {
            var h = Walking(new FlatWorld(60_000));
            h.Run(30f * 60f, 1f / 30f);
            Assert.IsFalse(h.Steps.Any(s => s.Stumble));
            Assert.IsFalse(h.BodyEvents.Any(e => e.Kind == BodyEventKind.Stumble));
        }

        [Test]
        public void StoneEvents_OnlyWhereStonesAre()
        {
            var layout = new StoneLayout(4f, 3f, 0f, false, new Vector2(0.05f, 0.4f));
            var stones = new StoneObstacles(7, layout);
            var h = Walking(new FlatWorld(60_000), stones);
            h.Run(15f * 60f, 1f / 30f);

            var touched = h.Steps.Where(s => s.OnStone || s.Stumble).ToList();
            TestContext.WriteLine($"шагов {h.Steps.Count(s => s.Felt)}: обошёл {h.BodyEvents.Count(e => e.Kind == BodyEventKind.StoneAvoided)}, " +
                                  $"наступил {h.Steps.Count(s => s.OnStone)}, споткнулся {h.Steps.Count(s => s.Stumble)}");
            Assert.That(touched.Count + h.BodyEvents.Count(e => e.Kind == BodyEventKind.StoneAvoided), Is.GreaterThan(0),
                "в густых камнях тело на них реагирует");
            foreach (var s in touched)
                Assert.IsTrue(stones.FindNear(s.Contact, 0.6f, out _, out _), $"камень у {s.Contact}");
        }

        [Test]
        public void Contact_IsUnderTheFoot_NotTheBody()
        {
            var h = Walking();
            h.Run(10f, 1f / 30f);
            var steps = h.Steps.Where(s => s.Felt).Skip(4).Take(10).ToList();
            foreach (var s in steps)
                Assert.That(Math.Abs(s.Contact.X) / 1000.0, Is.EqualTo(h.GaitSettings.footOffset).Within(0.02),
                    "идёт на север: стопа сбоку от оси");
            Assert.IsTrue(steps.Any(s => s.Left) && steps.Any(s => !s.Left));
            Assert.IsTrue(steps.All(s => (s.Contact.X < 0) == s.Left), "левая — слева");
        }

        [Test]
        public void SurfaceIsSampled_UnderEachFoot()
        {
            // Граница твёрдого и рыхлого вдоль курса: слева рыхло, справа твёрдо.
            var h = Walking(new SplitWorld());
            h.Run(20f, 1f / 30f);
            var felt = h.Steps.Where(s => s.Felt).ToList();
            Assert.IsTrue(felt.Where(s => s.Left).All(s => s.Surface == SurfaceKind.FineAsh));
            Assert.IsTrue(felt.Where(s => !s.Left).All(s => s.Surface == SurfaceKind.PackedAsh));
        }

        sealed class SplitWorld : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(1, 0, "split");
            public long SampleHeightMm(long xMm, long zMm) => 0;
            public SurfaceSample SampleSurface(long xMm, long zMm) =>
                xMm < 0 ? new SurfaceSample(SurfaceKind.FineAsh, 60_000) : new SurfaceSample(SurfaceKind.PackedAsh, 5_000);
        }

        [Test]
        public void StartAndStop_AreScenes()
        {
            var h = new BodyHarness(new FlatWorld());
            h.Run(2f, 1f / 30f);
            h.Sim.Apply(Intent.Walk());
            h.Run(8f, 1f / 30f);
            h.Sim.Apply(Intent.Stop());
            h.Run(4f, 1f / 30f);

            var kinds = h.BodyEvents.Select(e => e.Kind).ToList();
            Assert.Contains(BodyEventKind.Start, kinds);
            Assert.Contains(BodyEventKind.Stop, kinds);
            Assert.Contains(BodyEventKind.Settled, kinds);
        }

        [Test]
        public void TurningOnTheSpot_IsStepping()
        {
            var h = new BodyHarness(new FlatWorld());
            h.Run(1f, 1f / 30f);
            h.Sim.Apply(Intent.Course(120f));
            h.Run(8f, 1f / 30f);
            Assert.That(h.Steps.Count, Is.GreaterThanOrEqualTo(3), "переступает, а не вращается");
        }
    }
}
