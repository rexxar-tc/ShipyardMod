using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.Settings;
using ShipyardMod.Utility;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.ProcessHandlers
{
    public class ProcessShipyardDetection : ProcessHandlerBase
    {
        public static HashSet<ShipyardItem> ShipyardsList = new HashSet<ShipyardItem>();
        private readonly List<IMyCubeBlock> _corners = new List<IMyCubeBlock>();
        private readonly string FullName = typeof(ProcessShipyardDetection).FullName;

        public override int GetUpdateResolution()
        {
            return 5000;
        }

        public override bool ServerOnly()
        {
            return true;
        }

        public override void Handle()
        {
            var tmpEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(tmpEntities);
            if (tmpEntities.Count == 0)
            {
                Logging.Instance.WriteLine("Failed to get list of entities in ShipyardDetection.");
                return;
            }

            //copy the list of entities because concurrency
            IMyEntity[] entities = tmpEntities.ToArray();

            //run through our current list of shipyards and make sure they're still valid
            var itemsToRemove = new HashSet<ShipyardItem>();
            var firstCheckBlock = Profiler.Start(FullName, nameof(Handle), "First Check");
            foreach (ShipyardItem item in ShipyardsList)
            {
                if (!AreToolsConnected(item.Tools))
                {
                    Logging.Instance.WriteLine("remove item tools " + item.Tools.Length);
                    item.Disable();
                    itemsToRemove.Add(item);
                    foreach(var tool in item.Tools)
                        Communication.SendCustomInfo(tool.EntityId, "Invalid Shipyard: All tools must be on the same conveyor network!");
                    continue;
                }

                if (item.Tools.Any(x => x.Closed || x.MarkedForClose))
                {
                    Logging.Instance.WriteLine("remove item closed tools " + item.Tools.Length);
                    item.Disable();
                    itemsToRemove.Add(item);
                    continue;
                }

                if (!entities.Contains(item.YardEntity) || item.YardEntity.Closed || item.YardEntity.MarkedForClose)
                {
                    Logging.Instance.WriteLine("remove item entity");
                    item.Disable();
                    itemsToRemove.Add(item);
                    continue;
                }

                using (Profiler.Start(FullName, nameof(Handle), "Physics Check"))
                {
                    if (item.YardEntity.Physics == null 
                        || item.StaticYard && (!item.YardEntity.Physics.IsStatic || !((IMyCubeGrid)item.YardEntity).IsInVoxels()))
                    {
                        Logging.Instance.WriteLine("remove item physics");
                        itemsToRemove.Add(item);
                        item.Disable();
                        foreach (var tool in item.Tools)
                            Communication.SendCustomInfo(tool.EntityId, "Invalid Shipyard: Shipyard must be anchored to voxels!");
                        continue;
                    }
                }

                if (item.Tools.Any(t => ((IMyTerminalBlock)t).CustomInfo.Contains("Invalid Shipyard")))
                {
                    foreach (var tool in item.Tools)
                        Communication.SendCustomInfo(tool.EntityId, string.Empty);
                }
            }
            firstCheckBlock.End();

            foreach (ShipyardItem item in itemsToRemove)
            {
                item.YardType = ShipyardType.Invalid;
                Communication.SendYardState(item);
                ShipyardsList.Remove(item);
            }

            foreach (IMyEntity entity in entities)
            {
                _corners.Clear();
                var grid = entity as IMyCubeGrid;

                if (grid?.Physics == null || grid.Closed || grid.MarkedForClose )
                    continue;

                if (ShipyardsList.Any(x => x.EntityId == entity.EntityId))
                    continue;

                var gridBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(gridBlocks);

                foreach (IMySlimBlock slimBlock in gridBlocks.ToArray())
                {
                    var collector = slimBlock.FatBlock as IMyCollector;
                    if (collector == null)
                        continue;

                    if (collector.BlockDefinition.SubtypeId.StartsWith("ShipyardCorner"))
                    {
                        _corners.Add(slimBlock.FatBlock);
                    }
                }

                if (_corners.Count != 8)
                {
                    foreach (var tool in _corners)
                        Communication.SendCustomInfo(tool.EntityId, $"Invalid Shipyard: Must be 8 corner blocks, there are {_corners.Count} on this grid!");
                    continue;
                }

                if (_corners.Any(c => c.BlockDefinition.SubtypeId != _corners[0].BlockDefinition.SubtypeId))
                {
                    foreach (var tool in _corners)
                        Communication.SendCustomInfo(tool.EntityId, $"Invalid Shipyard: All 8 corner blocks must be the same type!");
                    continue;
                }
                
                using (Profiler.Start(FullName, nameof(Handle), "Static Check"))
                {
                    if (_corners[0].BlockDefinition.SubtypeId == "ShipyardCorner_Large" && !ShipyardCore.Debug)
                    {
                        if (!grid.IsStatic || !grid.IsInVoxels())
                        {
                            Logging.Instance.WriteDebug($"Yard {grid.EntityId} failed: Static check");
                            foreach (var tool in _corners)
                                Communication.SendCustomInfo(tool.EntityId, "Invalid Shipyard: Shipyard must be anchored to voxels!");
                            continue;
                        }
                    }
                }

                if (!IsYardValid(entity, _corners))
                    continue;

                //add an offset of 2.5m because the corner points are at the center of a 3^3 block, and the yard will be 2.5m short in all dimensions
                MyOrientedBoundingBoxD testBox = MathUtility.CreateOrientedBoundingBox((IMyCubeGrid)entity, _corners.Select(x => x.GetPosition()).ToList(), 2.5);

                Logging.Instance.WriteLine("Found yard");
                var item = new ShipyardItem(
                    testBox,
                    _corners.ToArray(),
                    ShipyardType.Disabled,
                    entity);
                item.Settings = ShipyardSettings.Instance.GetYardSettings(item.EntityId);
                foreach (IMyCubeBlock tool in _corners)
                    item.BlocksToProcess.Add(tool.EntityId, new BlockTarget[3]);

                ShipyardsList.Add(item);
                Communication.SendNewYard(item);
                foreach (var tool in item.Tools)
                    Communication.SendCustomInfo(tool.EntityId, "");
            }
           
            Communication.SendYardCount();
        }

        /// <summary>
        ///     This makes sure all the tools are connected to the same conveyor system
        /// </summary>
        /// <param name="tools"></param>
        /// <returns></returns>
        private bool AreToolsConnected(IReadOnlyList<IMyCubeBlock> tools)
        {
            bool found = true;

            if (tools.Any(x => x.Closed || x.MarkedForClose))
            {
                Logging.Instance.WriteDebug("tools closed?");
                return false;
            }

            Utilities.InvokeBlocking(() =>
                                     {
                                         using (Profiler.Start(FullName, nameof(AreToolsConnected)))
                                         {
                                             IMyInventory toolInventory = ((MyEntity)tools[0]).GetInventory();

                                             if (toolInventory == null)
                                             {
                                                 Logging.Instance.WriteDebug("null toolInventory");
                                                 return;
                                             }

                                             for (int i = 1; i < tools.Count; ++i)
                                             {
                                                 IMyInventory compareInventory = ((MyEntity)tools[i]).GetInventory();

                                                 if (compareInventory == null)
                                                 {
                                                     Logging.Instance.WriteDebug($"Null inventory at {i}");
                                                     found = false;
                                                     return;
                                                 }

                                                 if (!toolInventory.IsConnectedTo(compareInventory))
                                                 {
                                                     Logging.Instance.WriteDebug($"Tool not connected at {i}");
                                                     found = false;
                                                     return;
                                                 }
                                             }
                                         }
                                     });

            return found;
        }

        //    OBB corner structure
        //     ZMax    ZMin
        //    0----1  4----5
        //    |    |  |    |
        //    |    |  |    |
        //    3----2  7----6

        /// <summary>
        ///     Makes sure the shipyard has a complete frame made of shipyard conveyor blocks
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private bool IsFrameComplete(ShipyardItem item)
        {
            using (Profiler.Start(FullName, nameof(IsFrameComplete)))
            {
                var corners = new Vector3D[8];
                item.ShipyardBox.GetCorners(corners, 0);

                var gridCorners = new Vector3I[8];
                for (int i = 0; i < 8; i++)
                    gridCorners[i] = ((IMyCubeGrid)item.YardEntity).WorldToGridInteger(corners[i]);

                LinePair[] lines =
                {
                    new LinePair(0, 1),
                    new LinePair(0, 4),
                    new LinePair(1, 2),
                    new LinePair(1, 5),
                    new LinePair(2, 3),
                    new LinePair(2, 6),
                    new LinePair(3, 0),
                    new LinePair(3, 7),
                    new LinePair(4, 5),
                    new LinePair(5, 6),
                    new LinePair(6, 7),
                    new LinePair(7, 4),
                };

                var grid = (IMyCubeGrid)item.YardEntity;
                foreach (LinePair line in lines)
                {
                    var it = new MathUtility.Vector3ILineIterator(gridCorners[line.Start], gridCorners[line.End]);
                    while (it.IsValid())
                    {
                        IMySlimBlock block = grid.GetCubeBlock(it.Current);
                        it.MoveNext();
                        if (block == null)
                            return false;

                        if (!block.BlockDefinition.Id.SubtypeName.Contains("Shipyard"))
                            return false;

                        if (block.BuildPercent() < .8)
                            return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        ///     Checks if tools are on the same cargo system and are arranged orthogonally.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="tools"></param>
        /// <returns></returns>
        private bool IsYardValid(IMyEntity entity, List<IMyCubeBlock> tools)
        {
            using (Profiler.Start(FullName, nameof(IsYardValid)))
            {
                var gridPoints = new List<Vector3I>();
                foreach (IMyCubeBlock tool in tools)
                {
                    Vector3D point = tool.PositionComp.GetPosition();
                    //the grid position is not consistent with rotation, but world position is always the center of the block
                    //get the Vector3I of the center of the block and calculate APO on that
                    Vector3I adjustedPoint = ((IMyCubeGrid)entity).WorldToGridInteger(point);
                    gridPoints.Add(adjustedPoint);
                }

                if (!MathUtility.ArePointsOrthogonal(gridPoints))
                {
                    Logging.Instance.WriteDebug($"Yard {entity.EntityId} failed: APO");
                    foreach (var tool in tools)
                        Communication.SendCustomInfo(tool.EntityId, "Invalid Shipyard: Corners not aligned!");
                    return false;
                }

                if (!AreToolsConnected(tools))
                {
                    Logging.Instance.WriteDebug($"Yard {entity.EntityId} failed: ATC");
                    foreach (var tool in tools)
                        Communication.SendCustomInfo(tool.EntityId, "Invalid Shipyard: All tools must be on the same conveyor network!");
                    return false;
                }

                return true;
            }
        }

        private struct LinePair
        {
            public LinePair(int start, int end)
            {
                Start = start;
                End = end;
            }

            public readonly int Start;
            public readonly int End;
        }
    }
}