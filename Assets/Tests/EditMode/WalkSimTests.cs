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

        sealed class GentleSlope : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(0, 0, "gentle");
            public long SampleHeightMm(long xMm, long zMm) => zMm / 10; // 10% в гору на север
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.FineAsh, 0);
        }

        sealed class Loose : IWorldQuery
        {
            public WorldInfo Info => new WorldInfo(0, 0, "loose");
            public long SampleHeightMm(long xMm, long zMm) => 0;
            public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.FineAsh, 65535);
        }

        /// <summary>Без дрейфа своего темпа: там, где проверяется точная скорость.</summary>
        static WalkParams Steady
        {
            get
            {
                var p = WalkParams.Default;
                p.Pace.DriftAmount = 0f;
                return p;
            }
        }

        static WalkSim Sim(float course = 0f) => new WalkSim(Steady, new WorldPos(0, 0), course);

        static WalkSim Walking(float course = 0f)
        {
            var sim = Sim(course);
            sim.Apply(Intent.Walk());
            return sim;
        }

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

        /// <summary>Под гору ноги сами несут: спуск не тяжелее подъёма, пологий — почти не тормозит.</summary>
        [Test]
        public void DownhillIsEasierThanUphill()
        {
            var down = Sim(180f); // склон в 20% поднимается на север — идём на юг, вниз
            down.Apply(Intent.Walk());
            Run(down, new Slope(), 5f);
            var up = Sim(0f);
            up.Apply(Intent.Walk());
            Run(up, new Slope(), 5f);
            Assert.That(down.Slope, Is.LessThan(-0.15f), "правда спуск");
            Assert.That(down.Speed, Is.GreaterThan(up.Speed * 1.5f));
            Assert.That(down.Speed, Is.GreaterThan(WalkParams.Default.BaseSpeed * 0.85f));
            var p = WalkParams.Default;
            Assert.That(WalkSim.SlopeFactor(-0.6f, p.SteepDescent, p.DownhillEase), Is.InRange(0.35f, 0.7f),
                "крутой подветренный склон бархана всё же тормозит");
        }

        /// <summary>Небольшой спуск не тормозит вовсе: ход быстрее ровного и не падает, пока спуск не станет крутым.</summary>
        [Test]
        public void GentleDescent_NeverSlows_OnlySteepDoes()
        {
            var p = WalkParams.Default;
            float prev = WalkSim.SlopeFactor(0f, p.SteepDescent, p.DownhillEase);
            Assert.AreEqual(1f, prev, 1e-5f);
            for (float s = -0.01f; s >= -p.SteepDescent - 1e-4f; s -= 0.01f)
            {
                float f = WalkSim.SlopeFactor(s, p.SteepDescent, p.DownhillEase);
                Assert.That(f, Is.GreaterThanOrEqualTo(prev - 1e-5f), $"на спуске {s:0.00} не медленнее, чем положе");
                Assert.That(f, Is.GreaterThan(1f), $"на спуске {s:0.00} быстрее ровного");
                prev = f;
            }
            Assert.That(WalkSim.SlopeFactor(-p.SteepDescent - 0.1f, p.SteepDescent, p.DownhillEase), Is.LessThan(prev),
                "круче — придерживает");
        }

        /// <summary>Скорость, которую дал склон, даром: под гору быстрее, а запас не тратится.</summary>
        [Test]
        public void GentleDescent_FasterButFree()
        {
            var down = Walking(180f);
            Run(down, new GentleSlope(), 120f);
            Assert.That(down.Slope, Is.LessThan(-0.05f));
            Assert.That(down.Speed, Is.GreaterThan(WalkParams.Default.BaseSpeed * 1.1f));
            Assert.That(down.Effort, Is.LessThan(WalkParams.Default.Pace.Sustainable));
            Assert.AreEqual(0f, down.Fatigue, 1e-4f);
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

        [Test]
        public void Hurry_WalksFaster_UntilBreathRunsOut()
        {
            var sim = Walking();
            Run(sim, new Flat(), 5f);
            Assert.AreEqual(0f, sim.Fatigue, 1e-4f, "обычный шаг по ровному запас не тратит");

            sim.Apply(Intent.Hurry(1f));
            Run(sim, new Flat(), 30f);
            Assert.That(sim.Speed, Is.GreaterThan(WalkParams.Default.BaseSpeed * 1.3f), "спешка разгоняет");
            Assert.That(sim.Fatigue, Is.GreaterThan(0f).And.LessThan(WalkParams.Default.Pace.HurryFadeFrom),
                "тратит запас, но сил ещё полно");

            Run(sim, new Flat(), 90f);
            Assert.That(sim.Speed, Is.GreaterThan(WalkParams.Default.BaseSpeed * 1.3f), "быстрый шаг держится минутами");

            Run(sim, new Flat(), 480f);
            Assert.That(sim.Fatigue, Is.GreaterThan(WalkParams.Default.Pace.HurryFadeFrom));
            Assert.That(sim.Speed, Is.LessThan(WalkParams.Default.BaseSpeed * 1.2f), "запас кончился: бесконечно не разгонишься");
        }

        [Test]
        public void AfterHurry_SlowerThanUsual_ThenRecovers()
        {
            var sim = Walking();
            Run(sim, new Flat(), 3f);
            sim.Apply(Intent.Hurry(1f));
            Run(sim, new Flat(), 600f);
            sim.Apply(Intent.Hurry(0f));
            Run(sim, new Flat(), 4f);
            Assert.That(sim.Speed, Is.LessThan(WalkParams.Default.BaseSpeed * 0.95f), "после спешки плетётся");

            Run(sim, new Flat(), 900f);
            Assert.AreEqual(0f, sim.Fatigue, 0.01f, "запас вернулся");
            Assert.That(sim.Speed, Is.EqualTo(WalkParams.Default.BaseSpeed).Within(0.01f));
        }

        [Test]
        public void Uphill_SpendsReserve_DownhillDoesNot()
        {
            var up = Walking(0f);
            Run(up, new Slope(), 360f);
            var down = Walking(180f);
            Run(down, new Slope(), 360f);
            Assert.That(up.Fatigue, Is.GreaterThan(0.3f), "подъём выматывает");
            Assert.AreEqual(0f, down.Fatigue, 1e-4f, "под гору запас не тратится");
        }

        [Test]
        public void Rest_RecoversFasterThanWalking()
        {
            var resting = Walking();
            var walking = Walking();
            foreach (var sim in new[] { resting, walking })
            {
                sim.Apply(Intent.Hurry(1f));
                Run(sim, new Flat(), 180f);
            }
            Assert.AreEqual(resting.Fatigue, walking.Fatigue, 1e-5f);
            float tired = resting.Fatigue;

            resting.Apply(Intent.Stop());
            walking.Apply(Intent.Hurry(0f));
            Run(resting, new Flat(), 60f);
            Run(walking, new Flat(), 60f);
            Assert.That(walking.Fatigue, Is.LessThan(tired), "на ровном тоже отпускает");
            Assert.That(resting.Fatigue, Is.LessThan(walking.Fatigue), "стоя — быстрее");
        }

        [Test]
        public void Stop_And_Teleport_ClearHurry()
        {
            var sim = Walking();
            sim.Apply(Intent.Hurry(1f));
            Run(sim, new Flat(), 10f);
            sim.Apply(Intent.Stop());
            Assert.AreEqual(0f, sim.Hurry);

            sim.Apply(Intent.Walk());
            sim.Apply(Intent.Hurry(1f));
            Run(sim, new Flat(), 10f);
            sim.Teleport(new WorldPos(0, 0), 0f, true);
            Assert.AreEqual(0f, sim.Hurry);
            Assert.AreEqual(0f, sim.Haste);
            Assert.AreEqual(0f, sim.Fatigue, "вернулся из фона отдохнувшим");
        }

        [Test]
        public void HurryIgnored_WhileStanding()
        {
            var sim = Sim();
            sim.Apply(Intent.Hurry(1f));
            Run(sim, new Flat(), 3f);
            Assert.AreEqual(0f, sim.Speed);
            Assert.AreEqual(0f, sim.Haste);
        }

        /// <summary>Свой темп плавает по пути, но в узких пределах и одинаково для одного мира.</summary>
        [Test]
        public void OwnPace_DriftsWithinBounds_AndIsRepeatable()
        {
            var a = new WalkSim(WalkParams.Default, new WorldPos(0, 0), 0f);
            var b = new WalkSim(WalkParams.Default, new WorldPos(0, 0), 0f);
            a.Apply(Intent.Walk());
            b.Apply(Intent.Walk());
            Run(a, new Flat(), 5f);
            Run(b, new Flat(), 5f);

            float min = float.MaxValue, max = float.MinValue, amount = WalkParams.Default.Pace.DriftAmount;
            for (int i = 0; i < 600; i++)
            {
                Run(a, new Flat(), 1f);
                Run(b, new Flat(), 1f);
                min = System.Math.Min(min, a.Speed);
                max = System.Math.Max(max, a.Speed);
            }
            float baseSpeed = WalkParams.Default.BaseSpeed;
            Assert.That(min, Is.GreaterThanOrEqualTo(baseSpeed * (1f - amount) - 1e-3f));
            Assert.That(max, Is.LessThanOrEqualTo(baseSpeed * (1f + amount) + 1e-3f));
            Assert.That(max - min, Is.GreaterThan(baseSpeed * amount * 0.3f), "правда плавает");
            Assert.AreEqual(a.Position, b.Position, "детерминирован: сервер переиграет так же");
            Assert.AreEqual(0f, a.Fatigue, 1e-4f, "и не утомляет");
        }
    }
}
