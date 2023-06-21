using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ParallelTasks;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.ProcessHandlers;
using ShipyardMod.Settings;
using ShipyardMod.Utility;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

/* Hello there!
 * 
 * One of the best things about the SE modding community is sharing information, so
 * if you find something in here useful or interesting, feel free to use it in your own mod!
 * I just ask that you leave a comment saying something like 'shamelessly stolen from rexxar' :)
 * Or consider donating a few dollars at https://paypal.me/rexxar if you're able.
 * Or hell, even just leaving a comment on the mod page or the forum or wherever letting me know
 * you found an interesting tidbit in here is good enough for me.
 *  
 * This mod never would have happened without the huge amount of help from the other modders
 * in the KSH Discord server. Come hang out with some really cool people: https://discord.gg/Dqfhtuu
 * 
 * 
 * This mod is something I've wanted in the game since I started playing SE. I spent over 8
 * months on it, and I sincerely hope you enjoy it :)
 * 
 * <3 rexxar
 */

namespace ShipyardMod
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class ShipyardCore : MySessionComponentBase
    {
        private const string Version = "v3.24";
        private const string LastUpdate = "Oct22";

        //TODO
        public static volatile bool Debug = false;

        //private readonly List<Task> _tasks = new List<Task>();
        private static Task _task;
        public static readonly Dictionary<long, BoxItem> BoxDict = new Dictionary<long, BoxItem>();
        public static readonly CachingDictionary<IMyCubeGrid, ShipyardItem> TrackingIntersect = new CachingDictionary<IMyCubeGrid, ShipyardItem>();
        public static readonly CachingDictionary<IMyCubeGrid, ShipyardItem> TrackingContains = new CachingDictionary<IMyCubeGrid, ShipyardItem>();
        public static readonly CachingHashSet<ShipyardItem> TrackingYards = new CachingHashSet<ShipyardItem>();

        private bool _initialized;
        private bool _notified;

        private DateTime _lastMessageTime = DateTime.Now;
        private ProcessHandlerBase[] _processHandlers;
        private int _updateCount;
        private IMyHudNotification _warningNotification;
        double _warningTime;

        public static ProcessManager ProcessManager;

        private void Initialize()
        {
            AddMessageHandler();

            _processHandlers = new ProcessHandlerBase[]
                               {
                                   new ProcessShipyardAction(),
                                   new ProcessLocalYards(),
                                   new ProcessLCDMenu(),
                                   new ProcessShipyardDetection(),
                                   new ProcessConveyorCache(),
                               };

            ProcessManager = new ProcessManager(_processHandlers.Length, 10);
            Logging.Instance.WriteLine($"Shipyard Script Initialized: {Version}");
        }
        
        private void NotifyUpdate()
        {
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("notify.sav", typeof(ShipyardCore)))
            {
                var w = MyAPIGateway.Utilities.WriteFileInLocalStorage("notify.sav", typeof(ShipyardCore));
                w.Write(LastUpdate);
                w.Flush();
                w.Close();

                MyAPIGateway.Utilities.ShowNotification("Shipyards has updated. Enter '/shipyard new' to view the changelog", 5000, MyFontEnum.Green);
            }
            else
            {
                var r = MyAPIGateway.Utilities.ReadFileInLocalStorage("notify.sav", typeof(ShipyardCore));
                var con = r.ReadToEnd();
                r.Close();
                if (!con.Contains(LastUpdate))
                {
                    var w = MyAPIGateway.Utilities.WriteFileInLocalStorage("notify.sav", typeof(ShipyardCore));
                    w.Write(LastUpdate);
                    w.Flush();
                    w.Close();

                    MyAPIGateway.Utilities.ShowNotification($"Shipyards has updated to {Version}. Enter '/shipyard new' to view the changelog", 5000, MyFontEnum.Green);
                }
            }
        }

        private void NotifyError()
        {
            if (MyAPIGateway.Session?.Player?.PromoteLevel >= MyPromoteLevel.Moderator)
            {
                if (_warningNotification == null)
                {
                    _warningNotification = MyAPIGateway.Utilities.CreateNotification("ShipyardMod encountered an error! Please report on the workshop with '/shipyard workshop' and include the log!", 6000, MyFontEnum.Red);
                    _warningNotification.Show();
                    _warningTime = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
                }

                if (MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - _warningTime > 6000)
                {
                    _warningNotification.ResetAliveTime();
                    _warningNotification.Show();
                }
            }
        }

        private void ShowLog()
        {
            MyAPIGateway.Utilities.ShowMissionScreen("Shipyard Update", Version, "October 22 2017",
    @"Greetings engineers!

UPDATE OCTOBER 22:

Fixed an exception that was spamming logs every tick. Added ingame warning for mod errors.

UPDATE AUGUST 19:

This update should hopefully fix the issues people have been reporting with conveyors.

I've done a massive amount of optimization and parallelization. PLEASE leave a comment on the workshop page if it crashes or doesn't work as expected!

You can send '/shipyard workshop' in chat, and it will open the workshop page in the steam overlay!

More optimization and parallelization is coming Soon(tm)!

UPDATE JULY 25:

Shipyards now have an option to fill inventory of projections. Since projections keep track of what inventory is in the blueprint, the shipyard can pull cargo from its own grid and replace inventory in your projection, so it's like the day you blueprinted it!
Do note that this option is affected by normal welding efficiency, so you'll lose some components in the process!

UPDATE JULY 22:

The shipyard mod has received a major update, fixing many bugs and bringing some exciting new features!

I've fixed welding projections that are connected to the shiypard grid, fractional components, strange power use, graphical issues, various multithreading issues, and more.

Along with the projection fixes, there's a new option in the terminal which allows you to select a build pattern for projections. This is mostly a visual thing, but the results are interesting enough that it's worth having the option.

Power use has been rebalanced and reduced drastically. Component efficiency hasn't changed. As another balancing requirement, shipyard corners now require a Targeting Computer component, which is built with Platinum.

Most notably I've added a mobile version of the shiypard. It works the same as the normal fixed version, but requires much more power, and has a much lower component efficiency.

Mobile shipyards include a slight tractor beam effect, helping your fighters match speed to the shipyard so you can make repairs while under way, and also include an advanced locking feature.
This feature will lock grids inside mobile shipyards, but will consume a lot of power to maintain if you are doing hard maneuvers.

I hope you enjoy this update. Please visit the workshop and rate, subscribe, upvote, downvote, sing a jaunty tune, whatever it is that kids do these days.
You can open the workshop page by sending '/shipyard workshop' in chat.

Happy engineering!

<3 Rexxar");
        }

        private void HandleMessageEntered(string messageText, ref bool sendToOthers)
        {
            string messageLower = messageText.ToLower();
            
            if (!messageLower.StartsWith("/shipyard"))
                return;

            if (DateTime.Now - _lastMessageTime < TimeSpan.FromMilliseconds(200))
                return;

            if (messageLower.Equals("/shipyard debug on"))
            {
                lock (_processHandlers)
                {
                    Logging.Instance.WriteLine("Debug turned on");
                    Debug = true;
                }
            }
            else if (messageLower.Equals("/shipyard debug off"))
            {
                Logging.Instance.WriteLine("Debug turned off");
                Debug = false;
            }
            else if (messageLower.Equals("/shipyard new"))
            {
                sendToOthers = false;
                ShowLog();
                return;
            }
            else if (messageLower.Equals("/shipyard workshop"))
            {
                MyVisualScriptLogicProvider.OpenSteamOverlay(@"http://steamcommunity.com/sharedfiles/filedetails/?id=684618597");
                sendToOthers = false;
                return;
            }
            else if (messageLower.Equals("/shipyard crash"))
            {
                _runAction = ()=> { throw new Exception("TEST"); };
            }
            _lastMessageTime = DateTime.Now;

            sendToOthers = false;

            byte[] commandBytes = Encoding.UTF8.GetBytes(messageLower);
            byte[] idBytes = BitConverter.GetBytes(MyAPIGateway.Session.Player.SteamUserId);

            var message = new byte[commandBytes.Length + sizeof(ulong)];

            idBytes.CopyTo(message, 0);
            commandBytes.CopyTo(message, idBytes.Length);

            Communication.SendMessageToServer(Communication.MessageTypeEnum.ClientChat, message);
        }

        private void CalculateBoxesContaining()
        {
            lock (ShipyardCore.TrackingContains)
                foreach (var e in TrackingContains)
            {
                var yard = e.Value;
                var grid = e.Key;
                if (yard.YardType != ShipyardType.Disabled || grid.Closed || !ShipyardSettings.Instance.GetYardSettings(yard.EntityId).GuideEnabled)
                {
                    BoxDict.Remove(grid.EntityId);
                    continue;
                }

                BoxItem item;
                uint color;

                BoxDict.TryGetValue(grid.EntityId, out item);

                if (grid.Physics != null)
                    color = Color.Green.PackedValue;
                else
                {
                    var proj = grid.Projector();

                    if (proj == null) //ghost grid like Digi's helmet
                        continue;

                    if (proj.RemainingBlocks == 0) //projection is complete
                        continue;

                    color = Color.Cyan.PackedValue;
                }

                if (item == null)
                {
                    BoxDict[grid.EntityId] = new BoxItem
                                             {
                                                 Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                                                 GridId = grid.EntityId,
                                                 //PackedColor = grid.Physics == null ? Color.Cyan.PackedValue : Color.Green.PackedValue,
                                                 PackedColor = color,
                                                 LastMatrix = grid.WorldMatrix
                                             };
                }
                else
                {
                    var m = grid.WorldMatrix;
                    if (item.LastMatrix.EqualsFast(ref m))
                        return;

                    item.LastMatrix = m;
                    item.PackedColor = color;
                    item.Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid));
                }
            }
        }

        private void CalculateBoxesIntersecting()
        {
            foreach (var e in TrackingIntersect)
            {
                var yard = e.Value;
                var grid = e.Key;
                if (yard.YardType != ShipyardType.Disabled || grid.Closed || !ShipyardSettings.Instance.GetYardSettings(yard.EntityId).GuideEnabled)
                {
                    BoxDict.Remove(grid.EntityId);
                    continue;
                }

                BoxItem item;
                uint color;

                BoxDict.TryGetValue(grid.EntityId, out item);

                if (grid.Physics != null)
                    color = Color.Yellow.PackedValue;
                else
                {
                    var proj = grid.Projector();

                    if (proj == null) //ghost grid like Digi's helmet
                        continue;

                    if (proj.RemainingBlocks == 0) //projection is complete
                        continue;

                    color = Color.CornflowerBlue.PackedValue;
                }

                if (item == null)
                {
                    BoxDict[grid.EntityId] = new BoxItem
                                             {
                                                 Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                                                 GridId = grid.EntityId,
                                                 //PackedColor = grid.Physics == null ? Color.CornflowerBlue.PackedValue : Color.Yellow.PackedValue,
                                                 PackedColor = color,
                                                 LastMatrix = grid.WorldMatrix
                                             };
                }
                else
                {
                    var m = grid.WorldMatrix;
                    if (item.LastMatrix.EqualsFast(ref m))
                        return;

                    item.LastMatrix = m;
                    item.PackedColor = color;
                    item.Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid));
                }
            }
        }
        
        private void UpdateBoxes()
        {
            try
            {
                lock(TrackingContains)
                    TrackingContains.ApplyChanges();
            }
            catch (ArgumentNullException)
            {
                Logging.Instance.WriteLine("ArgumentNullException at TrackingContains. Attempting to continue...");
                lock (TrackingContains)
                    TrackingContains.Clear();
            }

            try
            {
                lock(TrackingIntersect)
                    TrackingIntersect.ApplyChanges();
            }
            catch (ArgumentNullException)
            {
                Logging.Instance.WriteLine("ArgumentNullException at TrackingIntersect. Attempting to continue...");
                lock (ShipyardCore.TrackingContains)
                    TrackingIntersect.Clear();
            }

            TrackingYards.ApplyChanges();

            CalculateBoxesContaining();
            CalculateBoxesIntersecting();
            //MyAPIGateway.Parallel.Do(CalculateBoxesIntersecting, CalculateBoxesContaining);
        }

        private void CalculateLines()
        {
            foreach (var e in Communication.LineDict.ToArray())
            {
                foreach (var line in e.Value)
                {
                    line.Start = MathUtility.CalculateEmitterOffset(line.EmitterBlock, line.Index);
                    var target = line.TargetGrid.GetCubeBlock(line.TargetBlock);
                    if (target == null || target.Closed())
                        continue;

                    line.End = target.GetPosition();

                    if (line.LinePackets != null)
                    {
                        line.LinePackets.Update(line.Start, line.End);
                    }
                }
            }
        }

        private void AddMessageHandler()
        {
            MyAPIGateway.Utilities.MessageEntered += HandleMessageEntered;
            Communication.RegisterHandlers();
        }

        private void RemoveMessageHandler()
        {
            MyAPIGateway.Utilities.MessageEntered -= HandleMessageEntered;
            Communication.UnregisterHandlers();
        }

        public override void Draw()
        {
            if (MyAPIGateway.Session?.Player == null || !_initialized)
                return;

            try
            {
                UpdateBoxes();
                CalculateLines();
                
                DrawLines();
                FadeLines();
                DrawScanning();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"Draw(): {ex}");
                MyLog.Default.WriteLineAndConsole("##SHIPYARD MOD: ENCOUNTERED ERROR DURING DRAW UPDATE. CHECK MOD LOG");
                NotifyError();
                if (Debug)
                    throw;
            }
        }

        private Action _runAction;

        public override void UpdateBeforeSimulation()
        {
            Profiler.ProfilingBlockBase block = null;
            if (_initialized)
                block = Profiler.Start("0.ShipyardMod.ShipyardCore", nameof(UpdateBeforeSimulation));

            try
            {
                try
                {
                    _runAction?.Invoke();
                }
                finally
                {
                    _runAction = null;
                }

                if (MyAPIGateway.Session == null)
                    return;

                if (!_initialized)
                {
                    _initialized = true;
                    Initialize();
                }

                if (!_notified && MyAPIGateway.Session.Player?.Character != null)
                {
                    _notified = true;
                    NotifyUpdate();
                }

                _updateCount++;

                using (Profiler.Start("0.ShipyardMod.ShipyardCore", nameof(UpdateBeforeSimulation), nameof(RunProcessHandlers)))
                    RunProcessHandlers();

                using (Profiler.Start("0.ShipyardMod.ShipyardCore", nameof(UpdateBeforeSimulation), nameof(ProcessManager)))
                    ProcessManager.Update(_updateCount);

                MyAPIGateway.Parallel.Start(UpdateYards);

                if (_updateCount % 10 != 0)
                    return;


                using (Profiler.Start("0.ShipyardMod.ShipyardCore", nameof(UpdateBeforeSimulation), nameof(CheckAndDamagePlayer)))
                    CheckAndDamagePlayer();

                using (Profiler.Start("0.ShipyardMod.ShipyardCore", nameof(UpdateBeforeSimulation), nameof(Utilities.ProcessActionQueue)))
                    Utilities.ProcessActionQueue();

                if (Debug)
                    Profiler.Save();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"UpdateBeforeSimulation(): {ex}");
                MyLog.Default.WriteLineAndConsole("##SHIPYARD MOD: ENCOUNTERED ERROR DURING MOD UPDATE. CHECK MOD LOG");
                NotifyError();
                if (Debug)
                    throw;
            }
            finally
            {
                block?.End();
            }
        }

        private void UpdateYards()
        {
            foreach (var item in ProcessShipyardDetection.ShipyardsList)
            {
                if (item.StaticYard)
                {
                    foreach (IMyCubeGrid yardGrid in item.YardGrids)
                        yardGrid.Stop();
                }
                else
                {
                    item.UpdatePosition();
                    item.NudgeGrids();
                }
            }

            foreach (var item in ProcessLocalYards.LocalYards)
            {
                if (!item.StaticYard)
                    item.UpdatePosition();
            }
        }

        private void CheckAndDamagePlayer()
        {
            var character = MyAPIGateway.Session.Player?.Controller?.ControlledEntity?.Entity as IMyCharacter;

            if (character == null)
                return;

            var damageBlock = Profiler.Start("0.ShipyardMod.ShipyardCore", nameof(CheckAndDamagePlayer));
            BoundingBoxD charbox = character.WorldAABB;

            MyAPIGateway.Parallel.ForEach(Communication.LineDict.Values.ToArray(), lineList =>
                                                                                   {
                                                                                       foreach (LineItem line in lineList)
                                                                                       {
                                                                                           var ray = new Ray(line.Start, line.End - line.Start);
                                                                                           double? intersection = charbox.Intersects(ray);
                                                                                           if (intersection.HasValue)
                                                                                           {
                                                                                               if (Vector3D.DistanceSquared(charbox.Center, line.Start) < Vector3D.DistanceSquared(line.Start, line.End))
                                                                                               {
                                                                                                   Utilities.Invoke(() => character.DoDamage(5, MyStringHash.GetOrCompute("ShipyardLaser"), true));
                                                                                               }
                                                                                           }
                                                                                       }
                                                                                   });
            damageBlock.End();
        }

        private void RunProcessHandlers()
        {
            //wait for execution to complete before starting up a new thread
            if (!_task.IsComplete)
                return;

            //exceptions are suppressed in tasks, so re-throw if one happens
            if (_task.Exceptions != null && _task.Exceptions.Length > 0)
            {
                MyLog.Default.WriteLineAndConsole("##SHIPYARD MOD: THREAD EXCEPTION, CHECK MOD LOG FOR MORE INFO.");
                MyLog.Default.WriteLineAndConsole("##SHIPYARD MOD: EXCEPTION: " + _task.Exceptions[0]);
                if (Debug)
                    throw _task.Exceptions[0];
            }

            //run all process handlers in serial so we don't have to design for concurrency
            _task = MyAPIGateway.Parallel.StartBackground(() =>
                                                {
                                                    string handlerName = "";
                                                    try
                                                    {
                                                        var processBlock = Profiler.Start("0.ShipyardMod.ShipyardCore", nameof(RunProcessHandlers));
                                                        foreach (ProcessHandlerBase handler in _processHandlers)
                                                        {
                                                            if (handler.CanRun())
                                                            {
                                                                handlerName = handler.GetType().Name;
                                                                var handlerBlock = Profiler.Start(handler.GetType().FullName);
                                                                //Logging.Instance.WriteDebug(handlerName + " start");
                                                                handler.Handle();
                                                                handler.LastUpdate = DateTime.Now;
                                                                handlerBlock.End();
                                                            }
                                                        }
                                                        processBlock.End();
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Logging.Instance.WriteLine($"Thread Exception: {handlerName}: {ex}");
                                                        Logging.Instance.Debug_obj("Thread exception! Check the log!");
                                                        throw;
                                                    }
                                                });
        }

        private void DrawScanning()
        {
            var toRemove = new List<ScanAnimation>();
            foreach (ScanAnimation animation in Communication.ScanList)
            {
                if (!animation.Draw())
                    toRemove.Add(animation);
            }

            foreach (ScanAnimation removeAnim in toRemove)
                Communication.ScanList.Remove(removeAnim);
        }

        private void DrawLines()
        {
            foreach (KeyValuePair<long, List<LineItem>> kvp in Communication.LineDict.ToArray())
            {
                foreach (LineItem line in kvp.Value)
                {
                    if (Communication.FadeList.Any(x => x.Start == line.Start))
                        continue;

                    if (line.Pulse)
                    {
                        PulseLines(line);
                        continue;
                    }

                    line.LinePackets?.DrawPackets();

                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ShipyardLaser"), ref line.Color, 0.4f);
                }
            }

            foreach (KeyValuePair<long, BoxItem> entry in BoxDict)
            {
                BoxItem box = entry.Value;
                Vector4 color = new Color(box.PackedColor).ToVector4();
                foreach (LineItem line in box.Lines)
                {
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ShipyardGizmo"), ref color, 1f);
                }
            }

            foreach (var item in TrackingYards)
            {
                Vector4 color = Color.White;
                if (item.YardType == ShipyardType.Disabled || item.YardType == ShipyardType.Invalid)
                    continue;

                foreach (LineItem line in item.BoxLines)
                {
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("WeaponLaserIgnoreDepth"), ref color, 1f);
                }
            }
        }

        private void PulseLines(LineItem item)
        {
            if (item.Descend)
                item.PulseVal -= 0.025;
            else
                item.PulseVal += 0.025;

            Vector4 drawColor = item.Color;
            drawColor.W = (float)((Math.Sin(item.PulseVal) + 1) / 2);
            if (drawColor.W <= 0.05)
                item.Descend = !item.Descend;
            MySimpleObjectDraw.DrawLine(item.Start, item.End, MyStringId.GetOrCompute("ShipyardLaser"), ref drawColor, drawColor.W * 0.4f);
        }

        private void FadeLines()
        {
            var linesToRemove = new List<LineItem>();
            foreach (LineItem line in Communication.FadeList)
            {
                line.FadeVal -= 0.075f;
                if (line.FadeVal <= 0)
                {
                    //blank the line for a couple frames. Looks better that way.
                    if (line.FadeVal <= -0.2f)
                        linesToRemove.Add(line);
                    continue;
                }
                Vector4 drawColor = line.Color;
                //do a cubic fade out
                drawColor.W = line.FadeVal * line.FadeVal * line.FadeVal;
                MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("ShipyardLaser"), ref drawColor, drawColor.W * 0.4f);
            }

            foreach (LineItem removeLine in linesToRemove)
            {
                Communication.FadeList.Remove(removeLine);
            }
        }
        
        protected override void UnloadData()
        {
            try
            {
                Utilities.SessionClosing = true;

                if (Utilities.AbortAllTasks())
                    Logging.Instance.WriteDebug("CAUGHT AND ABORTED TASK!!!!");

                RemoveMessageHandler();

                if (Logging.Instance != null)
                    Logging.Instance.Close();

                Communication.UnregisterHandlers();

                foreach (ShipyardItem yard in ProcessShipyardDetection.ShipyardsList.ToArray())
                    yard.Disable(false);

                ProcessShipyardDetection.ShipyardsList.Clear();
                ProcessManager = null;
                BoxDict.Clear();
            }
            catch
            {
                //ignore errors on session close
            }
        }
    }
}