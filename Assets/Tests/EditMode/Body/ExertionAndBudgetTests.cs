using Hodba.Client.Body;
using Hodba.Sim.Walk;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class ExertionAndBudgetTests
    {
        [Test]
        public void AfterClimb_BreathRecoversGradually()
        {
            var h = new BodyHarness(new HillWorld());
            h.Sim.Apply(Intent.Walk());
            h.Run(90f, 1f / 30f);
            float peak = h.Exertion.State.Load;
            float peakRate = h.Exertion.State.BreathRate;
            Assert.That(peak, Is.GreaterThan(0.5f), "подъём даёт одышку");

            h.Sim.Apply(Intent.Stop());
            h.Run(8f, 1f / 30f);
            Assert.That(h.Exertion.State.Load, Is.GreaterThan(peak * 0.6f), "не выключается сразу");
            Assert.That(h.Exertion.State.BreathRate, Is.GreaterThan(ExertionSettings.Default.breathRest * 1.2f));

            h.Run(180f, 1f / 30f);
            Assert.That(h.Exertion.State.Load, Is.LessThan(peak * 0.1f), "но отпускает");
            Assert.That(h.Exertion.State.BreathRate, Is.LessThan(peakRate));
        }

        /// <summary>Быстрый шаг: дыхание тяжелеет за полминуты, а запас сил уходит минутами.</summary>
        [Test]
        public void Hurry_BreathHeavyFast_ReserveDrainsSlowly()
        {
            var h = new BodyHarness(new FlatWorld());
            h.Sim.Apply(Intent.Walk());
            h.Run(10f, 1f / 30f);
            float calm = h.Exertion.State.Load;

            h.Sim.Apply(Intent.Hurry(1f));
            h.Run(30f, 1f / 30f);
            Assert.That(h.Exertion.State.Load, Is.GreaterThan(calm + 0.25f), "дыхание тяжелеет сразу");
            Assert.That(h.Sim.Fatigue, Is.LessThan(0.15f), "силы почти не тронуты");
        }

        /// <summary>После спешки дыхание тяжёлое, пока не вернулся запас сил, а не только пока идёт рывок.</summary>
        [Test]
        public void AfterHurry_BreathStaysHeavyWhileFatigued()
        {
            var h = new BodyHarness(new FlatWorld());
            h.Sim.Apply(Intent.Walk());
            h.Run(10f, 1f / 30f);
            float calm = h.Exertion.State.Load;

            h.Sim.Apply(Intent.Hurry(1f));
            h.Run(600f, 1f / 30f);
            h.Sim.Apply(Intent.Hurry(0f));
            h.Run(15f, 1f / 30f);

            Assert.That(h.Sim.Effort, Is.LessThan(h.Sim.Params.Pace.Sustainable), "уже идёт спокойно");
            Assert.That(h.Sim.Fatigue, Is.GreaterThan(0.4f), "но запас ещё не вернулся");
            Assert.That(h.Exertion.State.Load, Is.GreaterThan(calm + 0.15f), "и одышка держится");
        }

        [Test]
        public void Caution_LingersAfterLooseGround()
        {
            var h = new BodyHarness(new FlatWorld(60_000));
            h.GaitSettings.surfaces = new[]
            {
                new SurfaceFeel { kind = Hodba.World.SurfaceKind.PackedAsh, caution = 0.6f, impact = 1f, bounce = 1f, stride = 1f },
            };
            h.Sim.Apply(Intent.Walk());
            h.Run(30f, 1f / 30f);
            float high = h.Exertion.State.Caution;
            Assert.That(high, Is.GreaterThan(0.4f));

            h.GaitSettings.surfaces = new[]
            {
                new SurfaceFeel { kind = Hodba.World.SurfaceKind.PackedAsh, caution = 0f, impact = 1f, bounce = 1f, stride = 1f },
            };
            h.Run(10f, 1f / 30f);
            Assert.That(h.Exertion.State.Caution, Is.GreaterThan(high * 0.6f), "осторожность держится после трудного места");
        }

        [Test]
        public void Breath_MovesTheHead_StandingAndWalking()
        {
            var h = new BodyHarness(new FlatWorld());
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 30 * 10; i++)
            {
                h.Tick(1f / 30f);
                min = Mathf.Min(min, h.Breath.Pose.Up);
                max = Mathf.Max(max, h.Breath.Pose.Up);
            }
            Assert.That((max - min) * 1000f, Is.GreaterThan(1f), "стоя грудь поднимает глаза, мм");
        }

        sealed class Loud : IMotionLayer
        {
            public MotionRole Role { get; set; }
            public PoseDelta Pose { get; set; }
            public float EventStrength { get; set; }
        }

        [Test]
        public void Budget_SoftlyCapsCoincidingPeaks()
        {
            var s = PoseSettings.Default;
            var budget = new MotionBudget();
            var big = new PoseDelta(1f, 1f, 1f, 90f, 90f, 90f);
            budget.Add(new Loud { Role = MotionRole.Rhythm, Pose = big });
            budget.Add(new Loud { Role = MotionRole.Rhythm, Pose = big });
            PoseDelta p = default;
            for (int i = 0; i < 300; i++) p = budget.Limit(budget.Mix(1f / 30f, s), 1f / 30f, s);
            Assert.That(p.Up, Is.LessThanOrEqualTo(s.offsetLimit.x + 1e-4f));
            Assert.That(p.Pitch, Is.LessThanOrEqualTo(s.angleLimit.x + 1e-4f));
            Assert.That(p.Roll, Is.LessThanOrEqualTo(s.angleLimit.z + 1e-4f));
        }

        [Test]
        public void Budget_DucksBackground_DuringEvent()
        {
            var s = PoseSettings.Default;
            var budget = new MotionBudget();
            var drift = new Loud { Role = MotionRole.Background, Pose = new PoseDelta(0.01f, 0f, 0f, 0f, 0f, 0f) };
            var ev = new Loud { Role = MotionRole.Rhythm };
            budget.Add(drift);
            budget.Add(ev);
            float calm = budget.Mix(1f / 30f, s).Up;
            ev.EventStrength = 1f;
            float during = 0f;
            for (int i = 0; i < 10; i++) during = budget.Mix(1f / 30f, s).Up;
            Assert.That(during, Is.LessThan(calm * 0.5f));
        }

        [Test]
        public void Budget_RateLimit_SmoothsSteps()
        {
            var s = PoseSettings.Default;
            var budget = new MotionBudget();
            var layer = new Loud { Role = MotionRole.Rhythm };
            budget.Add(layer);
            budget.Limit(budget.Mix(1f / 30f, s), 1f / 30f, s);
            layer.Pose = new PoseDelta(0.04f, 0f, 0f, 0f, 0f, 0f);
            var p = budget.Limit(budget.Mix(1f / 30f, s), 1f / 30f, s);
            Assert.That(p.Up, Is.LessThanOrEqualTo(s.offsetRate / 30f + 1e-5f), "скачок сглажен");
        }
    }
}
