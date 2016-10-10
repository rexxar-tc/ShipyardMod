using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.Settings;
using ShipyardMod.Utility;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.ItemClasses
{
    public enum ShipyardType : byte
    {
        Disabled,
        Weld,
        Grind,
        Invalid,
        Scanning
    }

    public class ShipyardItem
    {
        private MyTuple<bool, bool> _shouldDisable;
        //public int ActiveTargets;

        //these are set when processing a grid
        //public IMyCubeGrid Grid;
        //tool, target block
        public Dictionary<long, BlockTarget[]> BlocksToProcess = new Dictionary<long, BlockTarget[]>();

        public List<LineItem> BoxLines = new List<LineItem>(12);
        public HashSet<IMyCubeBlock> ConnectedCargo = new HashSet<IMyCubeBlock>();

        public HashSet<IMyCubeGrid> ContainsGrids = new HashSet<IMyCubeGrid>();
        public HashSet<IMyCubeGrid> IntersectsGrids = new HashSet<IMyCubeGrid>();

        public LCDMenu Menu = null;

        public Dictionary<string, int> MissingComponentsDict = new Dictionary<string, int>();
        public Dictionary<long, List<BlockTarget>> ProxDict = new Dictionary<long, List<BlockTarget>>();

        public YardSettingsStruct Settings;
        public MyOrientedBoundingBoxD ShipyardBox;
        public HashSet<BlockTarget> TargetBlocks = new HashSet<BlockTarget>();
        public IMyCubeBlock[] Tools;
        //public int TotalBlocks;
        public IMyEntity YardEntity;
        public List<IMyCubeGrid> YardGrids = new List<IMyCubeGrid>();

        public ShipyardType YardType;

        public ShipyardItem(MyOrientedBoundingBoxD box, IMyCubeBlock[] tools, ShipyardType yardType, IMyEntity yardEntity)
        {
            ShipyardBox = box;
            Tools = tools;
            YardType = yardType;
            YardEntity = yardEntity;
        }

        public long EntityId
        {
            get { return YardEntity.EntityId; }
        }

        public void Init(ShipyardType yardType)
        {
            if (YardType == yardType)
                return;

            YardType = yardType;

            foreach (IMyCubeGrid grid in ContainsGrids)
            {
                ((MyCubeGrid)grid).OnGridSplit += OnGridSplit;
            }

            YardGrids = ContainsGrids.Where(x => !x.MarkedForClose).ToList();
            ContainsGrids.Clear();
            IntersectsGrids.Clear();
            Utilities.Invoke(() =>
                             {
                                 foreach (IMyCubeBlock tool in Tools)
                                 {
                                     (tool as IMyFunctionalBlock)?.RequestEnable(true);
                                 }
                             });

            Communication.SendYardState(this);
        }

        public void Disable(bool broadcast = true)
        {
            _shouldDisable.Item1 = true;
            _shouldDisable.Item2 = broadcast;
        }

        public void ProcessDisable()
        {
            if (!_shouldDisable.Item1)
                return;

            foreach (IMyCubeGrid grid in YardGrids)
                ((MyCubeGrid)grid).OnGridSplit -= OnGridSplit;

            YardGrids.Clear();

            foreach (IMyCubeBlock tool in Tools)
            {
                BlocksToProcess[tool.EntityId] = new BlockTarget[3];
                if (YardType == ShipyardType.Invalid)
                {
                    Utilities.Invoke(() =>
                                     {
                                         var comp = tool.GameLogic.GetAs<ShipyardCorner>();
                                         comp.SetPowerUse(5);
                                         comp.SetMaxPower(5);
                                         comp.Shipyard = null;
                                     });
                }
            }
            //TotalBlocks = 0;
            MissingComponentsDict.Clear();
            ContainsGrids.Clear();
            IntersectsGrids.Clear();
            ProxDict.Clear();
            TargetBlocks.Clear();
            YardType = ShipyardType.Disabled;
            if (_shouldDisable.Item2 && MyAPIGateway.Multiplayer.IsServer)
                Communication.SendYardState(this);

            _shouldDisable.Item1 = false;
            _shouldDisable.Item2 = false;
        }

        public void HandleButtonPressed(int index)
        {
            Communication.SendButtonAction(YardEntity.EntityId, index);
        }

        public void UpdatePowerUse()
        {
            if (YardType == ShipyardType.Disabled || YardType == ShipyardType.Invalid)
            {
                Utilities.Invoke(() =>
                                 {
                                     foreach (IMyCubeBlock tool in Tools)
                                     {
                                         tool.GameLogic.GetAs<ShipyardCorner>().SetPowerUse(5);
                                         Communication.SendToolPower(tool.EntityId, 5);
                                     }
                                 });
            }
            else
            {
                Utilities.Invoke(() =>
                                 {
                                     foreach (IMyCubeBlock tool in Tools)
                                     {
                                         float power = 5;
                                         foreach (BlockTarget blockTarget in BlocksToProcess[tool.EntityId])
                                         {
                                             if (blockTarget == null)
                                                 continue;
                                             //the value 8 here is a coincidence, not to do with the number of corners on a box
                                             float powerReq = 30 + (float)blockTarget.ToolDist[tool.EntityId] / 8;
                                             if (YardType == ShipyardType.Weld)
                                                 power += powerReq * Settings.WeldMultiplier;
                                             else if (YardType == ShipyardType.Grind)
                                                 power += powerReq * Settings.GrindMultiplier;
                                         }
                                         
                                         tool.GameLogic.GetAs<ShipyardCorner>().SetPowerUse(power);
                                         Communication.SendToolPower(tool.EntityId, power);
                                     }
                                 });
            }
        }

        public void OnGridSplit(MyCubeGrid oldGrid, MyCubeGrid newGrid)
        {
            if (YardGrids.Any(g => g.EntityId == oldGrid.EntityId))
            {
                newGrid.OnGridSplit += OnGridSplit;
                YardGrids.Add(newGrid);
            }
        }
    }
}