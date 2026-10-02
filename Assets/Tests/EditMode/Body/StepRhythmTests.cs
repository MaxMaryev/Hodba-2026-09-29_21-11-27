using System.Linq;
using Hodba.Client.Body;
using NUnit.Framework;

namespace Hodba.Tests
{
    public class StepRhythmTests
    {
        const float Freq = 1.4f;
        static readonly RhythmSettings S = RhythmSettings.Default;

        static GaitState State(bool stanceLeft, float phase) =>
            new GaitState(1f, phase, stanceLeft, Freq, 0f, 0f, 0f, 0f, 0f, 0f);

        /// <summary>Шаг за шагом: нога встала, потом к концу шага игрок тапает ту, что встанет следующей.</summary>
        static void TapAlong(StepRhythm r, int steps, ref bool stanceLeft)
        {
            for (int i = 0; i < steps; i++)
            {
                stanceLeft = !stanceLeft;
                r.Tick(0.05f, State(stanceLeft, 0.05f), true, S);
                r.Tick(0.3f, State(stanceLeft, 0.6f), true, S);
                Assert.AreEqual(TapResult.OnBeat, r.Tap(!stanceLeft, State(stanceLeft, 0.6f), true, S));
            }
        }

        [Test]
        public void TapsOnBeat_BuildHurry()
        {
            var r = new StepRhythm();
            bool stance = false;
            TapAlong(r, 1, ref stance);
            float one = r.Drive;
            Assert.That(one, Is.GreaterThan(0f).And.LessThan(1f), "один тап — ещё не спешка в полную силу");
            TapAlong(r, 3, ref stance);
            Assert.AreEqual(1f, r.Drive, 1e-5f);
        }

        [Test]
        public void JustLandedFoot_CountsOnce()
        {
            var r = new StepRhythm();
            r.Tick(0.05f, State(true, 0.05f), true, S);
            Assert.AreEqual(TapResult.OnBeat, r.Tap(true, State(true, 0.05f), true, S), "левая только что встала");
            Assert.AreEqual(TapResult.Miss, r.Tap(true, State(true, 0.08f), true, S), "второй тап на тот же шаг");
        }

        [Test]
        public void TapBeforeLanding_ThenLanding_IsTheSameStep()
        {
            var r = new StepRhythm();
            r.Tick(0.05f, State(true, 0.7f), true, S);
            Assert.AreEqual(TapResult.OnBeat, r.Tap(false, State(true, 0.7f), true, S), "правая вот-вот встанет");
            r.Tick(0.05f, State(false, 0.05f), true, S);
            Assert.AreEqual(TapResult.Miss, r.Tap(false, State(false, 0.05f), true, S), "это тот же шаг правой");
        }

        /// <summary>Какая нога сейчас, игрок не видит: первый тап любой рукой задаёт пару «рука — нога».</summary>
        [Test]
        public void FirstTap_AnyHand_ThenHandsMustAlternate()
        {
            var r = new StepRhythm();
            r.Tick(0.05f, State(true, 0.6f), true, S); // стоит на левой, вот-вот встанет правая
            Assert.AreEqual(TapResult.OnBeat, r.Tap(true, State(true, 0.6f), true, S), "левая рука ведёт правую ногу");

            r.Tick(0.05f, State(false, 0.6f), true, S); // встала правая, следующей встанет левая
            Assert.AreEqual(TapResult.Miss, r.Tap(true, State(false, 0.6f), true, S), "та же рука дважды подряд");
            Assert.AreEqual(TapResult.OnBeat, r.Tap(false, State(false, 0.6f), true, S), "правая рука — левая нога");
        }

        [Test]
        public void AfterABreak_HandsAreMatchedAgain()
        {
            var r = new StepRhythm();
            bool stance = false;
            TapAlong(r, 2, ref stance); // рука = нога
            for (int i = 0; i < 90; i++) r.Tick(1f / 30f, State(stance, 0.3f), true, S); // серия оборвалась
            Assert.AreEqual(0f, r.Drive, 1e-5f);

            stance = !stance;
            r.Tick(0.05f, State(stance, 0.6f), true, S);
            Assert.AreEqual(TapResult.OnBeat, r.Tap(stance, State(stance, 0.6f), true, S), "новая серия — любая рука");
        }

        [Test]
        public void WrongFoot_OrMidStep_IsAMiss()
        {
            var r = new StepRhythm();
            bool stance = false;
            TapAlong(r, 3, ref stance);
            float before = r.Drive;
            Assert.AreEqual(TapResult.Miss, r.Tap(stance, State(stance, 0.6f), true, S), "не та рука посреди серии");
            Assert.AreEqual(TapResult.Miss, r.Tap(!stance, State(stance, 0.3f), true, S), "слишком рано");
            Assert.That(r.Drive, Is.LessThan(before * S.missKeep + 1e-5f), "сбился — спешка тает");
        }

        [Test]
        public void WithoutTaps_HurryFadesInAFewSteps()
        {
            var r = new StepRhythm();
            bool stance = false;
            TapAlong(r, 3, ref stance);
            for (int i = 0; i < 30; i++) r.Tick(1f / 30f, State(stance, 0.6f), true, S); // ~1.4 шага
            Assert.AreEqual(1f, r.Drive, 1e-5f, "пару шагов спешка держится");
            for (int i = 0; i < 60; i++) r.Tick(1f / 30f, State(stance, 0.6f), true, S);
            Assert.AreEqual(0f, r.Drive, 1e-5f, "перестал подгонять — вернулся к своему темпу");
        }

        [Test]
        public void Standing_TapsIgnored()
        {
            var r = new StepRhythm();
            r.Tick(0.1f, State(true, 0.6f), false, S);
            Assert.AreEqual(TapResult.Ignored, r.Tap(false, State(true, 0.6f), false, S));
            Assert.AreEqual(0f, r.Drive);
        }

        [Test]
        public void Falter_IsAMisstep_NotAFall()
        {
            var h = new BodyHarness(new FlatWorld());
            h.Sim.Apply(Hodba.Sim.Walk.Intent.Walk());
            h.Run(3f, 1f / 30f);
            h.Gait.Falter();
            h.Run(2f, 1f / 30f);
            Assert.IsTrue(h.BodyEvents.Any(e => e.Kind == BodyEventKind.Misstep));
            Assert.IsFalse(h.BodyEvents.Any(e => e.Kind == BodyEventKind.Stumble), "спотыкаются только о камень");
            Assert.IsTrue(h.Steps.Any(s => s.Scuff), "нога шаркнула");
        }
    }
}
