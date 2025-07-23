using System;
using System.Threading;
namespace BasisNetworkCore.Pooling
{
    public class LockFreeQueue<T>
    {
        private readonly T[] _buffer;
        private readonly int _mask;
        private volatile int _head = 0;
        private volatile int _tail = 0;

        public LockFreeQueue(int capacityPowerOf2 = 1024)
        {
            if ((capacityPowerOf2 & (capacityPowerOf2 - 1)) != 0)
                throw new ArgumentException("Capacity must be a power of two.");

            _buffer = new T[capacityPowerOf2];
            _mask = capacityPowerOf2 - 1;
        }

        public bool Enqueue(T item)
        {
            int currentTail;
            int nextTail;

            do
            {
                currentTail = _tail;
                nextTail = (currentTail + 1) & _mask;

                if (nextTail == _head)
                    return false; // full

            } while (Interlocked.CompareExchange(ref _tail, nextTail, currentTail) != currentTail);

            _buffer[currentTail & _mask] = item;
            return true;
        }

        public bool Dequeue(out T item)
        {
            int currentHead;

            do
            {
                currentHead = _head;

                if (currentHead == _tail)
                {
                    item = default;
                    return false; // empty
                }

            } while (Interlocked.CompareExchange(ref _head, (currentHead + 1) & _mask, currentHead) != currentHead);

            item = _buffer[currentHead & _mask];
            return true;
        }
    }
}
