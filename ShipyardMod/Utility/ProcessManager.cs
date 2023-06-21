using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;

namespace ShipyardMod.Utility
{
    public class ProcessManager
    {
        private readonly List<PerTickProcessor> _processors;
        private readonly List<PerTickProcessor> _addCache;
        private readonly List<PerTickProcessor> _removeCache;
        private readonly FastResourceLock _cacheLock;
        private readonly TimeSpan _compareTime;

        private static string FullName = typeof(ProcessManager).FullName;

        public ProcessManager(int capacity, int timeoutMs)
        {
            _processors = new List<PerTickProcessor>(capacity);
            _addCache = new List<PerTickProcessor>(capacity);
            _removeCache = new List<PerTickProcessor>(capacity);
            _compareTime = TimeSpan.FromMilliseconds(timeoutMs);
            _cacheLock = new FastResourceLock();
        }

        public void AddProcessor(PerTickProcessor processor)
        {
            using (_cacheLock.AcquireSharedUsing())
                _addCache.Add(processor);
        }

        public void Update(int updateCount)
        {
            if (_processors.Count == 0 && _addCache.Count == 0)
                return;

            using (Profiler.Start(FullName, nameof(Update)))
            {
                bool needsSort = false;
                using (_cacheLock.AcquireExclusiveUsing())
                {
                    if (_addCache.Any())
                    {
                        _processors.AddRange(_addCache);
                        _addCache.Clear();
                        needsSort = true;
                    }
                }

                if (needsSort)
                    _processors.Sort((a, b) => a.Priority.CompareTo(b.Priority));

                var start = DateTime.Now;

                foreach (var processor in _processors)
                {
                    if (!processor.Process(updateCount))
                        _removeCache.Add(processor);

                    if (DateTime.Now - start > _compareTime)
                        break;
                }

                foreach (var rem in _removeCache)
                    _processors.Remove(rem);

                _removeCache.Clear();
            }
        }
    }
}
