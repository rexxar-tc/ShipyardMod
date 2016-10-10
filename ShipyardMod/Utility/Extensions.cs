using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
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

        public static bool PullAny(this MyInventory inventory, HashSet<IMyCubeBlock> sourceInventories, string component, int count)
        {
            return PullAny(inventory, sourceInventories, new Dictionary<string, int> {{component, count}});
        }

        public static bool PullAny(this MyInventory inventory, HashSet<IMyCubeBlock> sourceInventories, Dictionary<string, int> toPull)
        {
            bool result = false;
            foreach (KeyValuePair<string, int> entry in toPull)
            {
                int remainingAmount = entry.Value;
                Logging.Instance.WriteDebug(entry.Key + entry.Value);
                foreach (IMyCubeBlock block in sourceInventories)
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
    }
}