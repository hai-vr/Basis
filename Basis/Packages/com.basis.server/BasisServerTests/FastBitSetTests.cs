using System;
using System.Threading.Tasks;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;

namespace BasisServerTests
{
    public class FastBitSetTests
    {
        [Fact]
        public void Constructor_InitializesWithCorrectLength()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);
            Assert.Equal(64, bs.Length);
        }

        [Fact]
        public void Constructor_RoundsUpToWordBoundary()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(33);
            Assert.True(bs.Length >= 33);
            Assert.Equal(0, bs.Length % 32);
        }

        [Fact]
        public void Constructor_NegativeThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BasisServerReductionSystemEvents.FastBitSet(-1));
        }

        [Fact]
        public void SetAndGet_Basic()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);

            bs.Set(0, true);
            bs.Set(31, true);
            bs.Set(32, true);
            bs.Set(63, true);

            Assert.True(bs.Get(0));
            Assert.True(bs.Get(31));
            Assert.True(bs.Get(32));
            Assert.True(bs.Get(63));
            Assert.False(bs.Get(1));
            Assert.False(bs.Get(30));
        }

        [Fact]
        public void Set_ClearBit()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(32);
            bs.Set(5, true);
            Assert.True(bs.Get(5));

            bs.Set(5, false);
            Assert.False(bs.Get(5));
        }

        [Fact]
        public void Get_NegativeIndex_ReturnsFalse()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(32);
            Assert.False(bs.Get(-1));
        }

        [Fact]
        public void Get_OutOfRange_ReturnsFalse()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(32);
            Assert.False(bs.Get(100));
        }

        [Fact]
        public void TestAndClear_SetBit_ReturnsTrueAndClears()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(32);
            bs.Set(10, true);

            Assert.True(bs.TestAndClear(10));
            Assert.False(bs.Get(10));
        }

        [Fact]
        public void TestAndClear_ClearedBit_ReturnsFalse()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(32);
            Assert.False(bs.TestAndClear(10));
        }

        [Fact]
        public void SetAll_True_SetsAllBits()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);
            bs.SetAll(true);

            for (int i = 0; i < 64; i++)
                Assert.True(bs.Get(i), $"Bit {i} should be set");
        }

        [Fact]
        public void SetAll_False_ClearsAllBits()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);
            bs.SetAll(true);
            bs.SetAll(false);

            for (int i = 0; i < 64; i++)
                Assert.False(bs.Get(i), $"Bit {i} should be clear");
        }

        [Fact]
        public void Clear_ClearsAllBits()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);
            bs.Set(0, true);
            bs.Set(32, true);
            bs.Set(63, true);

            bs.Clear();

            Assert.False(bs.Get(0));
            Assert.False(bs.Get(32));
            Assert.False(bs.Get(63));
        }

        [Fact]
        public void AnyTrue_WhenNoneSet_ReturnsFalse()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);
            Assert.False(bs.AnyTrue());
        }

        [Fact]
        public void AnyTrue_WhenOneSet_ReturnsTrue()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);
            bs.Set(42, true);
            Assert.True(bs.AnyTrue());
        }

        [Fact]
        public void Set_BeyondInitialCapacity_GrowsAutomatically()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(32);
            bs.Set(100, true);

            Assert.True(bs.Length > 100);
            Assert.True(bs.Get(100));
        }

        [Fact]
        public void ThreadSafety_ConcurrentSetAndGet()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(1024);
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            Parallel.For(0, 1000, i =>
            {
                try
                {
                    int index = i % 1024;
                    bs.Set(index, true);
                    bool _ = bs.Get(index);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }

        [Fact]
        public void ThreadSafety_ConcurrentTestAndClear()
        {
            var bs = new BasisServerReductionSystemEvents.FastBitSet(64);

            // Set a single bit
            bs.Set(10, true);

            // Race multiple threads to TestAndClear - only one should get true
            int trueCount = 0;
            Parallel.For(0, 100, _ =>
            {
                if (bs.TestAndClear(10))
                    System.Threading.Interlocked.Increment(ref trueCount);
            });

            Assert.Equal(1, trueCount);
        }
    }
}
