using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Basis.Network.Core.Compression;
using BasisNetworkCore.Pooling;
using Xunit;

namespace BasisServerTests
{
    public class BasisByteArrayPoolingTests
    {
        // Reset pool state before each test
        public BasisByteArrayPoolingTests()
        {
            BasisByteArrayPooling.Clear();
        }

        [Fact]
        public void Rent_ReturnsArrayOfCorrectSize()
        {
            byte[] arr = BasisByteArrayPooling.Rent(100);
            Assert.NotNull(arr);
            Assert.Equal(100, arr.Length);
        }

        [Fact]
        public void Rent_AfterReturn_ReusesSameArray()
        {
            byte[] arr1 = BasisByteArrayPooling.Rent(50);
            BasisByteArrayPooling.Return(arr1);
            byte[] arr2 = BasisByteArrayPooling.Rent(50);

            Assert.Same(arr1, arr2);
        }

        [Fact]
        public void Rent_DifferentSizes_ReturnsDifferentArrays()
        {
            byte[] arr10 = BasisByteArrayPooling.Rent(10);
            byte[] arr20 = BasisByteArrayPooling.Rent(20);

            Assert.Equal(10, arr10.Length);
            Assert.Equal(20, arr20.Length);
        }

        [Fact]
        public void Return_Null_DoesNotThrow()
        {
            BasisByteArrayPooling.Return(null); // should not throw
        }

        [Fact]
        public void Clear_EmptiesPool()
        {
            byte[] arr = BasisByteArrayPooling.Rent(10);
            BasisByteArrayPooling.Return(arr);
            BasisByteArrayPooling.Clear();

            byte[] arr2 = BasisByteArrayPooling.Rent(10);
            Assert.NotSame(arr, arr2); // pool was cleared, so new allocation
        }

        [Fact]
        public void ThreadSafety_ConcurrentRentReturn()
        {
            const int iterations = 1000;
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, iterations, i =>
            {
                try
                {
                    byte[] arr = BasisByteArrayPooling.Rent(32);
                    Thread.SpinWait(10);
                    BasisByteArrayPooling.Return(arr);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }
    }

    public class ThreadSafeMessagePoolTests
    {
        private class TestMessage
        {
            public int Value;
        }

        [Fact]
        public void Rent_ReturnsNewInstance_WhenPoolEmpty()
        {
            var obj = ThreadSafeMessagePool<TestMessage>.Rent();
            Assert.NotNull(obj);
        }

        [Fact]
        public void Rent_AfterReturn_ReusesInstance()
        {
            var obj = ThreadSafeMessagePool<TestMessage>.Rent();
            obj.Value = 42;
            ThreadSafeMessagePool<TestMessage>.Return(obj);

            var obj2 = ThreadSafeMessagePool<TestMessage>.Rent();
            Assert.Same(obj, obj2);
            Assert.Equal(42, obj2.Value); // state preserved
        }

        [Fact]
        public void ThreadSafety_ConcurrentRentReturn()
        {
            const int iterations = 1000;
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, iterations, i =>
            {
                try
                {
                    var obj = ThreadSafeMessagePool<TestMessage>.Rent();
                    obj.Value = i;
                    Thread.SpinWait(10);
                    ThreadSafeMessagePool<TestMessage>.Return(obj);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }
    }

    public class BasisObjectPoolTests
    {
        [Fact]
        public void Get_CallsFactory_WhenEmpty()
        {
            int callCount = 0;
            var pool = new BasisObjectPool<byte[]>(() =>
            {
                callCount++;
                return new byte[10];
            });

            var arr = pool.Get();
            Assert.NotNull(arr);
            Assert.Equal(10, arr.Length);
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void Get_ReusesReturnedItems()
        {
            var pool = new BasisObjectPool<byte[]>(() => new byte[10]);

            var arr1 = pool.Get();
            pool.Return(arr1);
            var arr2 = pool.Get();

            Assert.Same(arr1, arr2);
        }

        [Fact]
        public void Return_Null_ThrowsArgumentNullException()
        {
            var pool = new BasisObjectPool<byte[]>(() => new byte[1]);
            Assert.Throws<ArgumentNullException>(() => pool.Return(null));
        }

        [Fact]
        public void Constructor_NullFactory_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new BasisObjectPool<byte[]>(null));
        }

        [Fact]
        public void ThreadSafety_ConcurrentGetReturn()
        {
            var pool = new BasisObjectPool<byte[]>(() => new byte[16]);
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, 500, i =>
            {
                try
                {
                    var arr = pool.Get();
                    Thread.SpinWait(10);
                    pool.Return(arr);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }
    }
}
