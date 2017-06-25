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
using System;
using VRage.Collections;
using VRage.Game.Components;

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
        public HashSet<IMyTerminalBlock> ConnectedCargo = new HashSet<IMyTerminalBlock>();

        public MyConcurrentHashSet<IMyCubeGrid> ContainsGrids = new MyConcurrentHashSet<IMyCubeGrid>();
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
        public bool StaticYard;

        public ShipyardItem(MyOrientedBoundingBoxD box, IMyCubeBlock[] tools, ShipyardType yardType, IMyEntity yardEntity)
        {
            ShipyardBox = box;
            Tools = tools;
            YardType = yardType;
            YardEntity = yardEntity;
            StaticYard = tools[0].BlockDefinition.SubtypeId == "ShipyardCorner_Large";
        }

        public long EntityId
        {
            get { return YardEntity.EntityId; }
        }

        public void Init(ShipyardType yardType)
        {
            if (YardType == yardType)
                return;

            Logging.Instance.WriteDebug("YardItem.Init: " + yardType);

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
                                     var myFunctionalBlock = tool as IMyFunctionalBlock;
                                     if (myFunctionalBlock != null)
                                         myFunctionalBlock.Enabled = true; //.RequestEnable(true);
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

        /*
         * Worst case power usage would be a grid of 24+ blocks immediately
         * inside one corner of our yard.  All (up to) 24 of our lasers will be
         * required to weld/grind this grid, but the lasers from the opposite
         * corner would be the longest, effectively equal to the diagonal
         * length of our yard.
         * 
         * (each corner block)
         *     base power = 5
         *     (each laser)
         *         base power = 30
         *         additional power = 300 * Max(WeldMultiplier, GrindMultiplier) * (YardDiagonal / 200000)
         * 
         * For balance, we scale power usage so that when YardDiag^2 == 200,000
         * (the same distance that our component effeciency bottoms out: ~450m),
         * each //LASER// at 1x multiplier consumes the full output of a Large
         * Reactor (300 MW).
         * 
         * To put that in perspective, a cubical shipyard with a ~450m diagonal
         * would be ~250m on each edge, or about 100 large blocks. But keep in
         * mind: even with a shipyard this big, as long as the target grid is
         * centered within the volume of the shipyard, laserLength would only
         * be 225m, not the full 450m.  And at 225m, each laser consumes only 
         * 75 MW: 25% of the max.  So with a shipyard of this size, centering
         * your target grid gets more and more important.
         * 
         * Unlike component efficiency, however, which bottoms out to a fixed
         * "minimum" efficiency past 450m, power requirements for lasers longer
         * than this will continue to increase exponentially.
         * 
         * This really only needs to be called whenever yard settings are changed
         * (since BeamCount and multipliers affect this calculation), or when the
         * yard changes state (weld/grind/disable)
         */
        public void UpdateMaxPowerUse()
        {
            float multiplier;
            var corners = new Vector3D[8];
            ShipyardBox.GetCorners(corners, 0);

            if (YardType == ShipyardType.Weld)
            {
                multiplier = Settings.WeldMultiplier;
            }
            else if (YardType == ShipyardType.Grind)
            {
                multiplier = Settings.GrindMultiplier;
            }
            else
            {
                // Yard is neither actively welding or grinding right now, so just show worst case
                multiplier = Math.Max(Settings.WeldMultiplier, Settings.GrindMultiplier);
            }

            float maxpower = 5 + Settings.BeamCount * (30 + 300 * multiplier * (float)Vector3D.DistanceSquared(corners[0], corners[6]) / 200000);

            if (!StaticYard)
                maxpower *= 2;

            foreach (IMyCubeBlock tool in Tools)
            {
                ((IMyCollector)tool).GameLogic.GetAs<ShipyardCorner>().SetMaxPower(maxpower);
            }
        }

        public void UpdatePowerUse(float addedPower = 0)
        {
            addedPower /= 8;
            if (YardType == ShipyardType.Disabled || YardType == ShipyardType.Invalid)
            {
                Utilities.Invoke(() =>
                                 {
                                     foreach (IMyCubeBlock tool in Tools)
                                     {
                                         tool.GameLogic.GetAs<ShipyardCorner>().SetPowerUse(5 + addedPower);
                                         Communication.SendToolPower(tool.EntityId, 5 + addedPower);
                                     }
                                 });
            }
            else
            {
                float multiplier;
                if (YardType == ShipyardType.Weld)
                {
                    multiplier = Settings.WeldMultiplier;
                }
                else
                {
                    multiplier = Settings.GrindMultiplier;
                }

                Utilities.Invoke(() =>
                                 {
                                     foreach (IMyCubeBlock tool in Tools)
                                     {
                                         float power = 5;
                                         //Logging.Instance.WriteDebug(String.Format("Tool[{0}] Base power usage [{1:F1} MW]", tool.DisplayNameText, power));
                                         int i = 0;
                                         foreach (BlockTarget blockTarget in BlocksToProcess[tool.EntityId])
                                         {
                                             if (blockTarget == null)
                                                 continue;

                                             float laserPower = 30 + 300 * multiplier * (float)blockTarget.ToolDist[tool.EntityId] / 200000;
                                             //Logging.Instance.WriteDebug(String.Format("Tool[{0}] laser[{1}] distance[{2:F1}m] multiplier[{3:F1}x] additional power req [{4:F1} MW]", tool.DisplayNameText, i, Math.Sqrt(blockTarget.ToolDist[tool.EntityId]), multiplier, laserPower));
                                             power += laserPower;
                                             i++;
                                         }

                                         if (!StaticYard)
                                             power *= 2;

                                         power += addedPower;

                                         //Logging.Instance.WriteDebug(String.Format("Tool[{0}] Total computed power [{1:F1} MW]", tool.DisplayNameText, power));
                                         var log = tool.GameLogic.GetAs<ShipyardCorner>();
                                         if (log == null)
                                             continue;
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

        public void UpdatePosition()
        {
            ShipyardBox = MathUtility.CreateOrientedBoundingBox((IMyCubeGrid)YardEntity, Tools.Select(x => x.GetPosition()).ToList(), 2.5);
        }

        /// <summary>
        /// Gives grids in the shipyard a slight nudge to help them match velocity when the shipyard is moving.
        /// 
        /// Code donated by Equinox
        /// </summary>
        public void NudgeGrids()
        {
        //magic value of 0.005 here was determined experimentally.
        //value is just enough to assist with matching velocity to the shipyard, but not enough to prevent escape
            foreach (var grid in ContainsGrids)
            {
                if (Vector3D.IsZero(grid.Physics.LinearVelocity - YardEntity.Physics.LinearVelocity))
                    continue;

                grid.Physics.ApplyImpulse(grid.Physics.Mass * Vector3D.ClampToSphere((YardEntity.Physics.LinearVelocity - grid.Physics.LinearVelocity), 0.005), grid.Physics.CenterOfMassWorld);
            }
            
            foreach (var grid in YardGrids)
            {
                if (!Settings.AdvancedLocking)
                    grid.Physics.ApplyImpulse(grid.Physics.Mass * Vector3D.ClampToSphere((YardEntity.Physics.LinearVelocity - grid.Physics.LinearVelocity), 0.005), grid.Physics.CenterOfMassWorld);
                else
                {
                    double powerUse = MathUtility.MatchShipVelocity(grid, YardEntity, true);
                    if(powerUse > 0)
                    UpdatePowerUse((float)powerUse);
                }
            }
        }
    }
}