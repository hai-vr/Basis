using System;
using System.Threading;

namespace BasisNetworkCore.Pooling
{
    public class LockFreeSpinThreadPool : IDisposable
    {
        private readonly Thread[] _threads;
        private readonly LockFreeQueue<Action> _queue;
        private volatile bool _running = true;
        private int _activeCount = 0;

        public LockFreeSpinThreadPool(int threadCount, int queueSizePowerOf2 = 1024)
        {
            _queue = new LockFreeQueue<Action>(queueSizePowerOf2);
            _threads = new Thread[threadCount];

            for (int Index = 0; Index < threadCount; Index++)
            {
                _threads[Index] = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = $"LFThread-{Index}"
                };
                _threads[Index].Start();
            }
        }

        public bool Enqueue(Action task)
        {
            return _queue.Enqueue(task);
        }

        private void WorkerLoop()
        {
            var spinner = new SpinWait();

            while (_running)
            {
                if (_queue.Dequeue(out Action task))
                {
                    Interlocked.Increment(ref _activeCount);
                    try
                    {
                        task?.Invoke();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeCount);
                    }

                    spinner.Reset(); // got work, reset spinner
                }
                else
                {
                    spinner.SpinOnce(); // spin without yielding
                }
            }
        }

        public void Dispose()
        {
            _running = false;

            foreach (var t in _threads)
            {
                if (t.IsAlive)
                {
                    t.Join();
                }
            }
        }
    }
}
