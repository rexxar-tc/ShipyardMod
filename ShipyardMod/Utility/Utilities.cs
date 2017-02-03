using System;
using System.Collections.Generic;
using System.Linq;
using ParallelTasks;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.ProcessHandlers;
using VRage;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.Utility
{
    public static class Utilities
    {
        private static readonly MyConcurrentHashSet<FastResourceLock> ThreadLocks = new MyConcurrentHashSet<FastResourceLock>();
        private static readonly MyConcurrentQueue<Action> ActionQueue = new MyConcurrentQueue<Action>();
        public static volatile bool SessionClosing;
        private static readonly string FullName = typeof(Utilities).FullName;

        private static volatile bool Processing;
        private static Task lastInvokeTask;

        /// <summary>
        ///     Invokes actions on the game thread, and blocks until completion
        /// </summary>
        /// <param name="action"></param>
        public static void InvokeBlocking(Action action)
        {
            var threadLock = new FastResourceLock();

            if (!SessionClosing)
                ThreadLocks.Add(threadLock);

            threadLock.AcquireExclusive();
            try
            {
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                          {
                                                              try
                                                              {
                                                                  var invokeBlock = Profiler.Start(FullName, nameof(InvokeBlocking));
                                                                  action();
                                                                  invokeBlock.End();
                                                                }
                                                              catch (Exception ex)
                                                              {
                                                                  Logging.Instance.WriteLine("Exception on blocking game thread invocation: " + ex);

                                                                  if (!SessionClosing && ShipyardCore.Debug)
                                                                      throw;
                                                              }
                                                              finally
                                                              {
                                                                  threadLock.ReleaseExclusive();
                                                              }
                                                          });
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Exception in Utilities.InvokeBlocking: " + ex);
                threadLock.ReleaseExclusive();

                if (!SessionClosing && ShipyardCore.Debug)
                    throw;
            }

            threadLock.AcquireExclusive();
            threadLock.ReleaseExclusive();

            if (!SessionClosing)
                ThreadLocks.Remove(threadLock);
        }
        
        /// <summary>
        ///     Wraps InvokeOnGameThread in lots of try/catch to reduce failure on session close
        /// </summary>
        /// <param name="action"></param>
        public static void Invoke(Action action)
        {
            try
            {
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                          {
                                                              try
                                                              {
                                                                  var invokeBlock = Profiler.Start(FullName, nameof(Invoke));
                                                                  action();
                                                                  invokeBlock.End();
                                                              }
                                                              catch (Exception ex)
                                                              {
                                                                  Logging.Instance.WriteLine("Exception on game thread invocation: " + ex);
                                                                  if (!SessionClosing && ShipyardCore.Debug)
                                                                      throw;
                                                              }
                                                          });
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Exception in Utilities.Invoke: " + ex);
                if (!SessionClosing && ShipyardCore.Debug)
                    throw;
            }
        }

        /// <summary>
        ///     Invokes an action on the game thread with a callback
        /// </summary>
        /// <param name="action"></param>
        /// <param name="callback"></param>
        public static void Invoke(Action action, Action callback)
        {
            MyAPIGateway.Parallel.StartBackground(() => { InvokeBlocking(action); }, callback);
        }

        /// <summary>
        ///     Enqueus Actions to be executed in an worker thread separate from the game thread
        /// </summary>
        /// <param name="action"></param>
        public static void QueueAction(Action action)
        {
            ActionQueue.Enqueue(action);
        }

        /// <summary>
        ///     Processes the action queue
        /// </summary>
        public static void ProcessActionQueue()
        {
            if (Processing || ActionQueue.Count == 0)
                return;

            if (lastInvokeTask.Exceptions != null && lastInvokeTask.Exceptions.Length > 0)
                throw lastInvokeTask.Exceptions[0];

            Processing = true;
            lastInvokeTask = MyAPIGateway.Parallel.Start(() =>
                                                         {
                                                             try
                                                             {
                                                                 var queueBlock = Profiler.Start(FullName, nameof(ProcessActionQueue));
                                                                 while (ActionQueue.Count > 0)
                                                                 {
                                                                     Action action = ActionQueue.Dequeue();
                                                                     action();
                                                                 }
                                                                 queueBlock.End();
                                                             }
                                                             catch (Exception ex)
                                                             {
                                                                 Logging.Instance.WriteLine("Exception in ProcessActionQueue: " + ex);
                                                                 if (!SessionClosing && ShipyardCore.Debug)
                                                                     throw;
                                                             }
                                                             finally
                                                             {
                                                                 Processing = false;
                                                             }
                                                         });
        }

        /// <summary>
        ///     Causes any waiting calls to InvokeBlocking() to return immediately
        /// </summary>
        /// <returns></returns>
        public static bool AbortAllTasks()
        {
            bool result = false;
            foreach (FastResourceLock threadLock in ThreadLocks)
            {
                result = true;
                threadLock.ReleaseExclusive();
            }

            return result;
        }

        /// <summary>
        ///     Gets the entity from the given list that is closest to the given point
        /// </summary>
        /// <param name="target"></param>
        /// <param name="candidates"></param>
        /// <returns></returns>
        public static IMyEntity GetNearestTo(Vector3D target, IEnumerable<IMyEntity> candidates)
        {
            IMyEntity result = null;
            double minDist = 0;

            foreach (IMyEntity entity in candidates)
            {
                double distance = Vector3D.DistanceSquared(target, entity.GetPosition());
                if (result == null || distance < minDist)
                {
                    minDist = distance;
                    result = entity;
                }
            }

            return result;
        }

        /// <summary>
        ///     Finds the shipyard nearest to the given point
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static ShipyardItem GetNearestYard(Vector3D target)
        {
            double minDist = 0;
            IMyCubeBlock closestCorner = null;

            foreach (ShipyardItem yard in ProcessShipyardDetection.ShipyardsList.ToArray())
            {
                foreach (IMyCubeBlock corner in yard.Tools)
                {
                    double distance = Vector3D.DistanceSquared(target, corner.GetPosition());

                    if (closestCorner == null || distance < minDist)
                    {
                        minDist = distance;
                        closestCorner = corner;
                    }
                }
            }

            foreach (ShipyardItem toVerify in ProcessShipyardDetection.ShipyardsList.ToArray())
            {
                if (toVerify.Tools.Contains(closestCorner))
                    return toVerify;
            }

            return null;
        }

        public static bool TryGetPlayerFromSteamId(ulong steamId, out IMyPlayer result)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, x => x.SteamUserId == steamId);

            if (players.Count == 0)
            {
                result = null;
                return false;
            }

            result = players[0];
            return true;
        }
    }
}