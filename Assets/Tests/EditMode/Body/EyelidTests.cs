using System.Collections.Generic;
using System.Linq;
using Hodba.Client.Body;
using Hodba.Core;
using Hodba.Sim.Walk;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class EyelidTests
    {
        static readonly WalkSim Sim = new WalkSim(WalkParams.Default, new WorldPos(0, 0), 0f);
        static readonly Vector3 North = Vector3.forward;

        static BodyContext Ctx(float dt, double t, Vector3 sun, float elevation, Vector3 wind, float strength, float focus = 0f) =>
            new BodyContext(dt, t, Sim, new FlatWorld(), wind, strength, 0.5f, sun, elevation, 0f, 0f, false, focus);

        /// <summary>Считает моргания и собирает паузы между ними.</summary>
        static List<double> Blinks(Eyelids lids, EyelidSettings s, float seconds, System.Func<double, BodyContext> ctx, out float deepest)
        {
            var starts = new List<double>();
            float prev = 0f;
            deepest = 0f;
            const float dt = 1f / 60f;
            for (double t = 0; t < seconds; t += dt)
            {
                lids.Tick(ctx(t), North, s);
                float b = lids.State.Blink;
                if (b > 0f && prev == 0f) starts.Add(t);
                prev = b;
                deepest = Mathf.Max(deepest, lids.State.Upper);
            }
            return starts;
        }

        [Test]
        public void Blinks_Irregularly_WithLongPausesSometimes()
        {
            var s = EyelidSettings.Default;
            var lids = new Eyelids(1);
            var starts = Blinks(lids, s, 30 * 60, t => Ctx(1f / 60f, t, Vector3.down, -30f, Vector3.zero, 0f), out float deepest);

            var gaps = starts.Zip(starts.Skip(1), (a, b) => b - a).OrderBy(g => g).ToArray();
            double median = gaps[gaps.Length / 2];
            TestContext.WriteLine($"морганий {starts.Count}, медиана паузы {median:0.00} с, самая долгая {gaps.Last():0.0} с, глубже всего {deepest:0.00}");
            Assert.That(median, Is.InRange(1.5, 6.0));
            Assert.That(gaps.Last(), Is.GreaterThan(median * 3), "изредка долго не моргает");
            Assert.That(gaps.Count(g => g < 0.6), Is.GreaterThan(0), "бывают двойные");
            Assert.That(deepest, Is.LessThan(0.97f), "обычное моргание — не полная тьма");
            Assert.That(deepest, Is.GreaterThan(0.8f));
        }

        [Test]
        public void FocusSlowsBlinking_WindInTheFaceSpeedsItUp()
        {
            var s = EyelidSettings.Default;
            int calm = Blinks(new Eyelids(3), s, 600, t => Ctx(1f / 60f, t, Vector3.down, -30f, Vector3.zero, 0f), out _).Count;
            int focused = Blinks(new Eyelids(3), s, 600, t => Ctx(1f / 60f, t, Vector3.down, -30f, Vector3.zero, 0f, 1f), out _).Count;

            var windy = new Eyelids(3);
            windy.Add(new WindIrritant());
            int wind = Blinks(windy, s, 600, t => Ctx(1f / 60f, t, Vector3.down, -30f, Vector3.back * 8f, 0.9f), out _).Count;

            TestContext.WriteLine($"за 10 минут: спокойно {calm}, всматриваясь {focused}, ветер в лицо {wind}");
            Assert.That(focused, Is.LessThan(calm * 0.75f));
            Assert.That(wind, Is.GreaterThan(calm * 1.5f));
        }

        [Test]
        public void Wind_SquintsOnlyWhenInTheFace()
        {
            var s = EyelidSettings.Default;
            var face = new Eyelids(5);
            face.Add(new WindIrritant());
            var back = new Eyelids(5);
            back.Add(new WindIrritant());
            for (double t = 0; t < 5; t += 1f / 30f)
            {
                face.Tick(Ctx(1f / 30f, t, Vector3.down, -30f, Vector3.back * 8f, 0.9f), North, s);
                back.Tick(Ctx(1f / 30f, t, Vector3.down, -30f, Vector3.forward * 8f, 0.9f), North, s);
            }
            Assert.That(face.State.Squint, Is.GreaterThan(0.4f));
            Assert.That(back.State.Squint, Is.LessThan(0.05f), "спиной к ветру глазам легче");
        }

        /// <summary>Щурятся на стимул, а видят воспринятое: прищур и ослепление сходятся, не качаясь.</summary>
        [Test]
        public void SunSquint_ConvergesWithoutOscillation()
        {
            var s = EyelidSettings.Default;
            s.squintFlutter = 0f;
            var lids = new Eyelids(7);
            var sun = new SunIrritant();
            lids.Add(sun);
            var adaptation = new GlareAdaptation();

            var sunDir = new Vector3(0f, 0.1f, 1f).normalized; // низкое солнце прямо по взгляду
            float prevSquint = 0f, prevGlare = 0f, prevSlope = 0f;
            int turns = 0;
            for (double t = 0; t < 20; t += 1f / 30f)
            {
                lids.Tick(Ctx(1f / 30f, t, sunDir, 6f, Vector3.zero, 0f), sunDir, s);
                float squint = lids.State.Squint;
                float glare = adaptation.Tick(sun.Stimulus, squint, s.squintGlareRelief, 1.2f, 5f, 1f / 30f);
                Assert.That(squint, Is.GreaterThanOrEqualTo(prevSquint - 1e-5f), $"прищур не отпускает сам по себе, t={t:0.00}");
                float slope = glare - prevGlare;
                if (Mathf.Abs(slope) > 1e-4f)
                {
                    if (prevSlope != 0f && Mathf.Sign(slope) != Mathf.Sign(prevSlope)) turns++;
                    prevSlope = slope;
                }
                prevSquint = squint;
                prevGlare = glare;
            }
            Assert.That(turns, Is.LessThanOrEqualTo(1), "ослепление не качается: самое большее один перелёт");
            TestContext.WriteLine($"стимул {sun.Stimulus:0.00}, прищур {prevSquint:0.00}, воспринятое {prevGlare:0.00}");
            Assert.That(prevSquint, Is.GreaterThan(0.8f));
            Assert.That(prevGlare, Is.LessThan(sun.Stimulus * 0.7f), "прищур помогает");
        }

        [Test]
        public void SunBehind_NoSquint_ButNoonWhitensAnyway()
        {
            var s = EyelidSettings.Default;
            s.squintFlutter = 0f;
            var dawn = new Eyelids(9);
            dawn.Add(new SunIrritant());
            var noon = new Eyelids(9);
            noon.Add(new SunIrritant());
            for (double t = 0; t < 10; t += 1f / 30f)
            {
                dawn.Tick(Ctx(1f / 30f, t, new Vector3(0f, 0.1f, -1f).normalized, 6f, Vector3.zero, 0f), North, s);
                noon.Tick(Ctx(1f / 30f, t, new Vector3(0.2f, 1f, -0.3f).normalized, 70f, Vector3.zero, 0f), North, s);
            }
            Assert.That(dawn.State.Squint, Is.LessThan(0.02f));
            Assert.That(noon.State.Squint, Is.InRange(s.middaySquint * 0.8f, s.middaySquint * 1.2f));
        }

        [Test]
        public void LongFrame_FinishesBlink_WithoutGettingStuck()
        {
            var s = EyelidSettings.Default;
            var lids = new Eyelids(11);
            lids.Blink();
            lids.Tick(Ctx(1f / 60f, 0, Vector3.down, -30f, Vector3.zero, 0f), North, s);
            Assert.That(lids.State.Blink, Is.GreaterThan(0f));
            lids.Tick(Ctx(1f, 1, Vector3.down, -30f, Vector3.zero, 0f), North, s);
            Assert.AreEqual(0f, lids.State.Blink, "за длинный кадр моргание закончилось");
            Assert.IsTrue(lids.State.FullyOpen);
        }

        [Test]
        public void Stumble_UsuallyMakesYouBlink()
        {
            var s = EyelidSettings.Default;
            int blinked = 0;
            for (uint seed = 1; seed <= 50; seed++)
            {
                var lids = new Eyelids(seed);
                lids.Tick(Ctx(1f / 60f, 0, Vector3.down, -30f, Vector3.zero, 0f), North, s);
                lids.OnBodyEvent(new BodyEvent(BodyEventKind.Stumble, 1f), s);
                lids.Tick(Ctx(1f / 60f, 1f / 60f, Vector3.down, -30f, Vector3.zero, 0f), North, s);
                if (lids.State.Blink > 0f) blinked++;
            }
            Assert.That(blinked, Is.GreaterThan(30));
        }
    }
}
