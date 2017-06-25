using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.ProcessHandlers;
using ShipyardMod.Settings;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.Utility
{
    public class Communication
    {
        private const ushort MESSAGE_ID = 18597;
        public static Dictionary<long, List<LineItem>> LineDict = new Dictionary<long, List<LineItem>>();
        public static HashSet<LineItem> FadeList = new HashSet<LineItem>();
        public static List<ScanAnimation> ScanList = new List<ScanAnimation>();
        public static string FullName = typeof(Communication).FullName;
        
        private static void Recieve(byte[] data)
        {
            try
            {
                var recieveBlock = Profiler.Start(FullName, nameof(Recieve));
                var messageId = (MessageTypeEnum)data[0];
                var newData = new byte[data.Length - 1];
                Array.Copy(data, 1, newData, 0, newData.Length);
                //Logging.Instance.WriteDebug("Recieve: " + messageId);

                switch (messageId)
                {
                    case MessageTypeEnum.ToolLine:
                        HandleToolLine(newData);
                        break;

                    case MessageTypeEnum.NewYard:
                        HandleNewShipyard(newData);
                        break;

                    case MessageTypeEnum.YardState:
                        HandleShipyardState(newData);
                        break;

                    case MessageTypeEnum.ClientChat:
                        HandleClientChat(newData);
                        break;

                    case MessageTypeEnum.ServerChat:
                        HandleServerChat(newData);
                        break;

                    case MessageTypeEnum.ServerDialog:
                        HandleServerDialog(newData);
                        break;

                    case MessageTypeEnum.ClearLine:
                        HandleClearLine(newData);
                        break;

                    case MessageTypeEnum.ShipyardSettings:
                        HandleShipyardSettings(newData);
                        break;

                    case MessageTypeEnum.YardCommand:
                        HandleYardCommand(newData);
                        break;

                    case MessageTypeEnum.ButtonAction:
                        HandleButtonAction(newData);
                        break;

                    case MessageTypeEnum.RequestYard:
                        HandleYardRequest(newData);
                        break;

                    case MessageTypeEnum.ToolPower:
                        HandleToolPower(newData);
                        break;

                    case MessageTypeEnum.ShipyardCount:
                        HandleYardCount(newData);
                        break;

                    case MessageTypeEnum.CustomInfo:
                        HandleCustomInfo(newData);
                        break;
                }
                recieveBlock.End();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Recieve(): " + ex);
            }
        }

        #region Comms Structs

        //(ﾉ◕ヮ◕)ﾉ*:･ﾟ✧ ABSTRACTION! (ﾉ◕ヮ◕)ﾉ*:･ﾟ✧
        public enum MessageTypeEnum : byte
        {
            ToolLine,
            NewYard,
            YardState,
            ClientChat,
            ServerChat,
            ServerDialog,
            ClearLine,
            ShipyardSettings,
            YardCommand,
            ButtonAction,
            RequestYard,
            ToolPower,
            ShipyardCount,
            CustomInfo
        }

        public struct ToolLineStruct
        {
            public long ToolId;
            public long GridId;
            public SerializableVector3I BlockPos;
            public uint PackedColor;
            public bool Pulse;
            public byte EmitterIndex;
        }

        public struct YardStruct
        {
            public long GridId;
            public long[] ToolIds;
            public ShipyardType YardType;
            public long ButtonId;
        }

        public struct DialogStruct
        {
            public string Title;
            public string Subtitle;
            public string Message;
            public string ButtonText;
        }

        #endregion

        #region Send Methods

        public static void SendLine(ToolLineStruct toolLine, Vector3D target)
        {
            string messageString = MyAPIGateway.Utilities.SerializeToXML(toolLine);
            byte[] data = Encoding.UTF8.GetBytes(messageString);
            SendMessageToNearby(target, 2000, MessageTypeEnum.ToolLine, data);
        }

        public static void ClearLine(long toolId, int index)
        {
            var data = new byte[sizeof(long) + 1];
            data[0] = (byte)index;
            BitConverter.GetBytes(toolId).CopyTo(data, 1);
            SendMessageToClients(MessageTypeEnum.ClearLine, data);
        }

        public static void SendYardState(ShipyardItem item)
        {
            var data = new byte[sizeof(long) + 1];
            BitConverter.GetBytes(item.EntityId).CopyTo(data, 0);
            data[sizeof(long)] = (byte)item.YardType;

            SendMessageToClients(MessageTypeEnum.YardState, data);
        }

        public static void SendNewYard(ShipyardItem item, ulong steamId = 0)
        {
            Logging.Instance.WriteLine("Sent Yard");
            var newYard = new YardStruct
                          {
                              GridId = item.EntityId,
                              ToolIds = item.Tools.Select(x => x.EntityId).ToArray(),
                              YardType = item.YardType
                          };

            if (item.Menu?.Buttons != null)
            {
                Logging.Instance.WriteLine("Button ID: " + item.Menu.Buttons.EntityId);
                newYard.ButtonId = item.Menu.Buttons.EntityId;
            }

            string message = MyAPIGateway.Utilities.SerializeToXML(newYard);
            byte[] data = Encoding.UTF8.GetBytes(message);

            if (steamId == 0)
                SendMessageToClients(MessageTypeEnum.NewYard, data);
            else
                SendMessageTo(MessageTypeEnum.NewYard, data, steamId);
        }

        public static void SendMessageToNearby(Vector3D target, double distance, MessageTypeEnum messageId, byte[] data)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            double distSq = distance * distance;
            foreach (IMyPlayer player in players)
            {
                //compare squared values for M A X I M U M  S P E E D
                if (Vector3D.DistanceSquared(player.GetPosition(), target) <= distSq)
                    SendMessageTo(messageId, data, player.SteamUserId);
            }
        }

        //SendMessageToOthers is apparently bugged
        public static void SendMessageToClients(MessageTypeEnum messageId, byte[] data, bool skipLocal = false)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer player in players)
            {
                if (skipLocal && player == MyAPIGateway.Session.Player)
                    continue;

                SendMessageTo(messageId, data, player.SteamUserId);
            }
        }

        public static void SendMessageToServer(MessageTypeEnum messageId, byte[] data)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)messageId;
            data.CopyTo(newData, 1);

            Utilities.Invoke(() => MyAPIGateway.Multiplayer.SendMessageToServer(MESSAGE_ID, newData));
        }

        public static void SendMessageTo(MessageTypeEnum messageId, byte[] data, ulong steamId)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)messageId;
            data.CopyTo(newData, 1);

            Utilities.Invoke(() => MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, newData, steamId));
        }

        public static void SendShipyardSettings(long entityId, YardSettingsStruct settings)
        {
            string message = MyAPIGateway.Utilities.SerializeToXML(settings);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            var data = new byte[sizeof(long) + messageBytes.Length];

            BitConverter.GetBytes(entityId).CopyTo(data, 0);
            messageBytes.CopyTo(data, sizeof(long));

            SendMessageToServer(MessageTypeEnum.ShipyardSettings, data);
        }

        public static void SendPrivateInfo(ulong steamId, string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);

            SendMessageTo(MessageTypeEnum.ServerChat, data, steamId);
        }

        public static void SendDialog(ulong steamId, string title, string subtitle, string message, string button = "close")
        {
            var msg = new DialogStruct
                      {
                          Title = title,
                          Subtitle = subtitle,
                          Message = message,
                          ButtonText = button
                      };
            string serialized = MyAPIGateway.Utilities.SerializeToXML(msg);
            byte[] data = Encoding.ASCII.GetBytes(serialized);

            SendMessageTo(MessageTypeEnum.ServerDialog, data, steamId);
        }

        public static void SendYardCommand(long yardId, ShipyardType type)
        {
            var data = new byte[sizeof(long) + 1];
            BitConverter.GetBytes(yardId).CopyTo(data, 0);
            data[sizeof(long)] = (byte)type;

            SendMessageToServer(MessageTypeEnum.YardCommand, data);
        }

        private static DateTime _lastButtonSend = DateTime.Now;

        public static void SendButtonAction(long yardId, int index)
        {
            if (DateTime.Now - _lastButtonSend < TimeSpan.FromMilliseconds(100))
                return;

            _lastButtonSend = DateTime.Now;
            var data = new byte[sizeof(long) + 1];
            BitConverter.GetBytes(yardId).CopyTo(data, 0);
            data[sizeof(long)] = (byte)index;
            SendMessageToServer(MessageTypeEnum.ButtonAction, data);
        }

        public static void RequestShipyards()
        {
            byte[] data = BitConverter.GetBytes(MyAPIGateway.Session.Player.SteamUserId);
            SendMessageToServer(MessageTypeEnum.RequestYard, data);
        }

        public static void SendToolPower(long blockId, float power)
        {
            var data = new byte[sizeof(long) + sizeof(float)];
            BitConverter.GetBytes(blockId).CopyTo(data, 0);
            BitConverter.GetBytes(power).CopyTo(data, sizeof(long));

            SendMessageToClients(MessageTypeEnum.ToolPower, data);
        }

        public static void SendYardCount()
        {
            byte[] data = BitConverter.GetBytes(ProcessShipyardDetection.ShipyardsList.Count);

            SendMessageToClients(MessageTypeEnum.ShipyardCount, data);
        }

        public static void SendCustomInfo(long entityId, string info)
        {
            byte[] message = Encoding.UTF8.GetBytes(info);
            var data = new byte[message.Length + sizeof(long)];
            BitConverter.GetBytes(entityId).CopyTo(data, 0);
            Array.Copy(message, 0, data, sizeof(long), message.Length);

            SendMessageToClients(MessageTypeEnum.CustomInfo, data);
        }

        #endregion

        #region Receive Methods

        private static void HandleToolPower(byte[] data)
        {
            long blockId = BitConverter.ToInt64(data, 0);
            float power = BitConverter.ToSingle(data, sizeof(long));

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(blockId, out entity))
                return;

            ((IMyCollector)entity).GameLogic.GetAs<ShipyardCorner>().SetPowerUse(power);
        }

        private static void HandleToolLine(byte[] data)
        {
            string message = Encoding.UTF8.GetString(data);
            var item = MyAPIGateway.Utilities.SerializeFromXML<ToolLineStruct>(message);

            IMyEntity toolEntity;
            IMyEntity gridEntity;

            MyAPIGateway.Entities.TryGetEntityById(item.ToolId, out toolEntity);
            MyAPIGateway.Entities.TryGetEntityById(item.GridId, out gridEntity);

            var tool = toolEntity as IMyCubeBlock;
            var grid = gridEntity as IMyCubeGrid;
            if (grid == null || tool == null)
                return;

            var newLine = new LineItem
                          {
                              Start = MathUtility.CalculateEmitterOffset(tool, item.EmitterIndex),
                              End = grid.GridIntegerToWorld(item.BlockPos),
                              Color = new Color(item.PackedColor).ToVector4(),
                              Pulse = item.Pulse,
                              PulseVal = 0,
                              Index = item.EmitterIndex,
                              EmitterBlock = tool,
                              TargetGrid = grid,
                              TargetBlock = item.BlockPos,
                          };

            if (newLine.Color == Color.OrangeRed.ToVector4())
            {
                newLine.Color.W = 0.5f;
                newLine.LinePackets = new PacketManager(newLine.End, newLine.Start, Color.Yellow.ToVector4(), invert: true);
            }
            else if (newLine.Color == Color.DarkCyan.ToVector4())
                newLine.LinePackets = new PacketManager(newLine.Start, newLine.End, Color.CadetBlue.ToVector4());
            else
                newLine.LinePackets = null;

            if (LineDict.ContainsKey(item.ToolId))
            {
                LineItem oldLine = null;
                foreach (LineItem line in LineDict[item.ToolId])
                    if (line.Index == item.EmitterIndex)
                        oldLine = line;

                if (oldLine == null)
                {
                    LineDict[item.ToolId].Add(newLine);
                    return;
                }

                if (oldLine.Color == Color.Purple.ToVector4() && item.ToolId != 0)
                {
                    //if the old line is pulsing, and the new one is set to pulse, leave it alone so we don't reset the pulse cycle
                    if (newLine.Color == Color.Purple.ToVector4() && (oldLine.End == newLine.End))
                        return;

                    FadeList.Add(oldLine);
                    LineDict[item.ToolId].Remove(oldLine);
                    LineDict[item.ToolId].Add(newLine);
                    return;
                }

                if (oldLine.End == newLine.End)
                    return;

                LineDict[item.ToolId].Remove(oldLine);

                if (newLine.Start != Vector3D.Zero)
                    LineDict[item.ToolId].Add(newLine);

                if (LineDict[item.ToolId].Count == 0)
                    LineDict.Remove(item.ToolId);

                FadeList.Add(oldLine);
            }
            else
                LineDict[item.ToolId] = new List<LineItem>(3) {newLine};
        }

        private static void HandleNewShipyard(byte[] data)
        {
            Logging.Instance.WriteLine("ReceivedYard");
            string message = Encoding.UTF8.GetString(data);
            var yardStruct = MyAPIGateway.Utilities.SerializeFromXML<YardStruct>(message);

            //the server has already verified this shipyard. Don't question it, just make the shipyard item
            if (yardStruct.ToolIds.Length != 8)
                return;

            IMyEntity outEntity;

            if (!MyAPIGateway.Entities.TryGetEntityById(yardStruct.GridId, out outEntity))
                return;

            var yardGrid = outEntity as IMyCubeGrid;
            if (yardGrid == null)
                return;

            var points = new List<Vector3D>(8);
            var tools = new List<IMyCubeBlock>(8);
            foreach (long toolId in yardStruct.ToolIds)
            {
                IMyEntity entity;
                if (!MyAPIGateway.Entities.TryGetEntityById(toolId, out entity))
                    return;

                var block = entity as IMyCubeBlock;
                if (block == null)
                    return;

                tools.Add(block);
                points.Add(block.GetPosition());
            }

            MyOrientedBoundingBoxD yardBox = MathUtility.CreateOrientedBoundingBox(yardGrid, points, 2.5);

            var yardItem = new ShipyardItem(yardBox, tools.ToArray(), yardStruct.YardType, yardGrid);

            if (MyAPIGateway.Entities.TryGetEntityById(yardStruct.ButtonId, out outEntity))
            {
                Logging.Instance.WriteLine("Bind Buttons");
                var buttons = (IMyButtonPanel)outEntity;
                buttons.ButtonPressed += yardItem.HandleButtonPressed;
                var blockDef = (MyButtonPanelDefinition)MyDefinitionManager.Static.GetCubeBlockDefinition(buttons.BlockDefinition);
                for (int i = 1; i <= 4; i ++)
                {
                    var c = blockDef.ButtonColors[i % blockDef.ButtonColors.Length];
                    buttons.SetEmissiveParts($"Emissive{i}", new Color(c.X, c.Y, c.Z), c.W);
                }
                buttons.SetCustomButtonName(0, "Exit");
                buttons.SetCustomButtonName(1, "Up");
                buttons.SetCustomButtonName(2, "Down");
                buttons.SetCustomButtonName(3, "Select");
                //buttons.SetEmissiveParts("Emissive1", blockDef.ButtonColors[1 % blockDef.ButtonColors.Length], 1);
                //buttons.SetEmissiveParts("Emissive2", blockDef.ButtonColors[2 % blockDef.ButtonColors.Length], 1);
                //buttons.SetEmissiveParts("Emissive3", blockDef.ButtonColors[3 % blockDef.ButtonColors.Length], 1);
                //buttons.SetEmissiveParts("Emissive4", blockDef.ButtonColors[4 % blockDef.ButtonColors.Length], 1);
            }

            foreach (IMyCubeBlock tool in yardItem.Tools)
            {
                var corner = ((IMyCollector)tool).GameLogic.GetAs<ShipyardCorner>();
                corner.Shipyard = yardItem;
                //tool.SetEmissiveParts("Emissive1", Color.Yellow, 0f);
            }

            yardItem.UpdateMaxPowerUse();
            yardItem.Settings = ShipyardSettings.Instance.GetYardSettings(yardItem.EntityId);
            ProcessLocalYards.LocalYards.Add(yardItem);
        }

        private static void HandleShipyardState(byte[] data)
        {
            long gridId = BitConverter.ToInt64(data, 0);
            var type = (ShipyardType)data.Last();

            Logging.Instance.WriteDebug("Got yard state: " + type);
            Logging.Instance.WriteDebug($"Details: {gridId} {ProcessLocalYards.LocalYards.Count} [{string.Join(" ", data)}]");

            foreach (ShipyardItem yard in ProcessLocalYards.LocalYards)
            {
                if (yard.EntityId != gridId)
                    continue;

                yard.YardType = type;
                Logging.Instance.WriteLine(type.ToString());

                foreach (IMyCubeBlock yardTool in yard.Tools)
                    yardTool?.GameLogic?.GetAs<ShipyardCorner>()?.UpdateVisuals();

                switch (type)
                {
                    case ShipyardType.Disabled:
                    case ShipyardType.Invalid:
                        yard.Disable(false);

                        foreach (IMyCubeBlock tool in yard.Tools)
                        {
                            if (LineDict.ContainsKey(tool.EntityId))
                            {
                                foreach (LineItem line in LineDict[tool.EntityId])
                                    FadeList.Add(line);
                                LineDict.Remove(tool.EntityId);
                            }
                            //tool.SetEmissiveParts("Emissive0", Color.White, 0.5f);
                        }
                        break;
                    case ShipyardType.Scanning:
                        ScanList.Add(new ScanAnimation(yard));
                        //foreach(var tool in yard.Tools)
                        //    tool.SetEmissiveParts("Emissive0", Color.Green, 0.5f);
                        break;
                        //case ShipyardType.Weld:
                        //foreach (var tool in yard.Tools)
                        //    tool.SetEmissiveParts("Emissive0", Color.DarkCyan, 0.5f);
                        //break;
                        //case ShipyardType.Grind:
                        //foreach (var tool in yard.Tools)
                        //    tool.SetEmissiveParts("Emissive0", Color.OrangeRed, 0.5f);
                        //break;
                    //default:
                    //    throw new ArgumentOutOfRangeException();
                }

                yard.UpdateMaxPowerUse();
                
            }
        }

        private static void HandleClientChat(byte[] data)
        {
            ulong remoteSteamId = BitConverter.ToUInt64(data, 0);
            string command = Encoding.UTF8.GetString(data, sizeof(ulong), data.Length - sizeof(ulong));
            if (!command.StartsWith("/shipyard"))
                return;

            Logging.Instance.WriteLine("Received chat: " + command);
            if (MyAPIGateway.Session.IsUserAdmin(remoteSteamId))
            {
                if (command.Equals("/shipyard debug on"))
                {
                    Logging.Instance.WriteLine("Debug turned on");
                    ShipyardCore.Debug = true;
                }
                else if (command.Equals("/shipyard debug off"))
                {
                    Logging.Instance.WriteLine("Debug turned off");
                    ShipyardCore.Debug = false;
                }
            }
            /*
            foreach (ChatHandlerBase handler in ChatHandlers)
            {
                if (handler.CanHandle(remoteSteamId, command))
                {
                    string[] splits = command.Replace(handler.CommandText(), "").Split(new[] {" "}, StringSplitOptions.RemoveEmptyEntries);
                    handler.Handle(remoteSteamId, splits);
                    return;
                }
            }
            */
        }

        private static void HandleServerChat(byte[] data)
        {
            if (MyAPIGateway.Session.Player == null)
                return;

            string message = Encoding.ASCII.GetString(data);

            MyAPIGateway.Utilities.ShowMessage("Shipyard Overlord", message);
        }

        private static void HandleServerDialog(byte[] data)
        {
            string serialized = Encoding.ASCII.GetString(data);
            var msg = MyAPIGateway.Utilities.SerializeFromXML<DialogStruct>(serialized);

            MyAPIGateway.Utilities.ShowMissionScreen(msg.Title, null, msg.Subtitle, msg.Message.Replace("|", "\r\n"), null, msg.ButtonText);
        }

        private static void HandleClearLine(byte[] bytes)
        {
            byte index = bytes[0];
            long toolId = BitConverter.ToInt64(bytes, 1);

            if (!LineDict.ContainsKey(toolId))
                return;

            List<LineItem> entry = LineDict[toolId];
            var toRemove = new HashSet<LineItem>();
            foreach (LineItem line in entry)
            {
                if (line.Index != index)
                    continue;

                line.FadeVal = 1;
                FadeList.Add(line);
                toRemove.Add(line);
            }

            if (entry.Count == 0)
                LineDict.Remove(toolId);

            foreach (LineItem line in toRemove)
                LineDict[toolId].Remove(line);
        }

        private static void HandleShipyardSettings(byte[] data)
        {
            long entityId = BitConverter.ToInt64(data, 0);
            string message = Encoding.UTF8.GetString(data, sizeof(long), data.Length - sizeof(long));
            var settings = MyAPIGateway.Utilities.SerializeFromXML<YardSettingsStruct>(message);

            bool found = false;

            Logging.Instance.WriteDebug($"Received shipyard settings:\r\n" +
                                        $"\t{settings.EntityId}\r\n" +
                                        $"\t{settings.BeamCount}\r\n" +
                                        $"\t{settings.GuideEnabled}\r\n" +
                                        $"\t{settings.GrindMultiplier}\r\n" +
                                        $"\t{settings.WeldMultiplier}");

            foreach (ShipyardItem yard in ProcessShipyardDetection.ShipyardsList)
            {
                if (yard.EntityId != entityId)
                    continue;

                yard.Settings = settings;
                yard.UpdateMaxPowerUse();  // BeamCount and Multiplier affect our maxpower calculation
                ShipyardSettings.Instance.SetYardSettings(yard.EntityId, settings);
                found = true;
                break;
            }

            foreach (ShipyardItem yard in ProcessLocalYards.LocalYards)
            {
                if (yard.EntityId != entityId)
                    continue;

                yard.Settings = settings;
                yard.UpdateMaxPowerUse();  // BeamCount and Multiplier affect our maxpower calculation
                ShipyardSettings.Instance.SetYardSettings(yard.EntityId, settings);
                
                foreach (IMyCubeBlock tool in yard.Tools)
                {
                    tool.GameLogic.GetAs<ShipyardCorner>().UpdateVisuals();
                }
                
                found = true;
                break;
            }

            if (found && MyAPIGateway.Multiplayer.IsServer)
            {
                ShipyardSettings.Instance.Save();
                //pass true to skip the local player
                //on player-hosted MP we can get caught in an infinite loop if we don't
                SendMessageToClients(MessageTypeEnum.ShipyardSettings, data, true);
            }
        }

        private static void HandleYardCommand(byte[] data)
        {
            long yardId = BitConverter.ToInt64(data, 0);
            var type = (ShipyardType)data.Last();
            Logging.Instance.WriteDebug($"Received Yard Command: {type} for {yardId}");

            foreach (ShipyardItem yard in ProcessShipyardDetection.ShipyardsList)
            {
                if (yard.EntityId != yardId)
                    continue;

                if (type == ShipyardType.Disabled || type == ShipyardType.Invalid)
                    yard.Disable();
                else
                    yard.Init(type);

                break;
            }
        }

        private static void HandleButtonAction(byte[] data)
        {
            long yardId = BitConverter.ToInt64(data, 0);
            byte index = data.Last();

            foreach (ShipyardItem yard in ProcessShipyardDetection.ShipyardsList)
            {
                if (yard.EntityId == yardId && yard.Menu != null)
                {
                    yard.Menu.ButtonPanelHandler(index);
                    return;
                }
            }
        }

        private static void HandleYardRequest(byte[] data)
        {
            ulong steamId = BitConverter.ToUInt64(data, 0);

            Logging.Instance.WriteLine("Recieved shipyard request from " + steamId);

            if (!ProcessShipyardDetection.ShipyardsList.Any())
                return;

            foreach (ShipyardItem yard in ProcessShipyardDetection.ShipyardsList)
                SendNewYard(yard, steamId);
        }

        private static void HandleYardCount(byte[] data)
        {
            if (MyAPIGateway.Multiplayer.IsServer || MyAPIGateway.Session.Player?.Controller?.ControlledEntity?.Entity == null)
                return;

            int count = BitConverter.ToInt32(data, 0);

            if (ProcessLocalYards.LocalYards.Count != count)
            {
                RequestShipyards();
            }
        }

        private static void HandleCustomInfo(byte[] data)
        {
            long entityId = BitConverter.ToInt64(data, 0);
            string info = Encoding.UTF8.GetString(data, sizeof(long), data.Length - sizeof(long));
            IMyEntity outEntity;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out outEntity))
                return;

            IMyCollector col = outEntity as IMyCollector;

            if (col == null)
                return;

            col.GameLogic.GetAs<ShipyardCorner>()?.SetInfo(info);
        }

        #endregion

        #region Handler Setup

        public static void RegisterHandlers()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(MESSAGE_ID, Recieve);
        }

        public static void UnregisterHandlers()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(MESSAGE_ID, Recieve);
        }

        #endregion
    }
}