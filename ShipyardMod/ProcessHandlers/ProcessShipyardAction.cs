using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.Utility;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.ProcessHandlers
{
    public class ProcessShipyardAction : ProcessHandlerBase
    {
        private static readonly string FullName = typeof(ProcessShipyardAction).FullName;

        private readonly HashSet<BlockTarget> _stalledTargets = new HashSet<BlockTarget>();

        private readonly MyInventory _tmpInventory = new MyInventory();

        public override int GetUpdateResolution()
        {
            return 500;
        }

        public override bool ServerOnly()
        {
            return true;
        }

        public override void Handle()
        {
            foreach (ShipyardItem item in ProcessShipyardDetection.ShipyardsList)
            {
                item.ProcessDisable();
                Profiler.ProfilingBlock startBlock = Profiler.Start(FullName, nameof(Handle), "Target Detection");
                //see if the shipyard has been deleted
                //just disable the yard and skip it, the ShipyardHandler will take care of deleting the item
                if (item.YardEntity.Closed || item.YardEntity.MarkedForClose)
                {
                    item.Disable();
                    item.ProcessDisable();
                    continue;
                }

                item.UpdatePowerUse();

                if (item.Tools.Any(x => !((IMyFunctionalBlock)x).Enabled))
                {
                    item.Disable();
                    item.ProcessDisable();
                    continue;
                }

                //look for grids inside the shipyard
                if (item.YardType == ShipyardType.Disabled)
                {
                    var allEntities = new HashSet<IMyEntity>();
                    var grids = new HashSet<IMyCubeGrid>();

                    MyAPIGateway.Entities.GetEntities(allEntities);
                    if (allEntities.Count == 0)
                    {
                        //this can happen when we pull the entitiy list outside of the game thread
                        Logging.Instance.WriteLine("Failed to get entity list in ShipyardAction");
                        continue;
                    }

                    //do a fast check to get entities near the shipyard
                    //we check against the OBB later, but it takes more time than this
                    double centerDist = item.ShipyardBox.HalfExtent.LengthSquared();

                    foreach (IMyEntity entity in allEntities)
                    {
                        if (!(entity is IMyCubeGrid) || entity.EntityId == item.EntityId)
                            continue;

                        if (entity.Closed || entity.MarkedForClose)
                            continue;

                        //use the center of the ship's bounding box. GetPosition() returns the pivot point, which can be far away from grid center
                        if (Vector3D.DistanceSquared(entity.Center(), item.ShipyardBox.Center) <= centerDist)
                            grids.Add(entity as IMyCubeGrid);
                    }

                    if (grids.Count == 0)
                        continue;

                    foreach (IMyCubeGrid grid in grids)
                    {
                        //create a bounding box around the ship
                        MyOrientedBoundingBoxD gridBox = MathUtility.CreateOrientedBoundingBox(grid);

                        //check if the ship is completely inside the shipyard
                        if (item.ShipyardBox.Contains(ref gridBox) == ContainmentType.Contains)
                            item.ContainsGrids.Add(grid);
                        else
                        //in case it was previously inside, but moved
                            item.ContainsGrids.Remove(grid);
                    }

                    continue;
                }

                var toRemove = new List<IMyCubeGrid>();

                foreach (IMyCubeGrid yardGrid in item.YardGrids)
                {
                    if (yardGrid.Closed || yardGrid.MarkedForClose)
                    {
                        toRemove.Add(yardGrid);
                        continue;
                    }

                    //check if the target has left the shipyard
                    MyOrientedBoundingBoxD gridBox = MathUtility.CreateOrientedBoundingBox(yardGrid);
                    if (item.ShipyardBox.Contains(ref gridBox) != ContainmentType.Contains)
                    {
                        Logging.Instance.WriteLine("Target grid left yard");
                        toRemove.Add(yardGrid);
                    }
                }

                foreach (IMyCubeGrid removeGrid in toRemove)
                {
                    item.YardGrids.Remove(removeGrid);
                    ((MyCubeGrid)removeGrid).OnGridSplit -= item.OnGridSplit;
                    var blocks = new List<IMySlimBlock>();
                    removeGrid.GetBlocks(blocks);
                    var targetsToRemove = new HashSet<BlockTarget>();
                    foreach (BlockTarget target in item.TargetBlocks.Where(x => blocks.Contains(x.Block)))
                    {
                        targetsToRemove.Add(target);

                        foreach (KeyValuePair<long, List<BlockTarget>> entry in item.ProxDict)
                            entry.Value.Remove(target);
                        foreach (KeyValuePair<long, BlockTarget[]> procEntry in item.BlocksToProcess)
                        {
                            for (int i = 0; i < procEntry.Value.Length; i++)
                            {
                                if (procEntry.Value[i] == target)
                                {
                                    procEntry.Value[i] = null;
                                    Communication.ClearLine(procEntry.Key, i);
                                }
                            }
                        }
                    }
                    foreach (BlockTarget remove in targetsToRemove)
                        item.TargetBlocks.Remove(remove);
                }

                if (item.YardGrids.Count == 0 || item.YardGrids.All(g => g.Physics == null && g.Projector() != null))
                {
                    Logging.Instance.WriteLine("Disabling shipyard; no more targets");
                    //clear out and disable the shipyard
                    item.Disable();
                }

                startBlock.End();
                if (item.YardType == ShipyardType.Grind)
                {
                    Profiler.ProfilingBlock grindBlock = Profiler.Start(FullName, "StepGrind");
                    StepGrind(item);
                    grindBlock.End();
                }
                else if (item.YardType == ShipyardType.Weld)
                {
                    Profiler.ProfilingBlock weldBlock = Profiler.Start(FullName, "StepWeld");
                    if (!StepWeld(item))
                        item.Disable();
                    weldBlock.End();
                }

                item.UpdatePowerUse();
                item.ProcessDisable();
            }
        }

        private void StepGrind(ShipyardItem shipyardItem)
        {
            var targetsToRedraw = new HashSet<BlockTarget>();
            //we need to multiply this by MyShipGrinderConstants.GRINDER_AMOUNT_PER_SECOND / 2... which evaluates to 1
            float grindAmount = MyAPIGateway.Session.GrinderSpeedMultiplier * shipyardItem.Settings.GrindMultiplier;
            if (shipyardItem.TargetBlocks.Count == 0)
            {
                Profiler.ProfilingBlock targetblock = Profiler.Start(FullName, nameof(StepGrind), "Populate target blocks");
                var blocks = new List<IMySlimBlock>();
                Logging.Instance.WriteLine(shipyardItem.YardGrids.Count.ToString());
                foreach (IMyCubeGrid yardGrid in shipyardItem.YardGrids.Where(g => g.Physics != null)) //don't process projections
                {
                    var tmpBlocks = new List<IMySlimBlock>();
                    yardGrid.GetBlocks(tmpBlocks);
                    blocks.AddRange(tmpBlocks);
                }

                foreach (IMySlimBlock tmpBlock in blocks)
                    shipyardItem.TargetBlocks.Add(new BlockTarget(tmpBlock, shipyardItem));
                targetblock.End();
            }
            List<BlockTarget> allBlocks = shipyardItem.TargetBlocks.ToList();

            if (allBlocks.Count == 0)
                return;

            if (!shipyardItem.ProxDict.Any())
            {
                //sort blocks by distance to each tool
                foreach (IMyCubeBlock tool in shipyardItem.Tools)
                {
                    Profiler.ProfilingBlock targetSortBlock = Profiler.Start(FullName, nameof(StepGrind), "Sort Targets");
                    List<BlockTarget> sortTargets = allBlocks.ToList();
                    sortTargets.Sort((a, b) => a.ToolDist[tool.EntityId].CompareTo(b.ToolDist[tool.EntityId]));
                    shipyardItem.ProxDict[tool.EntityId] = sortTargets;
                    targetSortBlock.End();
                }
            }

            Profiler.ProfilingBlock targetFindBlock = Profiler.Start(FullName, nameof(StepGrind), "Find Targets");
            foreach (IMyCubeBlock tool in shipyardItem.Tools)
            {
                if (tool.Closed || tool.MarkedForClose)
                {
                    //this is bad
                    shipyardItem.Disable();
                    return;
                }

                BlockTarget[] blockArray = shipyardItem.BlocksToProcess[tool.EntityId];

                //find the next target for each grinder, if it needs one
                for (int i = 0; i < shipyardItem.Settings.BeamCount; i++)
                {
                    var toRemove = new HashSet<BlockTarget>();
                    if (blockArray[i] != null)
                        continue;

                    BlockTarget nextTarget = null;

                    for (int b = 0; b < shipyardItem.ProxDict[tool.EntityId].Count; b++)
                    {
                        nextTarget = shipyardItem.ProxDict[tool.EntityId][b];

                        if (nextTarget.CubeGrid.Closed || nextTarget.CubeGrid.MarkedForClose)
                            continue;

                        //one grinder per block, please
                        bool found = false;
                        foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
                        {
                            foreach (BlockTarget target in entry.Value)
                            {
                                if (target == null)
                                    continue;

                                if (target == nextTarget)
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (found)
                        {
                            toRemove.Add(nextTarget);
                            continue;
                        }

                        targetsToRedraw.Add(nextTarget);
                        break;
                    }

                    foreach (BlockTarget removeTarget in toRemove)
                    {
                        shipyardItem.ProxDict[tool.EntityId].Remove(removeTarget);
                        shipyardItem.TargetBlocks.Remove(removeTarget);
                    }

                    //we found a block to pair with our grinder, add it to the dictionary and carry on with destruction
                    if (nextTarget != null)
                    {
                        shipyardItem.BlocksToProcess[tool.EntityId][i] = nextTarget;
                    }
                }
            }
            targetFindBlock.End();
            shipyardItem.UpdatePowerUse();
            Profiler.ProfilingBlock grindActionBlock = Profiler.Start(FullName, nameof(StepGrind), "Grind action");
            var removeTargets = new List<BlockTarget>();
            //do the grinding
            Utilities.InvokeBlocking(() =>
                                     {
                                         foreach (IMyCubeBlock tool in shipyardItem.Tools)
                                         {
                                             for (int b = 0; b < shipyardItem.BlocksToProcess[tool.EntityId].Length; b++)
                                             {
                                                 BlockTarget target = shipyardItem.BlocksToProcess[tool.EntityId][b];
                                                 if (target == null)
                                                     continue;

                                                 if (target.CubeGrid.Closed || target.CubeGrid.MarkedForClose)
                                                 {
                                                     Logging.Instance.WriteDebug("Error in grind action: Target closed");
                                                     removeTargets.Add(target);
                                                     return;
                                                 }

                                                 if (targetsToRedraw.Contains(target))
                                                 {
                                                     var toolLine = new Communication.ToolLineStruct
                                                                    {
                                                                        ToolId = tool.EntityId,
                                                                        GridId = target.CubeGrid.EntityId,
                                                                        BlockPos = target.GridPosition,
                                                                        PackedColor = Color.OrangeRed.PackedValue,
                                                                        Pulse = false,
                                                                        EmitterIndex = (byte)b
                                                                    };

                                                     Communication.SendLine(toolLine, shipyardItem.ShipyardBox.Center);
                                                 }

                                                 /*
                                                  * Grinding laser "efficiency" is a float between 0-1 where:
                                                  *   0.0 =>   0% of components recovered
                                                  *   1.0 => 100% of components recovered
                                                  * 
                                                  * Efficiency decays exponentially as distance to the target (length of the "laser") increases
                                                  *     0m => 1.0000
                                                  *    10m => 0.9995
                                                  *    25m => 0.9969
                                                  *    50m => 0.9875
                                                  *   100m => 0.9500
                                                  *   150m => 0.8875
                                                  *   250m => 0.6875
                                                  *   400m => 0.2000
                                                  *    inf => 0.1000
                                                  * We impose a minimum efficiency of 0.1 (10%), which happens at distances > ~450m
                                                  */
                                                 double efficiency = 1 - (target.ToolDist[tool.EntityId] / 200000);
                                                 if (efficiency < 0.1)
                                                     efficiency = 0.1;
                                                 Logging.Instance.WriteDebug(String.Format("Grinder[{0}]block[{1}] distance=[{2:F2}m] efficiency=[{3:F5}]", tool.DisplayNameText, b, Math.Sqrt(target.ToolDist[tool.EntityId]), efficiency));

                                                 if (!shipyardItem.YardGrids.Contains(target.CubeGrid))
                                                 {
                                                     //we missed this grid or its split at some point, so add it to the list and register the split event
                                                     shipyardItem.YardGrids.Add(target.CubeGrid);
                                                     ((MyCubeGrid)target.CubeGrid).OnGridSplit += shipyardItem.OnGridSplit;
                                                 }
                                                 MyInventory grinderInventory = ((MyEntity)tool).GetInventory();

                                                 if (!target.Block.IsFullyDismounted)
                                                 {
                                                     Profiler.ProfilingBlock decreaseBlock = Profiler.Start(FullName, nameof(StepGrind), "DecreaseMountLevel");
                                                     target.Block.DecreaseMountLevel(grindAmount, grinderInventory);
                                                     decreaseBlock.End();
                                                     Profiler.ProfilingBlock inventoryBlock = Profiler.Start(FullName, nameof(StepGrind), "Grind Inventory");
                                                     // First move everything into _tmpInventory
                                                     target.Block.MoveItemsFromConstructionStockpile(_tmpInventory);

                                                     // Then move items into grinder inventory, factoring in our efficiency ratio
                                                     foreach (MyPhysicalInventoryItem item in _tmpInventory.GetItems())
                                                     {
                                                         Logging.Instance.WriteDebug(String.Format("Grinder[{0}]block[{1}] Item[{2}] grind_amt[{3:F2}] collect_amt[{4:F2}]", tool.DisplayNameText, b, item.Content.SubtypeName, item.Amount, (double)item.Amount*efficiency));
                                                         grinderInventory.Add(item, item.Amount * (MyFixedPoint)efficiency);
                                                     }

                                                     // Then clear out everything left in _tmpInventory
                                                     _tmpInventory.Clear();
                                                     inventoryBlock.End();
                                                 }

                                                 // This isn't an <else> clause because target.Block may have become FullyDismounted above,
                                                 // in which case we need to run both code blocks
                                                 if (target.Block.IsFullyDismounted)
                                                 {
                                                     Profiler.ProfilingBlock dismountBlock = Profiler.Start(FullName, nameof(StepGrind), "FullyDismounted");
                                                     var tmpItemList = new List<MyPhysicalInventoryItem>();
                                                     var blockEntity = target.Block.FatBlock as MyEntity;
                                                     if (blockEntity != null && blockEntity.HasInventory)
                                                     {
                                                         Profiler.ProfilingBlock dismountInventory = Profiler.Start(FullName, nameof(StepGrind), "DismountInventory");
                                                         for (int i = 0; i < blockEntity.InventoryCount; ++i)
                                                         {
                                                             MyInventory blockInventory = blockEntity.GetInventory(i);
                                                             if (blockInventory == null)
                                                                 continue;

                                                             if (blockInventory.Empty())
                                                                 continue;

                                                             tmpItemList.Clear();
                                                             tmpItemList.AddRange(blockInventory.GetItems());

                                                             foreach (MyPhysicalInventoryItem item in tmpItemList)
                                                             {
                                                                 Logging.Instance.WriteDebug(String.Format("Grinder[{0}]block[{1}] Item[{2}] inventory[{3:F2}] collected[{4:F2}]", tool.DisplayNameText, b, item.Content.SubtypeName, item.Amount, (double)item.Amount * efficiency));
                                                                 blockInventory.Remove(item, item.Amount);
                                                                 grinderInventory.Add(item, item.Amount * (MyFixedPoint)efficiency);
                                                             }
                                                         }
                                                         dismountInventory.End();
                                                     }
                                                     target.Block.SpawnConstructionStockpile();
                                                     target.CubeGrid.RazeBlock(target.GridPosition);
                                                     removeTargets.Add(target);
                                                     shipyardItem.TargetBlocks.Remove(target);
                                                     dismountBlock.End();
                                                 }
                                             }
                                         }
                                     });

            foreach (KeyValuePair<long, List<BlockTarget>> entry in shipyardItem.ProxDict)
            {
                foreach (BlockTarget removeBlock in removeTargets)
                    entry.Value.Remove(removeBlock);
            }

            foreach (BlockTarget removeBlock in removeTargets)
                shipyardItem.TargetBlocks.Remove(removeBlock);

            //shipyardItem.ActiveTargets = 0;
            //clear lines for any destroyed blocks and update our target count
            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    BlockTarget removeBlock = entry.Value[i];

                    if (removeTargets.Contains(removeBlock))
                    {
                        Communication.ClearLine(entry.Key, i);
                        entry.Value[i] = null;
                    }

                    //if (!removeTargets.Contains(removeBlock) && removeBlock != null)
                    //{
                    //    shipyardItem.ActiveTargets++;
                    //}
                }
            }

            grindActionBlock.End();
        }

        private bool StepWeld(ShipyardItem shipyardItem)
        {
            var targetsToRemove = new HashSet<BlockTarget>();
            var targetsToRedraw = new HashSet<BlockTarget>();

            //we need to multiply this by MyShipWelder.WELDER_AMOUNT_PER_SECOND / 2, which also evaluates to 1
            float weldAmount = MyAPIGateway.Session.WelderSpeedMultiplier * shipyardItem.Settings.WeldMultiplier;
            float boneAmount = weldAmount * .1f;

            if (shipyardItem.TargetBlocks.Count == 0)
            {
                Profiler.ProfilingBlock sortBlock = Profiler.Start(FullName, nameof(StepWeld), "Sort Targets");
                shipyardItem.TargetBlocks.Clear();
                shipyardItem.ProxDict.Clear();
                //precalculate all the BlockTarget for our target grids to speed things up
                var gridTargets = new Dictionary<long, List<BlockTarget>>();
                foreach (IMyCubeGrid targetGrid in shipyardItem.YardGrids)
                {
                    if (targetGrid.Closed || targetGrid.MarkedForClose)
                        continue;

                    var tmpBlocks = new List<IMySlimBlock>();
                    targetGrid.GetBlocks(tmpBlocks);

                    gridTargets.Add(targetGrid.EntityId, new List<BlockTarget>(tmpBlocks.Count));
                    foreach (IMySlimBlock block in tmpBlocks.ToArray())
                    {
                        if (block == null || (targetGrid.Physics != null && block.IsFullIntegrity && !block.HasDeformation))
                            continue;

                        var target = new BlockTarget(block, shipyardItem);

                        shipyardItem.TargetBlocks.Add(target);

                        gridTargets[targetGrid.EntityId].Add(target);
                    }
                    /*
                    var proj = targetGrid.Projector();
                    if (proj != null && !shipyardItem.YardGrids.Contains(proj.CubeGrid))
                    {
                        proj.CubeGrid.GetBlocks(tmpBlocks);
                        gridTargets.Add(proj.CubeGrid.EntityId,new List<BlockTarget>());
                        foreach (var block in tmpBlocks.ToArray())
                        {
                            if (block == null || (targetGrid.Physics != null && block.IsFullIntegrity && !block.HasDeformation))
                                continue;

                            var pos = block.GetPosition();
                            if (!shipyardItem.ShipyardBox.Contains(ref pos))
                                continue;

                            var target = new BlockTarget(block, shipyardItem);

                            shipyardItem.TargetBlocks.Add(target);
                            gridTargets[block.CubeGrid.EntityId].Add(target);
                        }
                    }
                    */
                }
                int count = 0;
                foreach (KeyValuePair<long, List<BlockTarget>> entry in gridTargets)
                    count += entry.Value.Count;
                
                foreach (IMyCubeBlock tool in shipyardItem.Tools)
                {
                    shipyardItem.ProxDict.Add(tool.EntityId, new List<BlockTarget>());
                    //first sort grids by distance to tool
                    shipyardItem.YardGrids.Sort((a, b) =>
                                                    Vector3D.DistanceSquared(a.Center(), tool.GetPosition()).CompareTo(
                                                        Vector3D.DistanceSquared(b.Center(), tool.GetPosition())));

                    foreach (IMyCubeGrid grid in shipyardItem.YardGrids)
                    {
                        //then sort blocks by distance to grid center
                        List<BlockTarget> list = gridTargets[grid.EntityId];
                        list.Sort((a, b) => a.CenterDist.CompareTo(b.CenterDist));

                        shipyardItem.ProxDict[tool.EntityId].AddRange(list);
                    }
                }
                sortBlock.End();
            }

            //nothing to do
            if (shipyardItem.TargetBlocks.Count == 0)
                return false;
            
            //assign blocks to our welders
            foreach (IMyCubeBlock welder in shipyardItem.Tools)
            {
                for (int i = 0; i < shipyardItem.Settings.BeamCount; i++)
                {
                    if (shipyardItem.BlocksToProcess[welder.EntityId][i] != null)
                        continue;

                    var toRemove = new List<BlockTarget>();
                    BlockTarget nextTarget = null;

                    foreach (BlockTarget target in shipyardItem.ProxDict[welder.EntityId])
                    {
                        bool found = false;
                        //one block per tool
                        foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
                        {
                            if (entry.Value.Contains(target))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found)
                        {
                            toRemove.Add(target);
                            continue;
                        }

                        if (target.Projector != null)
                        {
                            BuildCheckResult res = target.Projector.CanBuild(target.Block, false);
                            if (res == BuildCheckResult.AlreadyBuilt)
                            {
                                target.UpdateAfterBuild();
                            }
                            else if (res != BuildCheckResult.OK)
                            {
                                continue;
                            }
                            else
                            {
                                bool success = false;
                                Utilities.InvokeBlocking(() => success = BuildTarget(target, shipyardItem, welder));
                                if (!success)
                                {
                                    //toRemove.Add(target);
                                    continue;
                                }
                                target.UpdateAfterBuild();
                            }
                        }
                        if (target.Block.IsFullIntegrity && !target.Block.HasDeformation)
                        {
                            toRemove.Add(target);
                            continue;
                        }
                        nextTarget = target;
                        break;
                    }

                    if (nextTarget != null)
                    {
                        targetsToRedraw.Add(nextTarget);
                        shipyardItem.BlocksToProcess[welder.EntityId][i] = nextTarget;
                    }
                    
                    foreach (BlockTarget removeTarget in toRemove)
                    {
                        shipyardItem.ProxDict[welder.EntityId].Remove(removeTarget);
                        shipyardItem.TargetBlocks.Remove(removeTarget);
                    }
                }
            }
            
            //update lasers
            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    BlockTarget targetBlock = entry.Value[i];

                    if (targetBlock == null || _stalledTargets.Contains(targetBlock))
                        continue;

                    if (targetsToRedraw.Contains(targetBlock))
                    {
                        var toolLine = new Communication.ToolLineStruct
                                       {
                                           ToolId = entry.Key,
                                           GridId = targetBlock.CubeGrid.EntityId,
                                           BlockPos = targetBlock.GridPosition,
                                           PackedColor = Color.DarkCyan.PackedValue,
                                           Pulse = false,
                                           EmitterIndex = (byte)i
                                       };
                        
                        Communication.SendLine(toolLine, shipyardItem.ShipyardBox.Center);
                    }
                }
            }

            if (shipyardItem.BlocksToProcess.All(e => e.Value.All(t => t == null)))
                return false;

            shipyardItem.UpdatePowerUse();
            targetsToRedraw.Clear();

            Utilities.InvokeBlocking(() =>
                                     {
                                         foreach (IMyCubeBlock welder in shipyardItem.Tools)
                                         {
                                             var tool = (IMyCollector)welder;
                                             MyInventory welderInventory = ((MyEntity)tool).GetInventory();
                                             int i = 0;
                                             foreach (BlockTarget target in shipyardItem.BlocksToProcess[tool.EntityId])
                                             {
                                                 if (target == null)
                                                     continue;
                                                 
                                                 if (target.CubeGrid.Physics == null || target.CubeGrid.Closed || target.CubeGrid.MarkedForClose)
                                                 {
                                                     targetsToRemove.Add(target);
                                                     continue;
                                                 }

                                                 if (!MyAPIGateway.Session.CreativeMode)
                                                 {
                                                     /*
                                                      * Welding laser "efficiency" is a float between 0-1 where:
                                                      *   0.0 =>   0% of component stock used for construction (100% loss)
                                                      *   1.0 => 100% of component stock used for construction (0% loss)
                                                      * 
                                                      * Efficiency decay/distance formula is the same as above for grinder
                                                      */
                                                     double efficiency = 1 - (target.ToolDist[tool.EntityId] / 200000);
                                                     if (efficiency < 0.1)
                                                         efficiency = 0.1;
                                                     Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] distance=[{2:F2}m] efficiency=[{3:F5}]", tool.DisplayNameText, i, Math.Sqrt(target.ToolDist[tool.EntityId]), efficiency));
                                                     /*
                                                      * We have to factor in our efficiency ratio before transferring to the block "construction stockpile",
                                                      * but that math isn't nearly as easy as it was with the grinder.
                                                      * 
                                                      * For each missing component type, we know how many items are "missing" from the construction model, M
                                                      * 
                                                      * The simplest approach would be to pull M items from the conveyor system (if enabled), then move
                                                      * (M*efficiency) items to the "construction stockpile" and vaporize the other (M*(1-efficiency)) items.
                                                      * However, this approach would leave the construction stockpile incomplete and require several iterations
                                                      * before M items have actually been copied.
                                                      * 
                                                      * A better solution is to pull enough "extra" items from the conveyors that the welder has enough to
                                                      * move M items to the construction stockpile even after losses due to distance inefficiency
                                                      *
                                                      * For example, if the target block is missing M=9 items and we are running at 0.9 (90%) efficiency,
                                                      * ideally that means we should pull 10 units, vaporize 1, and still have 9 for construction. However,
                                                      * if the conveyor system was only able to supply us with 2 components, we should not continue to blindly
                                                      * vaporize 1 unit.
                                                      * 
                                                      * Instead, we have to consult the post-conveyor-pull welder inventory to determine if it has at least
                                                      * the required number of components.  If it does, we vaporize M*(1-efficiency).  Otherwise we only
                                                      * vaporize current_count*(1-efficiency) and transfer the rest to the construction stockpile
                                                      */
                                                     var missingComponents = new Dictionary<string, int>();
                                                     target.Block.GetMissingComponents(missingComponents);

                                                     var wasteComponents = new Dictionary<string, int>();
                                                     foreach (KeyValuePair<string, int> entry in missingComponents)
                                                     {
                                                         var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), entry.Key);
                                                         int missing = entry.Value;
                                                         MyFixedPoint totalRequired = (MyFixedPoint)(missing / efficiency);

                                                         MyFixedPoint currentStock = welderInventory.GetItemAmount(componentId);

                                                         Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] Component[{2}] missing[{3:F3}] inefficiency requires[{4:F3}] in_stock[{5:F3}]", tool.DisplayNameText, i, entry.Key, missing, totalRequired, currentStock));
                                                         if (currentStock < totalRequired && tool.UseConveyorSystem)
                                                         {
                                                             // Welder doesn't have totalRequired, so try to pull the difference from conveyors
                                                             welderInventory.PullAny(shipyardItem.ConnectedCargo, entry.Key, (int)Math.Ceiling((double)(totalRequired - currentStock)));
                                                             currentStock = welderInventory.GetItemAmount(componentId);
                                                             Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] Component[{2}] - after conveyor pull - in_stock[{3:F3}]", tool.DisplayNameText, i, entry.Key, currentStock));
                                                         }

                                                         // Now compute the number of components to delete
                                                         // MoveItemsToConstructionPile() below won't move anything if we have less than 1 unit,
                                                         // so don't bother "losing" anything due to ineffeciency
                                                         if (currentStock >= 1)
                                                         {
                                                             // The lesser of (missing, currentStock), times (1 minus) our efficiency fraction
                                                             MyFixedPoint toDelete = MyFixedPoint.Min(MyFixedPoint.Floor(currentStock), missing) * (MyFixedPoint)(1 - efficiency);
                                                             Logging.Instance.WriteDebug(String.Format("Welder[{0}]block[{1}] Component[{2}] amount lost due to distance [{3:F3}]", tool.DisplayNameText, i, entry.Key, toDelete));
                                                             welderInventory.RemoveItemsOfType(toDelete, componentId);
                                                         }
                                                     }

                                                     target.Block.MoveItemsToConstructionStockpile(welderInventory);

                                                     missingComponents.Clear();
                                                     target.Block.GetMissingComponents(missingComponents);
                                                     
                                                     if (missingComponents.Any() && !target.Block.HasDeformation)
                                                     {
                                                         if (_stalledTargets.Add(target))
                                                             targetsToRedraw.Add(target);
                                                     }
                                                     else
                                                     {
                                                         if (_stalledTargets.Remove(target))
                                                             targetsToRedraw.Add(target);
                                                     }

                                                     target.Block.IncreaseMountLevel(weldAmount, 0, welderInventory, boneAmount, true);
                                                     if (target.Block.IsFullIntegrity && !target.Block.HasDeformation)
                                                         targetsToRemove.Add(target);
                                                 }
                                                 i++;
                                             }
                                         }
                                     });
            
            shipyardItem.MissingComponentsDict.Clear();

            foreach (KeyValuePair<long, BlockTarget[]> entry in shipyardItem.BlocksToProcess)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    BlockTarget target = entry.Value[i];

                    if (target == null)
                        continue;

                    if (targetsToRemove.Contains(target))
                    {
                        Communication.ClearLine(entry.Key, i);
                        _stalledTargets.Remove(target);
                        shipyardItem.TargetBlocks.Remove(target);
                        entry.Value[i] = null;
                        continue;
                    }
                    
                    if (_stalledTargets.Contains(target))
                    {
                        var blockComponents = new Dictionary<string, int>();
                        target.Block.GetMissingComponents(blockComponents);

                        foreach (KeyValuePair<string, int> component in blockComponents)
                        {
                            if (shipyardItem.MissingComponentsDict.ContainsKey(component.Key))
                                shipyardItem.MissingComponentsDict[component.Key] += component.Value;
                            else
                                shipyardItem.MissingComponentsDict.Add(component.Key, component.Value);
                        }
                        
                        var toolLine = new Communication.ToolLineStruct
                                       {
                                           ToolId = entry.Key,
                                           GridId = target.CubeGrid.EntityId,
                                           BlockPos = target.GridPosition,
                                           PackedColor = Color.Purple.PackedValue,
                                           Pulse = true,
                                           EmitterIndex = (byte)i
                                       };

                        if (targetsToRedraw.Contains(target))
                        {
                            Communication.ClearLine(entry.Key, i);
                            Communication.SendLine(toolLine, shipyardItem.ShipyardBox.Center);
                        }
                        continue;
                    }
                }
            }

            return true;
        }

        private bool BuildTarget(BlockTarget target, ShipyardItem item, IMyCubeBlock tool)
        {
            IMyProjector projector = target.Projector;
            IMySlimBlock block = target.Block;
            
            if (projector == null || block == null)
                return false;
            
            if (projector.CanBuild(block, false) != BuildCheckResult.OK)
                return false;

            if (MyAPIGateway.Session.CreativeMode)
            {
                projector.Build(block, 0, tool.EntityId, false);
                return projector.CanBuild(block, true) != BuildCheckResult.OK;
            }

            //try to remove the first component from inventory
            string name = ((MyCubeBlockDefinition)block.BlockDefinition).Components[0].Definition.Id.SubtypeName;
            if (_tmpInventory.PullAny(item.ConnectedCargo, name, 2))
            {
                _tmpInventory.Clear();
                projector.Build(block, 0, tool.EntityId, false);
                return projector.CanBuild(block, true) != BuildCheckResult.OK;
            }

            return false;
        }
    }
}