using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BasisNetworkCore.Pooling
{
    public class EfficientScheduler : IDisposable
    {
        private class TaskEntry : IComparable<TaskEntry>
        {
            public long ExecuteAtTicks;
            public Action Callback;

            public int CompareTo(TaskEntry other)
            {
                return ExecuteAtTicks.CompareTo(other.ExecuteAtTicks);
            }
        }

        private readonly SortedSet<TaskEntry> _taskQueue = new();
        private readonly object _lock = new();
        private readonly Thread _workerThread;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly AutoResetEvent _newTaskEvent = new(false);
        private volatile bool _running = true;

        public EfficientScheduler()
        {
            _workerThread = new Thread(Run)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _workerThread.Start();
        }

        public void Schedule(Action callback, int delayMs)
        {
            long executeAt = _stopwatch.ElapsedTicks + delayMs * Stopwatch.Frequency / 1000;

            lock (_lock)
            {
                _taskQueue.Add(new TaskEntry
                {
                    ExecuteAtTicks = executeAt,
                    Callback = callback
                });
                _newTaskEvent.Set(); // Notify the thread of a new task
            }
        }

        private void Run()
        {
            while (_running)
            {
                TaskEntry nextTask = null;
                long now = _stopwatch.ElapsedTicks;

                lock (_lock)
                {
                    if (_taskQueue.Count > 0)
                    {
                        nextTask = Min(_taskQueue);
                        if (nextTask.ExecuteAtTicks <= now)
                        {
                            _taskQueue.Remove(nextTask);
                        }
                        else
                        {
                            nextTask = null;
                        }
                    }
                }

                if (nextTask != null)
                {
                    try { nextTask.Callback(); }
                    catch (Exception e) { Console.WriteLine($"Task exception: {e}"); }
                }
                else
                {
                    long waitTimeTicks;

                    lock (_lock)
                    {
                        if (_taskQueue.Count > 0)
                        {
                            long nextTick = Min(_taskQueue).ExecuteAtTicks;
                            now = _stopwatch.ElapsedTicks;
                            waitTimeTicks = Math.Max(0, nextTick - now);
                        }
                        else
                        {
                            waitTimeTicks = Stopwatch.Frequency; // 1 second max wait
                        }
                    }

                    int waitMs = (int)(waitTimeTicks * 1000 / Stopwatch.Frequency);
                    _newTaskEvent.WaitOne(Math.Min(waitMs, 100)); // Wake early for new tasks
                }
            }
        }

        private TaskEntry Min(SortedSet<TaskEntry> set) => set.Min;

        public void Dispose()
        {
            _running = false;
            _newTaskEvent.Set();
            _workerThread.Join();
            _newTaskEvent.Dispose();
        }
    }
}
