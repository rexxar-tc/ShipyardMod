using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShipyardMod.Utility
{
    public class PerTickQueue<T>
    {
        private Queue<T> _queue;

        public int Count => _queue.Count;
        public int UpdateInterval { get; }

        public PerTickQueue(int updateInterval)
        {
            UpdateInterval = updateInterval;
            _queue = new Queue<T>();
        }

        public PerTickQueue(int updateInterval, IEnumerable<T> collection)
        {
            UpdateInterval = updateInterval;
            _queue = new Queue<T>(collection);
        }

        public bool TryDequeue(int updateCount, out T result)
        {
            if (_queue.Count == 0 || updateCount % UpdateInterval != 0)
            {
                result = default(T);
                return false;
            }

            result = _queue.Dequeue();
            return true;
        }

        public bool TryDequeue(out T result)
        {
            if (_queue.Count == 0)
            {
                result = default(T);
                return false;
            }

            result = _queue.Dequeue();
            return true;
        }

        public void Enqueue(T value)
        {
            _queue.Enqueue(value);
        }


        public void Clear()
        {
            _queue.Clear();
        }
    }
}
