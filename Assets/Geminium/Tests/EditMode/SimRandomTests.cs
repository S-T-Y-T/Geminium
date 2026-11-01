using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Geminium.Core;

namespace Geminium.Tests.EditMode
{
    public class SimRandomTests
    {
        [Test]
        public void KnownSeed_GivesReferenceValues()
        {
            // Эталон посчитан отдельной реализацией xoshiro256**. Если тест упал,
            // алгоритм изменился и старые прогоны перестанут воспроизводиться.
            var random = new SimRandom(12345);
            Assert.AreEqual(13720838825685603483UL, random.NextULong());
            Assert.AreEqual(2398916695208396998UL, random.NextULong());
            Assert.AreEqual(17770384849984869256UL, random.NextULong());
            Assert.AreEqual(2505731525339251501UL, new SimRandom(12345).Fork("market").NextULong());
        }

        [Test]
        public void SameSeed_GivesSameSequence()
        {
            var a = new SimRandom(42);
            var b = new SimRandom(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void DifferentSeeds_GiveDifferentSequences()
        {
            var a = new SimRandom(1);
            var b = new SimRandom(2);
            Assert.IsFalse(Enumerable.Range(0, 10).All(_ => a.NextULong() == b.NextULong()));
        }

        [Test]
        public void Fork_DependsOnlyOnSeedAndName()
        {
            var fresh = new SimRandom(7);
            var used = new SimRandom(7);
            for (int i = 0; i < 100; i++) used.NextULong();

            Assert.AreEqual(fresh.Fork("market").NextULong(), used.Fork("market").NextULong());
            Assert.AreNotEqual(fresh.Fork("market").NextULong(), fresh.Fork("supply").NextULong());
        }

        [Test]
        public void NextDouble_StaysInUnitInterval()
        {
            var random = new SimRandom(3);
            for (int i = 0; i < 100000; i++)
            {
                double value = random.NextDouble();
                Assert.That(value, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
            }
        }

        [Test]
        public void NextInt_StaysInBoundsAndHitsEveryValue()
        {
            var random = new SimRandom(4);
            var seen = new HashSet<int>();
            for (int i = 0; i < 10000; i++)
            {
                int value = random.NextInt(3, 7);
                Assert.That(value, Is.InRange(3, 6));
                seen.Add(value);
            }
            CollectionAssert.AreEquivalent(new[] { 3, 4, 5, 6 }, seen);
        }

        [Test]
        public void NextGaussian_HasZeroMeanAndUnitDeviation()
        {
            var random = new SimRandom(5);
            const int n = 200000;
            double sum = 0, sumSquares = 0;
            for (int i = 0; i < n; i++)
            {
                double x = random.NextGaussian();
                sum += x;
                sumSquares += x * x;
            }
            double mean = sum / n;
            double deviation = System.Math.Sqrt(sumSquares / n - mean * mean);
            Assert.That(mean, Is.EqualTo(0.0).Within(0.02));
            Assert.That(deviation, Is.EqualTo(1.0).Within(0.02));
        }
    }
}
