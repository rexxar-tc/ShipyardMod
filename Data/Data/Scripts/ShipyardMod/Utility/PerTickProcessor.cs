using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;

namespace ShipyardMod.Utility
{
    public class PerTickProcessor
    {
        private readonly Queue<Action> _actionQueue;
        private Action _callback;
        private readonly int _stride;
        private readonly bool _allowParallel;
        private readonly int _tickCount;
        public readonly int Priority;
        private Action[] _tmpArray;

        public PerTickProcessor(int priority, int tickCount = 1, int stride = 1, bool allowParallel = false, Action callback = null, IEnumerable<Action> actions = null)
        {
            Priority = priority;
            _tickCount = tickCount;
            _stride = stride;
            _allowParallel = allowParallel;
            _callback = callback;
            if (actions != null)
                _actionQueue = new Queue<Action>(actions);
            else
                _actionQueue = new Queue<Action>();
            if (allowParallel)
                _tmpArray = new Action[_stride];
        }

        public void Enqueue(Action action)
        {
            _actionQueue.Enqueue(action);
        }

        /// <summary>
        /// Processes the actions in the queue. Returns false if the queue is empty, true if processing should continue next tick.
        /// </summary>
        /// <param name="currentTick"></param>
        /// <returns></returns>
        public bool Process(int currentTick)
        {
            if (currentTick % _tickCount != 0)
                return true;

            if (_allowParallel)
            {
                Array.Clear(_tmpArray, 0, _stride);
                Action action;
                for (int i = 0; i < _stride; i++)
                {
                    if (_actionQueue.TryDequeue(out action))
                        _tmpArray[i] = action;
                }

                //Executes the array of Action potentially in parallel, and blocks until completion.
                MyAPIGateway.Parallel.Do(_tmpArray);

                if (_actionQueue.Count == 0)
                {
                    _callback?.Invoke();
                    return false;
                }
            }
            else
            {
                for (int i = 0; i < _stride; i++)
                {
                    Action action;
                    if (_actionQueue.TryDequeue(out action))
                        action();
                }

                if (_actionQueue.Count == 0)
                {
                    _callback?.Invoke();
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Blocks the current thread until the processor finishes.
        /// USE WITH **EXTREME** CAUTION!!
        /// </summary>
        public void Wait()
        {
            if (_actionQueue.Count == 0)
                return;

            FastResourceLock l = new FastResourceLock();

            l.AcquireExclusive();
            _callback += () => l.ReleaseExclusive();
            l.AcquireExclusive();
            l.ReleaseExclusive();
        }
    }
}
