using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ParallelTasks;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using ShipyardMod.ItemClasses;
using ShipyardMod.ProcessHandlers;
using ShipyardMod.Settings;
using ShipyardMod.Utility;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
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
        private const string Version = "v2.2";

        //TODO
        public static volatile bool Debug;

        //private readonly List<Task> _tasks = new List<Task>();
        private static Task _task;
        public static readonly MyConcurrentDictionary<long, BoxItem> BoxDict = new MyConcurrentDictionary<long, BoxItem>();

        private bool _initialized;

        private DateTime _lastMessageTime = DateTime.Now;
        private ProcessHandlerBase[] _processHandlers;
        private int _updateCount;

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

            Logging.Instance.WriteLine($"Shipyard Script Initialized: {Version}");
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
                Logging.Instance.WriteLine("Debug turned on");
                Debug = true;
            }
            else if (messageLower.Equals("/shipyard debug off"))
            {
                Logging.Instance.WriteLine("Debug turned off");
                Debug = false;
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

        private void CalculateGridBoxes()
        {
            foreach (ShipyardItem item in ProcessLocalYards.LocalYards)
            {
                foreach (IMyCubeGrid grid in item.ContainsGrids)
                {
                    if (item.YardType != ShipyardType.Disabled || grid.Closed || !ShipyardSettings.Instance.GetYardSettings(item.EntityId).GuideEnabled)
                    {
                        BoxDict.Remove(grid.EntityId);
                        continue;
                    }
                    if (BoxDict.ContainsKey(grid.EntityId) && Vector3D.DistanceSquared(BoxDict[grid.EntityId].LastPos, grid.GetPosition()) < 0.01)
                        continue;

                    uint color;

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

                    BoxDict[grid.EntityId] = new BoxItem
                                             {
                                                 Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                                                 GridId = grid.EntityId,
                                                 //PackedColor = grid.Physics == null ? Color.Cyan.PackedValue : Color.Green.PackedValue,
                                                 PackedColor = color,
                                                 LastPos = grid.GetPosition()
                                             };
                }

                foreach (IMyCubeGrid grid in item.IntersectsGrids)
                {
                    if (item.YardType != ShipyardType.Disabled || grid.Closed || !ShipyardSettings.Instance.GetYardSettings(item.EntityId).GuideEnabled)
                    {
                        BoxDict.Remove(grid.EntityId);
                        continue;
                    }
                    if (BoxDict.ContainsKey(grid.EntityId) && Vector3D.DistanceSquared(BoxDict[grid.EntityId].LastPos, grid.GetPosition()) < 0.01)
                        continue;

                    uint color;

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

                    BoxDict[grid.EntityId] = new BoxItem
                                             {
                                                 Lines = MathUtility.CalculateObbLines(MathUtility.CreateOrientedBoundingBox(grid)),
                                                 GridId = grid.EntityId,
                                                 //PackedColor = grid.Physics == null ? Color.CornflowerBlue.PackedValue : Color.Yellow.PackedValue,
                                                 PackedColor = color,
                                                 LastPos = grid.GetPosition()
                                             };
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
                CalculateGridBoxes();
                DrawLines();
                FadeLines();
                DrawScanning();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"Draw(): {ex}");
                MyLog.Default.WriteLineAndConsole("##SHIPYARD MOD: ENCOUNTERED ERROR DURING DRAW UPDATE. CHECK MOD LOG");
                if (Debug)
                    throw;
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (MyAPIGateway.Session == null)
                    return;

                if (!_initialized)
                {
                    _initialized = true;
                    Initialize();
                }
                
                RunProcessHandlers();

                foreach (ShipyardItem item in ProcessShipyardDetection.ShipyardsList.ToArray())
                {
                    foreach (IMyCubeGrid yardGrid in item.YardGrids)
                        yardGrid.Stop();
                }

                if (_updateCount++ % 10 != 0)
                    return;

                CheckAndDamagePlayer();
                Utilities.ProcessActionQueue();

                if (Debug)
                    Profiler.Save();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"UpdateBeforeSimulation(): {ex}");
                MyLog.Default.WriteLineAndConsole("##SHIPYARD MOD: ENCOUNTERED ERROR DURING MOD UPDATE. CHECK MOD LOG");
                if (Debug)
                    throw;
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
                MyLog.Default.WriteLineAndConsole("##SHIPYARD MOD: INNER EXCEPTION: " + _task.Exceptions[0].InnerException);
                if (Debug)
                    throw _task.Exceptions[0].InnerException;
            }

            //run all process handlers in serial so we don't have to design for concurrency
            _task = MyAPIGateway.Parallel.Start(() =>
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
                                                                Logging.Instance.WriteDebug(handlerName + " start");
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
            foreach (KeyValuePair<long, List<LineItem>> kvp in Communication.LineDict)
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
                    MySimpleObjectDraw.DrawLine(line.Start, line.End, MyStringId.GetOrCompute("WeaponLaserIgnoreDepth"), ref color, 1f);
                }
            }

            foreach (ShipyardItem item in ProcessLocalYards.LocalYards)
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

        public override void UpdatingStopped()
        {
            Utilities.SessionClosing = true;
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
            }
            catch
            {
                //ignore errors on session close
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "ShipyardCorner_Large")]
    public class ShipyardCorner : MyGameLogicComponent
    {
        private static bool _init;
        private static readonly MyDefinitionId PowerDef = MyResourceDistributorComponent.ElectricityId;
        private static readonly List<IMyTerminalControl> Controls = new List<IMyTerminalControl>();
        private IMyCollector _block;
        private float _maxpower;
        private float _power;
        private string _info = String.Empty;

        private MyResourceSinkComponent _sink = new MyResourceSinkComponent();

        public ShipyardItem Shipyard = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _block = (IMyCollector)Container.Entity;
            _block.Components.TryGet(out _sink);
            _block.NeedsUpdate = MyEntityUpdateEnum.NONE;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            _block.OnClosing += OnClosing;
            _block.AppendingCustomInfo += AppendingCustomInfo;
        }

        private void OnClosing(IMyEntity obj)
        {
            _block.OnClosing -= OnClosing;
            _block.AppendingCustomInfo -= AppendingCustomInfo;
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public override void Close()
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_init)
                return;

            _init = true;
            _block = Entity as IMyCollector;

            if (_block == null)
                return;

            //create terminal controls
            IMyTerminalControlSeparator sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyCollector>(string.Empty);
            sep.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(sep);

            IMyTerminalControlOnOffSwitch guideSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>("Shipyard_GuideSwitch");
            guideSwitch.Title = MyStringId.GetOrCompute("Guide Boxes");
            guideSwitch.Tooltip = MyStringId.GetOrCompute("Toggles the guide boxes drawn around grids in the shipyard.");
            guideSwitch.OnText = MyStringId.GetOrCompute("On");
            guideSwitch.OffText = MyStringId.GetOrCompute("Off");
            guideSwitch.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            guideSwitch.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            guideSwitch.SupportsMultipleBlocks = true;
            guideSwitch.Getter = GetGuideEnabled;
            guideSwitch.Setter = SetGuideEnabled;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(guideSwitch);
            Controls.Add(guideSwitch);

            IMyTerminalControlButton grindButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Shipyard_GrindButton");
            IMyTerminalControlButton weldButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Shipyard_WeldButton");
            IMyTerminalControlButton stopButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("Shipyard_StopButton");

            grindButton.Title = MyStringId.GetOrCompute("Grind");
            grindButton.Tooltip = MyStringId.GetOrCompute("Begins grinding ships in the yard.");
            grindButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b)?.YardType == ShipyardType.Disabled;
            grindButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            grindButton.SupportsMultipleBlocks = true;
            grindButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Grind);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindButton);
            Controls.Add(grindButton);

            weldButton.Title = MyStringId.GetOrCompute("Weld");
            weldButton.Tooltip = MyStringId.GetOrCompute("Begins welding ships in the yard.");
            weldButton.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b)?.YardType == ShipyardType.Disabled;
            weldButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            weldButton.SupportsMultipleBlocks = true;
            weldButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Weld);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldButton);
            Controls.Add(weldButton);

            stopButton.Title = MyStringId.GetOrCompute("Stop");
            stopButton.Tooltip = MyStringId.GetOrCompute("Stops the shipyard.");
            stopButton.Enabled = b =>
                                 {
                                     if (!b.BlockDefinition.SubtypeId.Contains("ShipyardCorner"))
                                         return false;

                                     ShipyardItem yard = GetYard(b);

                                     return yard?.YardType == ShipyardType.Weld || yard?.YardType == ShipyardType.Grind;
                                 };
            stopButton.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            stopButton.SupportsMultipleBlocks = true;
            stopButton.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Disabled);
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(stopButton);
            Controls.Add(stopButton);

            IMyTerminalControlSlider beamCountSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Shipyard_BeamCount");
            beamCountSlider.Title = MyStringId.GetOrCompute("Beam Count");

            beamCountSlider.Tooltip = MyStringId.GetOrCompute("Number of beams this shipyard can use per corner.");
            beamCountSlider.SetLimits(1, 3);
            beamCountSlider.Writer = (b, result) => result.Append(GetBeamCount(b));
            beamCountSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            beamCountSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            beamCountSlider.Getter = b => GetBeamCount(b);
            beamCountSlider.Setter = (b, v) =>
                                     {
                                         SetBeamCount(b, (int)Math.Round(v, 0, MidpointRounding.ToEven));
                                         beamCountSlider.UpdateVisual();
                                     };
            beamCountSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(beamCountSlider);
            Controls.Add(beamCountSlider);

            IMyTerminalControlSlider grindSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Shipyard_GrindSpeed");
            grindSpeedSlider.Title = MyStringId.GetOrCompute("Grind Speed");

            grindSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this shipyard grinds grids.");
            grindSpeedSlider.SetLimits(0.01f, 2);
            grindSpeedSlider.Writer = (b, result) => result.Append(GetGrindSpeed(b));
            grindSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            grindSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            grindSpeedSlider.Getter = GetGrindSpeed;
            grindSpeedSlider.Setter = (b, v) =>
                                      {
                                          SetGrindSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                                          grindSpeedSlider.UpdateVisual();
                                      };
            grindSpeedSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(grindSpeedSlider);
            Controls.Add(grindSpeedSlider);

            IMyTerminalControlSlider weldSpeedSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("Shipyard_WeldSpeed");
            weldSpeedSlider.Title = MyStringId.GetOrCompute("Weld Speed");

            weldSpeedSlider.Tooltip = MyStringId.GetOrCompute("How fast this shipyard welds grids.");
            weldSpeedSlider.SetLimits(0.01f, 2);
            weldSpeedSlider.Writer = (b, result) => result.Append(GetWeldSpeed(b));
            weldSpeedSlider.Visible = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            weldSpeedSlider.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner") && GetYard(b) != null;
            weldSpeedSlider.Getter = GetWeldSpeed;
            weldSpeedSlider.Setter = (b, v) =>
                                     {
                                         SetWeldSpeed(b, (float)Math.Round(v, 2, MidpointRounding.ToEven));
                                         weldSpeedSlider.UpdateVisual();
                                     };
            weldSpeedSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(weldSpeedSlider);
            Controls.Add(weldSpeedSlider);

            IMyTerminalAction grindAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Shipyard_GrindAction");
            grindAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            grindAction.Name = new StringBuilder("Grind");
            grindAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            grindAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Grind);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(grindAction);

            IMyTerminalAction weldAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Shipyard_WeldAction");
            weldAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            weldAction.Name = new StringBuilder("Weld");
            weldAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            weldAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Weld);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(weldAction);

            IMyTerminalAction stopAction = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Shipyard_StopAction");
            stopAction.Enabled = b => b.BlockDefinition.SubtypeId.Contains("ShipyardCorner");
            stopAction.Name = new StringBuilder("Stop");
            stopAction.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            stopAction.Action = b => Communication.SendYardCommand(b.CubeGrid.EntityId, ShipyardType.Disabled);
            MyAPIGateway.TerminalControls.AddAction<IMyCollector>(stopAction);
        }

        private void AppendingCustomInfo(IMyTerminalBlock b, StringBuilder arg2)
        {
            try
            {
                float power = _power;
                float maxpower = _maxpower;
                if (GetYard(b) != null)
                {
                    maxpower *= Math.Max(b.GetValueFloat("Shipyard_GrindSpeed"), b.GetValueFloat("Shipyard_WeldSpeed"));
                    maxpower *= GetBeamCount(b);
                }
                var sb = new StringBuilder();
                sb.Append("Required Input: ");
                MyValueFormatter.AppendWorkInBestUnit(power, sb);
                sb.AppendLine();
                sb.Append("Max required input: ");
                MyValueFormatter.AppendWorkInBestUnit(maxpower, sb);
                sb.AppendLine();
                sb.Append(_info);
                sb.AppendLine();

                arg2.Append(sb);
            }
            catch (Exception)
            {
                //don't really care, just don't crash
            }
        }

        private int GetBeamCount(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 3;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).BeamCount;
        }

        private void SetBeamCount(IMyCubeBlock b, int value)
        {
            if (GetYard(b) == null)
                return;

            //this value check stops infinite loops of sending the setting to server and immediately getting the same value back
            if (value == GetBeamCount(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.BeamCount = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private bool GetGuideEnabled(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return true;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).GuideEnabled;
        }

        private void SetGuideEnabled(IMyCubeBlock b, bool value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetGuideEnabled(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.GuideEnabled = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private float GetGrindSpeed(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 0.1f;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).GrindMultiplier;
        }

        private void SetGrindSpeed(IMyCubeBlock b, float value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetGrindSpeed(b))
                return;

            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.GrindMultiplier = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private float GetWeldSpeed(IMyCubeBlock b)
        {
            if (GetYard(b) == null)
                return 0.1f;

            return ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId).WeldMultiplier;
        }

        private void SetWeldSpeed(IMyCubeBlock b, float value)
        {
            if (GetYard(b) == null)
                return;

            if (value == GetWeldSpeed(b))
                return;
            
            YardSettingsStruct settings = ShipyardSettings.Instance.GetYardSettings(b.CubeGrid.EntityId);
            settings.WeldMultiplier = value;

            ShipyardSettings.Instance.SetYardSettings(b.CubeGrid.EntityId, settings);

            Communication.SendShipyardSettings(b.CubeGrid.EntityId, settings);
        }

        private ShipyardItem GetYard(IMyCubeBlock b)
        {
            return b.GameLogic.GetAs<ShipyardCorner>()?.Shipyard;
        }

        public void SetPowerUse(float req)
        {
            _power = req;
        }

        public void SetMaxPower(float req)
        {
            _maxpower = req;
        }

        public void SetInfo(string info)
        {
            _info = info;
        }

        public void UpdateVisuals()
        {
            foreach (IMyTerminalControl control in Controls)
                control.UpdateVisual();
        }

        public override void UpdateBeforeSimulation()
        {
            if (!((IMyCollector)Container.Entity).Enabled)
                _power = 0f;
            _sink.SetMaxRequiredInputByType(PowerDef, _power);
            _sink.SetRequiredInputByType(PowerDef, _power);
            //sink.Update();
        }

        public override void UpdateBeforeSimulation10()
        {
            ((IMyTerminalBlock)Container.Entity).RefreshCustomInfo();
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
        }
    }
}