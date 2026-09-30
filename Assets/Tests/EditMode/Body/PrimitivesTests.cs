using System;
using System.Linq;
using Hodba.Client.Body;
using NUnit.Framework;

namespace Hodba.Tests
{
    public class PrimitivesTests
    {
        [Test]
        public void Rng_SameSeedSameSequence_DifferentSaltDifferent()
        {
            var a = new Rng(42, 1);
            var b = new Rng(42, 1);
            var c = new Rng(42, 2);
            bool differs = false;
            for (int i = 0; i < 100; i++)
            {
                float x = a.Next01();
                Assert.AreEqual(x, b.Next01());
                differs |= x != c.Next01();
            }
            Assert.IsTrue(differs);
        }

        [Test]
        public void LogNormal_HasRequestedMedian_AndHeavyTail()
        {
            var rng = new Rng(3, 9);
            var v = Enumerable.Range(0, 20000).Select(_ => rng.LogNormal(3.5f, 0.6f)).OrderBy(x => x).ToArray();
            Assert.That(v[v.Length / 2], Is.EqualTo(3.5f).Within(0.15f));
            float mean = v.Average();
            Assert.That(mean, Is.GreaterThan(v[v.Length / 2]), "хвост вправо: среднее больше медианы");
            Assert.That(v[v.Length - 1], Is.GreaterThan(3.5f * 4f), "изредка очень долго");
        }

        [Test]
        public void PinkNoise_IsDeterministic_BoundedAndSmooth()
        {
            var a = new PinkNoise(5, 1, 0.01f, 6);
            var b = new PinkNoise(5, 1, 0.01f, 6);
            double sumSq = 0;
            float prev = a.Sample(0);
            float maxStep = 0f;
            for (int i = 0; i < 20000; i++)
            {
                double t = i * 0.05;
                float x = a.Sample(t);
                Assert.AreEqual(x, b.Sample(t));
                sumSq += x * x;
                maxStep = Math.Max(maxStep, Math.Abs(x - prev));
                prev = x;
            }
            double rms = Math.Sqrt(sumSq / 20000);
            Assert.That(rms, Is.InRange(0.2, 0.7), "σ около 0.45");
            Assert.That(maxStep, Is.LessThan(0.2f), "без скачков");
        }

        /// <summary>1/f: у медленных октав столько же мощности, сколько у быстрых, — дисперсия растёт с окном.</summary>
        [Test]
        public void PinkNoise_HasLongMemory()
        {
            var n = new PinkNoise(11, 1, 0.001f, 10);
            // Среднее по окну 1 с почти не гасит шум, а белый шум оно бы погасило.
            double sumSq = 0;
            int windows = 2000;
            for (int w = 0; w < windows; w++)
            {
                double s = 0;
                for (int k = 0; k < 20; k++) s += n.Sample(w * 1.0 + k * 0.05);
                s /= 20;
                sumSq += s * s;
            }
            Assert.That(Math.Sqrt(sumSq / windows), Is.GreaterThan(0.15));
        }

        [Test]
        public void Spring_SameResultAt30And60Fps()
        {
            var a = new Spring();
            var b = new Spring();
            a.Kick(1f);
            b.Kick(1f);
            for (int i = 0; i < 30; i++) a.Step(0.2f, 1f / 30f, 3f, 0.5f);
            for (int i = 0; i < 60; i++) b.Step(0.2f, 1f / 60f, 3f, 0.5f);
            Assert.That(a.Value, Is.EqualTo(b.Value).Within(0.01f));
        }

        [Test]
        public void Spring_SurvivesLongFrame()
        {
            var s = new Spring();
            s.Step(1f, 5f, 4f, 0.6f);
            Assert.That(s.Value, Is.InRange(-2f, 3f), "не улетает");
            Assert.IsFalse(float.IsNaN(s.Value));
        }

        [Test]
        public void SoftLimit_KeepsSmall_SaturatesLarge()
        {
            Assert.AreEqual(0.1f, SoftLimit.Apply(0.1f, 1f));
            Assert.That(SoftLimit.Apply(100f, 1f), Is.LessThanOrEqualTo(1f));
            Assert.That(SoftLimit.Apply(-100f, 1f), Is.GreaterThanOrEqualTo(-1f));
            Assert.That(SoftLimit.Apply(0.9f, 1f), Is.LessThan(0.9f).And.GreaterThan(0.6f));
        }

        [Test]
        public void EventClock_FiresOnceEvenInLongFrame()
        {
            var c = new EventClock();
            c.Schedule(0.1f);
            Assert.IsTrue(c.Tick(10f));
            Assert.IsFalse(c.Tick(10f), "без перезавода больше не срабатывает");
        }
    }
}
