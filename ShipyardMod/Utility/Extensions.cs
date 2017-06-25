using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.Utility
{
    public static class Extensions
    {
        public static void Stop(this IMyEntity entity)
        {
            if (entity?.Physics == null || entity.Closed)
                return;

            Utilities.Invoke(() =>
                             {
                                 if (entity.Physics == null || entity.Closed)
                                     return;
                                 entity.Physics.SetSpeeds(Vector3.Zero, Vector3.Zero);
                                 /*
                                 entity.Physics.Clear();
                                 if (!Vector3.IsZero(entity.Physics.LinearAcceleration) || !Vector3.IsZero(entity.Physics.AngularAcceleration))
                                 {
                                     entity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE,
                                                             Vector3.Negate(entity.Physics.LinearAcceleration) * entity.Physics.Mass,
                                                             entity.Center(),
                                                             Vector3.Negate(entity.Physics.AngularAcceleration) * entity.Physics.Mass);
                                 }
                                 */
                             });
        }

        public static Vector3D Center(this IMyEntity ent)
        {
            return ent.WorldAABB.Center;
        }

        public static Vector3D GetPosition(this IMySlimBlock block)
        {
            return block.CubeGrid.GridIntegerToWorld(block.Position);
        }

        public static long SlimId(this IMySlimBlock block)
        {
            return block.CubeGrid.EntityId.GetHashCode() + block.Position.GetHashCode();
        }

        public static bool Closed(this IMySlimBlock block)
        {
            return block.CubeGrid?.GetCubeBlock(block.Position) == null;
        }

        public static IMySlimBlock ProjectionResult(this IMySlimBlock block)
        {
            IMyProjector projector = block.CubeGrid.Projector();
            if (projector == null)
                return null;

            Vector3D pos = block.GetPosition();
            IMyCubeGrid grid = projector.CubeGrid;
            Vector3I gridPos = grid.WorldToGridInteger(pos);
            return grid.GetCubeBlock(gridPos);
        }

        public static IMyProjector Projector(this IMyCubeGrid grid)
        {
            return ((MyCubeGrid)grid).Projector;
        }

        public static float BuildPercent(this IMySlimBlock block)
        {
            return block.Integrity / block.MaxIntegrity;
        }

        public static bool PullAny(this MyInventory inventory, HashSet<IMyTerminalBlock> sourceInventories, string component, int count)
        {
            return PullAny(inventory, sourceInventories, new Dictionary<string, int> {{component, count}});
        }

        public static bool PullAny(this MyInventory inventory, HashSet<IMyTerminalBlock> sourceInventories, Dictionary<string, int> toPull)
        {
            bool result = false;
            foreach (KeyValuePair<string, int> entry in toPull)
            {
                int remainingAmount = entry.Value;
                //Logging.Instance.WriteDebug(entry.Key + entry.Value);
                foreach (IMyTerminalBlock block in sourceInventories)
                {
                    if (block == null || block.Closed)
                        continue;

                    MyInventory sourceInventory;
                    //get the output inventory for production blocks
                    if (((MyEntity)block).InventoryCount > 1)
                        sourceInventory = ((MyEntity)block).GetInventory(1);
                    else
                        sourceInventory = ((MyEntity)block).GetInventory();

                    List<MyPhysicalInventoryItem> sourceItems = sourceInventory.GetItems();
                    if (sourceItems.Count == 0)
                        continue;

                    var toMove = new List<KeyValuePair<MyPhysicalInventoryItem, int>>();
                    foreach (MyPhysicalInventoryItem item in sourceItems)
                    {
                        if (item.Content.SubtypeName == entry.Key)
                        {
                            if (item.Amount <= 0) //KEEEN
                                continue;

                            if (item.Amount >= remainingAmount)
                            {
                                toMove.Add(new KeyValuePair<MyPhysicalInventoryItem, int>(item, remainingAmount));
                                remainingAmount = 0;
                                result = true;
                            }
                            else
                            {
                                remainingAmount -= (int)item.Amount;
                                toMove.Add(new KeyValuePair<MyPhysicalInventoryItem, int>(item, (int)item.Amount));
                                result = true;
                            }
                        }
                    }

                    foreach (KeyValuePair<MyPhysicalInventoryItem, int> itemEntry in toMove)
                    {
                        if (inventory.ComputeAmountThatFits(itemEntry.Key.Content.GetId()) < itemEntry.Value)
                            return false;

                        sourceInventory.Remove(itemEntry.Key, itemEntry.Value);
                        inventory.Add(itemEntry.Key, itemEntry.Value);
                    }

                    if (remainingAmount == 0)
                        break;
                }
            }

            return result;
        }

        public static bool IsInVoxels(this IMyCubeGrid grid)
        {
            if (MyAPIGateway.Session.SessionSettings.StationVoxelSupport)
                return grid.IsStatic;

            // bool result = false;
            /*Utilities.InvokeBlocking(() =>
                                     {

                                         foreach (var block in blocks)
                                         {
                                             //TODO: this is horribly, horribly slow. Must revisit the commented code further down
                                             if (MyCubeGrid.IsInVoxels(((MyCubeGrid)grid).GetCubeBlock(block.Position)))
                                             {
                                                 result = true;
                                                 return;
                                             }
                                         }
                                     });
            */

            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            List<IMyEntity> entities = new List<IMyEntity>();
            var box = grid.PositionComp.WorldAABB;
            Utilities.InvokeBlocking(() =>
                                     {
                                         entities = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref box);
                                         grid.GetBlocks(blocks);
                                     });

            var voxels = entities.Where(e => e is IMyVoxelBase).ToList();

            if (!voxels.Any())
                return false;

            foreach (var block in blocks)
            {
                BoundingBoxD blockBox;
                block.GetWorldBoundingBox(out blockBox);
                var cubeSize = block.CubeGrid.GridSize;
                BoundingBoxD localAAABB = new BoundingBoxD(cubeSize * ((Vector3D)block.Position - 0.5), cubeSize * ((Vector3D)block.Max + 0.5));
                var gridWorldMatrix = block.CubeGrid.WorldMatrix;
                foreach (var map in voxels)
                {
                    if (((IMyVoxelBase)map).IsAnyAabbCornerInside(gridWorldMatrix, localAAABB))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}