using System;
using System.Linq;
using Hodba.Client.Body;
using Hodba.Core;
using Hodba.Sim.Walk;
using Hodba.World;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class UnevenGroundTests
    {
        /// <summary>Левая полоса выше правой на 4 см: идёшь на север — левая нога всё время выше.</summary>
        sealed class Tilted : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(1, 0, "tilted");
            public long SampleHeightMm(long xMm, long zMm) => xMm < 0 ? 40 : 0;
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.PackedAsh, 10_000);
        }

        /// <summary>Рябь: 3 см на 0,7 м поперёк пути.</summary>
        sealed class Rippled : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(1, 0, "rippled");
            public long SampleHeightMm(long xMm, long zMm) => (long)(15.0 * Math.Sin(zMm / 700.0 * Math.PI * 2.0));
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.FineAsh, 50_000, 26_000, 65_536);
        }

        /// <summary>Бугры: высота гуляет на 2,5 м.</summary>
        sealed class Bumpy : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(1, 0, "bumpy");
            public long SampleHeightMm(long xMm, long zMm) =>
                (long)ValueNoise.Fbm(xMm, zMm, 2_500, 2, 5) * 60 >> 16;
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.PackedAsh, 20_000, 65_536);
        }

        static BodyHarness Walking(IWorldQuery world)
        {
            var h = new BodyHarness(world);
            h.Sim.Apply(Intent.Walk());
            return h;
        }

        static float MeanRoll(BodyHarness h, float seconds)
        {
            double sum = 0;
            int n = 0;
            for (float t = 0; t < seconds; t += 1f / 30f)
            {
                h.Tick(1f / 30f);
                sum += h.Gait.Pose.Roll;
                n++;
            }
            return (float)(sum / n);
        }

        [Test]
        public void Body_TiltsWithTheFeet()
        {
            var flat = Walking(new FlatWorld());
            var tilted = Walking(new Tilted());
            flat.Run(5f, 1f / 30f);
            tilted.Run(5f, 1f / 30f);
            float a = MeanRoll(flat, 20f), b = MeanRoll(tilted, 20f);
            TestContext.WriteLine($"средний крен: ровно {a:0.00}°, левая нога выше {b:0.00}°");
            Assert.That(b - a, Is.GreaterThan(0.8f), "корпус кренится к ноге, что ниже");
            Assert.That(b - a, Is.LessThan(4f), "но тело гасит большую часть");
        }

        [Test]
        public void Support_FollowsTheFeet_Smoothly()
        {
            var h = Walking(new Bumpy());
            const float dt = 1f / 500f;
            h.Run(3f, dt);
            float prev = h.Gait.State.SupportHeight, prevDelta = 0f, worst = 0f, min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 10_000; i++)
            {
                h.Tick(dt);
                float s = h.Gait.State.SupportHeight;
                float d = s - prev;
                if (i > 0) worst = Mathf.Max(worst, Mathf.Abs(d - prevDelta));
                prevDelta = d;
                prev = s;
                min = Mathf.Min(min, s);
                max = Mathf.Max(max, s);
            }
            TestContext.WriteLine($"опора гуляет на {(max - min) * 1000f:0} мм, худшая вторая разность {worst * 1000f:0.000} мм");
            Assert.That(max - min, Is.GreaterThan(0.02f), "на буграх тело то выше, то ниже");
            Assert.That(worst * 1000f, Is.LessThan(0.3f), "без скачков");
        }

        /// <summary>Разброс крена вокруг среднего, °.</summary>
        static float RollSpread(BodyHarness h, float seconds)
        {
            var rolls = new System.Collections.Generic.List<float>();
            for (float t = 0; t < seconds; t += 1f / 30f)
            {
                h.Tick(1f / 30f);
                rolls.Add(h.Gait.Pose.Roll);
            }
            float mean = rolls.Average();
            return Mathf.Sqrt(rolls.Select(r => (r - mean) * (r - mean)).Average());
        }

        /// <summary>
        /// На подъёме передняя стопа всегда выше задней — это склон, а не перекос под ногами:
        /// корпус не должен валиться с ноги на ногу, а тело — подниматься ступеньками по шагу.
        /// </summary>
        [Test]
        public void Slope_DoesNotRockTheBody()
        {
            var flat = Walking(new FlatWorld());
            var hill = Walking(new HillWorld());
            flat.Run(5f, 1f / 30f);
            hill.Run(5f, 1f / 30f);
            float a = RollSpread(flat, 20f), b = RollSpread(hill, 20f);
            TestContext.WriteLine($"раскачка крена: ровно {a:0.00}°, в гору {b:0.00}°");
            Assert.That(b, Is.LessThan(a * 1.3f + 0.1f), "склон по ходу не раскачивает корпус");

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 600; i++)
            {
                hill.Tick(1f / 30f);
                float ground = hill.World.SampleHeightMm(hill.Sim.Position.X, hill.Sim.Position.Z) / 1000f;
                float lag = hill.Gait.State.SupportHeight - ground;
                min = Mathf.Min(min, lag);
                max = Mathf.Max(max, lag);
            }
            TestContext.WriteLine($"опора относительно земли под телом гуляет на {(max - min) * 1000f:0} мм");
            Assert.That(max - min, Is.LessThan(0.02f), "в гору — плавно, без ступенек по шагу");
        }

        [Test]
        public void Ripples_DoNotJerkTheWalk()
        {
            var sim = new WalkSim(WalkParams.Default, new WorldPos(0, 0), 0f);
            var world = new Rippled();
            sim.Apply(Intent.Walk());
            for (int i = 0; i < 150; i++) sim.Step(1f / 30f, world);
            var speeds = new float[900];
            for (int i = 0; i < speeds.Length; i++)
            {
                sim.Step(1f / 30f, world);
                speeds[i] = sim.Speed;
            }
            float mean = speeds.Average();
            float sd = Mathf.Sqrt(speeds.Select(v => (v - mean) * (v - mean)).Average());
            TestContext.WriteLine($"скорость на ряби {mean:0.000} ± {sd:0.0000} м/с");
            Assert.That(sd, Is.LessThan(0.01f), "рябь не дёргает ход");
            Assert.That(mean, Is.LessThan(WalkParams.Default.BaseSpeed), "но по ней чуть медленнее");
        }

        [Test]
        public void Bumps_MakeYouCareful()
        {
            var flat = Walking(new FlatWorld());
            var bumpy = Walking(new Bumpy());
            flat.Run(30f, 1f / 30f);
            bumpy.Run(30f, 1f / 30f);
            TestContext.WriteLine($"осторожность: ровно {flat.Exertion.State.Caution:0.00}, бугры {bumpy.Exertion.State.Caution:0.00}");
            Assert.That(bumpy.Exertion.State.Caution, Is.GreaterThan(flat.Exertion.State.Caution + 0.2f));
            Assert.That(bumpy.Sim.Speed, Is.LessThan(flat.Sim.Speed));
        }

        [Test]
        public void Glass_StaysGlass()
        {
            var h = Walking(new FlatWorld());
            h.Run(20f, 1f / 30f);
            Assert.AreEqual(0f, h.Gait.State.SupportHeight, 1e-4f);
        }
    }
}
