using Hodba.Client.Body;
using NUnit.Framework;

namespace Hodba.Tests
{
    public class PeripheryTests
    {
        static ExertionState Load(float load) => new ExertionState(load, load, 0f, 0.3f, 1f, 0f, 0.5f);

        [Test]
        public void Calm_IsBarelyThere_OnlyAtTheEdges()
        {
            var s = PeripherySettings.Default;
            var p = Periphery.From(Load(0f), s);
            Assert.That(p.Start, Is.GreaterThanOrEqualTo(0.5f), "центр чистый");
            Assert.That(p.Blur, Is.LessThanOrEqualTo(0.4f));
            Assert.AreEqual(0f, p.Darken, "в покое края не темнеют");
        }

        [Test]
        public void Breathlessness_NarrowsTheTunnel()
        {
            var s = PeripherySettings.Default;
            var calm = Periphery.From(Load(0.3f), s);
            var hard = Periphery.From(Load(1f), s);
            Assert.That(hard.Start, Is.LessThan(calm.Start));
            Assert.That(hard.Blur, Is.GreaterThan(calm.Blur));
            Assert.That(hard.Darken, Is.GreaterThan(0.2f));
            Assert.AreEqual(calm.Start, Periphery.From(Load(s.tunnelFrom), s).Start, 1e-4f, "туннель начинается не сразу");
        }
    }
}
