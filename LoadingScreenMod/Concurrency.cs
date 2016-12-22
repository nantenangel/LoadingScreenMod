using System.Collections.Generic;
using System.Threading;

namespace LoadingScreenMod
{
    /// <summary>
    /// A thread-safe queue. Enqueue never blocks. Dequeue blocks while the queue is empty.
    /// SetCompleted unblocks all blocked threads.
    /// </summary>
    public class ConcurrentQueue<T>
    {
        Queue<T> queue;
        object sync = new object();
        volatile bool completed = false;
        public int Count => queue.Count;
        public bool Completed => completed;

        public ConcurrentQueue(int capacity)
        {
            queue = new Queue<T>(capacity);
        }

        public void Enqueue(T item)
        {
            lock (sync)
            {
                queue.Enqueue(item);
                Monitor.Pulse(sync);
            }
        }

        public bool Dequeue(out T result)
        {
            lock (sync)
            {
                while (!completed && Count == 0)
                    Monitor.Wait(sync);

                if (Count > 0)
                {
                    result = queue.Dequeue();
                    return true;
                }
            }

            result = default(T);
            return false;
        }

        public void SetCompleted()
        {
            lock (sync)
            {
                completed = true;
                Monitor.PulseAll(sync);
            }
        }
    }

    /// <summary>
    /// A thread-safe counter with fixed upper and lower bounds. Attempts to increase or decrease
    /// past the bounds will block. The class is suitable for producer-consumer synchronization.
    /// </summary>
    public sealed class ConcurrentCounter
    {
        int value, min, max;
        object sync = new object();

        public ConcurrentCounter(int value, int min, int max)
        {
            this.value = value;
            this.min = min;
            this.max = max;
        }

        public void Increment()
        {
            lock (sync)
            {
                while (value >= max)
                    Monitor.Wait(sync);

                value++;
                Monitor.PulseAll(sync);
            }
        }

        public void Decrement()
        {
            lock (sync)
            {
                while (value <= min)
                    Monitor.Wait(sync);

                value--;
                Monitor.PulseAll(sync);
            }
        }
    }
}
