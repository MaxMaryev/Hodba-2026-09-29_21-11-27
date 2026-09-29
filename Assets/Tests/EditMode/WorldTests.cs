using Hodba.Core;
using Hodba.World.Gen;
using NUnit.Framework;

namespace Hodba.Tests
{
    public class WorldTests
    {
        [Test]
        public void Noise_SameSeed_SameValue()
        {
            for (long i = -50; i < 50; i++)
            {
                long x = i * 123_457, z = i * -98_765;
                Assert.AreEqual(ValueNoise.Sample(x, z, 10_000, 7), ValueNoise.Sample(x, z, 10_000, 7));
            }
        }

        [Test]
        public void Noise_StaysInRange()
        {
            for (long i = -500; i < 500; i++)
            {
                int v = ValueNoise.Sample(i * 3_331, i * 7_919, 50_000, 3);
                Assert.That(v, Is.InRange(0, ValueNoise.One));
                int f = ValueNoise.Fbm(i * 3_331, i * 7_919, 50_000, 4, 3);
                Assert.That(f, Is.InRange(-ValueNoise.One, ValueNoise.One));
            }
        }

        [Test]
        public void Noise_IsContinuousAcrossCells()
        {
            // На границе клетки нет скачка.
            int a = ValueNoise.Sample(99_999, 0, 100_000, 1);
            int b = ValueNoise.Sample(100_000, 0, 100_000, 1);
            Assert.That(System.Math.Abs(a - b), Is.LessThan(200));
        }

        [Test]
        public void FlatStub_HeightIsGentleUnderfoot()
        {
            var world = new FlatStub(1);
            long h0 = world.SampleHeightMm(0, 0);
            long h1 = world.SampleHeightMm(1_000, 0);
            Assert.That(System.Math.Abs(h1 - h0), Is.LessThan(150), "уклон под ногами меньше 15%");
        }

        [Test]
        public void WorldPos_FloorMathHandlesNegatives()
        {
            Assert.AreEqual(-1, WorldPos.FloorDiv(-1, 1000));
            Assert.AreEqual(999, WorldPos.FloorMod(-1, 1000));
            Assert.AreEqual(new WorldPos(-512_000, 0), new WorldPos(-1, 5).Snap(512_000));
        }
    }
}
