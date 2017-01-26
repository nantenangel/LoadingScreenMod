using System;
using System.Collections.Generic;
using System.Threading;

namespace LoadingScreenModTest
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

    /// <summary>
    /// A dictionary that maintains insertion order. Inspired by Java's LinkedHashMap.
    /// This implementation is very minimal.
    /// </summary>
    public sealed class LinkedHashMap<K, V>
    {
        readonly Dictionary<K, Node> map;
        readonly Node head;
        Node spare;

        public LinkedHashMap(int capacity)
        {
            map = new Dictionary<K, Node>(capacity);
            head = new Node();
            head.prev = head;
            head.next = head;
        }

        public int Count => map.Count;
        public bool ContainsKey(K key) => map.ContainsKey(key);
        public K EldestKey => head.next.key;

        public V this[K key]
        {
            get { return map[key].val; }

            set
            {
                Node n;

                if (map.TryGetValue(key, out n))
                    n.val = value;
                else
                    Add(key, value);
            }
        }

        public void Add(K key, V val)
        {
            Node n = CreateNode(key, val);
            map.Add(key, n);
            n.prev = head.prev;
            n.next = head;
            head.prev.next = n;
            head.prev = n;
        }

        public bool TryGetValue(K key, out V val)
        {
            Node n;

            if (map.TryGetValue(key, out n))
            {
                val = n.val;
                return true;
            }

            val = default(V);
            return false;
        }

        public void Reinsert(K key)
        {
            Node n;

            if (map.TryGetValue(key, out n))
            {
                n.prev.next = n.next;
                n.next.prev = n.prev;
                n.prev = head.prev;
                n.next = head;
                head.prev.next = n;
                head.prev = n;
            }
        }

        public bool Remove(K key)
        {
            Node n;

            if (map.TryGetValue(key, out n))
            {
                map.Remove(key);
                n.prev.next = n.next;
                n.next.prev = n.prev;
                AddSpare(n);
                return true;
            }

            return false;
        }

        public void RemoveEldest()
        {
            Node n = head.next;
            map.Remove(n.key);
            head.next = n.next;
            n.next.prev = head;
            AddSpare(n);
        }

        public void Clear()
        {
            while(Count > 0)
            {
                RemoveEldest();
                spare = null;
            }
        }

        Node CreateNode(K key, V val)
        {
            Node n = spare;

            if (n == null)
                n = new Node();
            else
                spare = n.next;

            n.key = key; n.val = val;
            return n;
        }

        void AddSpare(Node n)
        {
            n.key = default(K); n.val = default(V); n.prev = null; n.next = spare;
            spare = n;
        }

        sealed class Node
        {
            internal K key;
            internal V val;
            internal Node prev, next;
        }
    }

    public abstract class Instance<T>
    {
        private static T inst;

        public static T instance
        {
            get
            {
                return inst;
            }

            set
            {
                Instance<T>.inst = value;
            }
        }

        internal static T Create()
        {
            if (Instance<T>.inst == null)
                Instance<T>.inst = (T) Activator.CreateInstance(typeof(T), true);

            return Instance<T>.inst;
        }
    }
}
