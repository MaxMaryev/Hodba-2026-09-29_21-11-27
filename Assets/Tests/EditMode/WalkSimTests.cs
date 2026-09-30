using Hodba.Core;
using Hodba.Sim.Walk;
using Hodba.World;
using NUnit.Framework;

namespace Hodba.Tests
{
    public class WalkSimTests
    {
        sealed class Flat : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(0, 0, "test");
            public long SampleHeightMm(long xMm, long zMm) => 0;
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.FineAsh, 0);
        }

        sealed class Slope : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(0, 0, "slope");
            public long SampleHeightMm(long xMm, long zMm) => zMm / 5; // 20% в гору на север
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.FineAsh, 0);
        }

        sealed class Loose : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(0, 0, "loose");
            public long SampleHeightMm(long xMm, long zMm) => 0;
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.FineAsh, 65535);
        }

        static WalkSim Sim(float course = 0f) => new WalkSim(WalkParams.Default, new WorldPos(0, 0), course);

        static void Run(WalkSim sim, IWorldQuery world, float seconds, float dt = 1f / 30f)
        {
            for (float t = 0; t < seconds; t += dt) sim.Step(dt, world);
        }

        [Test]
        public void StandsStillUntilAskedToWalk()
        {
            var sim = Sim();
            Run(sim, new Flat(), 5f);
            Assert.AreEqual(new WorldPos(0, 0), sim.Position);
        }

        [Test]
        public void AcceleratesGradually_ThenWalksAtBaseSpeed()
        {
            var sim = Sim();
            sim.Apply(Intent.Walk());
            Run(sim, new Flat(), 0.3f);
            Assert.That(sim.Speed, Is.LessThan(WalkParams.Default.BaseSpeed * 0.5f), "не срывается с места");
            Run(sim, new Flat(), 3f);
            Assert.That(sim.Speed, Is.EqualTo(WalkParams.Default.BaseSpeed).Within(0.01f));
        }

        [Test]
        public void WalksNorthAtCourseZero()
        {
            var sim = Sim(0f);
            sim.Apply(Intent.Walk());
            Run(sim, new Flat(), 10f);
            Assert.That(sim.Position.Z, Is.GreaterThan(7_000));
            Assert.That(System.Math.Abs(sim.Position.X), Is.LessThan(10));
        }

        [Test]
        public void StopsInAFewSteps_NotInstantly()
        {
            var sim = Sim();
            sim.Apply(Intent.Walk());
            Run(sim, new Flat(), 5f);
            sim.Apply(Intent.Stop());
            sim.Step(1f / 30f, new Flat());
            Assert.That(sim.Speed, Is.GreaterThan(0f));
            Run(sim, new Flat(), 2f);
            Assert.AreEqual(0f, sim.Speed);
        }

        [Test]
        public void TurnsNoFasterThanMaxRate()
        {
            var sim = Sim(0f);
            sim.Apply(Intent.Course(90f));
            Run(sim, new Flat(), 1f);
            Assert.That(sim.Course, Is.EqualTo(WalkParams.Default.MaxTurnRate).Within(1f));
            Run(sim, new Flat(), 10f);
            Assert.That(sim.Course, Is.EqualTo(90f).Within(0.01f));
        }

        [Test]
        public void TurnsTheShortWay()
        {
            var sim = Sim(350f);
            sim.Apply(Intent.Course(10f));
            Run(sim, new Flat(), 0.2f);
            Assert.That(sim.Course, Is.GreaterThan(350f), "через север, а не в обход");
        }

        [Test]
        public void UphillIsSlower()
        {
            var sim = Sim(0f);
            sim.Apply(Intent.Walk());
            Run(sim, new Slope(), 5f);
            Assert.That(sim.Speed, Is.LessThan(WalkParams.Default.BaseSpeed * 0.7f));
        }

        [Test]
        public void LooseAshIsSlower()
        {
            var sim = Sim(0f);
            sim.Apply(Intent.Walk());
            Run(sim, new Loose(), 5f);
            float expected = WalkParams.Default.BaseSpeed * (1f - WalkParams.Default.LoosenessDrag);
            Assert.That(sim.Speed, Is.EqualTo(expected).Within(0.01f));
            Assert.That(sim.Looseness, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void AttentionSlowsDown()
        {
            var sim = Sim();
            sim.Apply(Intent.Walk());
            sim.Apply(Intent.Attention(0.4f));
            Run(sim, new Flat(), 5f);
            Assert.That(sim.Speed, Is.EqualTo(WalkParams.Default.BaseSpeed * 0.4f).Within(0.01f));
        }

        [Test]
        public void CountsSteps()
        {
            var sim = Sim();
            sim.Apply(Intent.Walk());
            Run(sim, new Flat(), 60f);
            long expected = (long)(sim.Distance / WalkParams.Default.StepLength);
            Assert.AreEqual(expected, sim.Steps);
            Assert.That(sim.Steps, Is.GreaterThan(60));
        }
    }
}
