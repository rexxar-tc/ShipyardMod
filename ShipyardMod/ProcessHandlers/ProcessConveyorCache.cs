using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.Utility;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace ShipyardMod.ProcessHandlers
{
    public class ProcessConveyorCache : ProcessHandlerBase
    {
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
            foreach (ShipyardItem item in ProcessShipyardDetection.ShipyardsList)
            {
                var grid = (IMyCubeGrid)item.YardEntity;

                if (grid.Physics == null || grid.Closed || item.YardType == ShipyardType.Invalid)
                {
                    item.ConnectedCargo.Clear();
                    continue;
                }

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, x => x.FatBlock != null);

                //assume that all the tools are connected, so only check against the first one in the list
                var cornerInventory = (IMyInventory)((MyEntity)item.Tools[0]).GetInventory();

                //check new blocks on the grid
                var nextLevelBlocks = new HashSet<IMyCubeBlock>();
                foreach (IMySlimBlock slimBlock in blocks)
                {
                    IMyCubeBlock block = slimBlock.FatBlock;

                    if (block.Closed)
                        continue;

                    var pistonBase = block as IMyPistonBase;
                    if (pistonBase != null)
                    {
                        if (!pistonBase.IsAttached || pistonBase.Top == null)
                            continue;

                        var levelBlocks = new List<IMySlimBlock>();
                        pistonBase.TopGrid.GetBlocks(levelBlocks, x => x.FatBlock != null);

                        //I don't care enough to recurse through all connected grids. Maybe once grid groups are a thing
                        foreach (IMySlimBlock levelBlock in levelBlocks)
                            nextLevelBlocks.Add(levelBlock.FatBlock);
                    }

                    var connector = block as IMyShipConnector;
                    if (connector != null)
                    {
                        if (!connector.IsConnected || connector.OtherConnector == null)
                            continue;

                        var levelBlocks = new List<IMySlimBlock>();
                        connector.OtherConnector.CubeGrid.GetBlocks(levelBlocks, x => x.FatBlock != null);

                        foreach (IMySlimBlock levelBlock in levelBlocks)
                            nextLevelBlocks.Add(levelBlock.FatBlock);
                    }

                    var motorRotor = block as IMyMotorRotor;
                    if (motorRotor != null)
                    {
                        if (!motorRotor.IsAttached || motorRotor.Stator == null)
                            continue;

                        var levelBlocks = new List<IMySlimBlock>();
                        motorRotor.Stator.CubeGrid.GetBlocks(levelBlocks, x => x.FatBlock != null);

                        foreach (IMySlimBlock levelBlock in levelBlocks)
                            nextLevelBlocks.Add(levelBlock.FatBlock);
                    }

                    var motorBase = block as IMyMotorBase;
                    if (motorBase != null)
                    {
                        if (!motorBase.IsAttached || motorBase.Rotor == null)
                            continue;

                        var levelBlocks = new List<IMySlimBlock>();
                        motorBase.RotorGrid.GetBlocks(levelBlocks, x => x.FatBlock != null);

                        foreach (IMySlimBlock levelBlock in levelBlocks)
                            nextLevelBlocks.Add(levelBlock.FatBlock);
                    }
                }

                var disconnectedInventories = new HashSet<IMyCubeBlock>();
                var newConnections = new HashSet<IMyCubeBlock>();
                Utilities.InvokeBlocking(() =>
                                         {
                                             //check our cached inventories for connected-ness
                                             foreach (IMyCubeBlock cargo in item.ConnectedCargo)
                                             {
                                                 if (cargo.Closed)
                                                     continue;
                                                 if (cornerInventory == null)
                                                     return;
                                                 if (!cornerInventory.IsConnectedTo(((MyEntity)cargo).GetInventory()))
                                                     disconnectedInventories.Add(cargo);
                                             }

                                             foreach (IMySlimBlock slimBlock in blocks)
                                             {
                                                 if (slimBlock.FatBlock == null)
                                                     continue;

                                                 IMyCubeBlock block = slimBlock.FatBlock;

                                                 //to avoid shipyard corners pulling from each other. Circles are no fun.
                                                 if (block.BlockDefinition.SubtypeName.Contains("ShipyardCorner"))
                                                     continue;

                                                 //ignore reactors
                                                 if (block is IMyReactor)
                                                     continue;

                                                 if (item.ConnectedCargo.Contains(block) || disconnectedInventories.Contains(block))
                                                     continue;

                                                 if (((MyEntity)block).HasInventory)
                                                 {
                                                     MyInventory inventory = ((MyEntity)block).GetInventory();
                                                     if (cornerInventory == null)
                                                         return;
                                                     if (cornerInventory.IsConnectedTo(inventory))
                                                         newConnections.Add(block);
                                                 }
                                             }

                                             foreach (IMyCubeBlock block in nextLevelBlocks)
                                             {
                                                 if (item.ConnectedCargo.Contains(block) || disconnectedInventories.Contains(block))
                                                     continue;

                                                 //to avoid shipyard corners pulling from each other. Circles are no fun.
                                                 if (block.BlockDefinition.SubtypeName.Contains("ShipyardCorner"))
                                                     continue;

                                                 //ignore reactors
                                                 if (block is IMyReactor)
                                                     continue;

                                                 //ignore oxygen generators and tanks
                                                 if (block is IMyOxygenGenerator || block is IMyOxygenTank)
                                                     continue;

                                                 if (((MyEntity)block).HasInventory)
                                                 {
                                                     if (cornerInventory == null)
                                                         return;
                                                     MyInventory inventory = ((MyEntity)block).GetInventory();
                                                     if (cornerInventory.IsConnectedTo(inventory))
                                                         newConnections.Add(block);
                                                 }
                                             }
                                         });
                foreach (IMyCubeBlock removeBlock in disconnectedInventories)
                    item.ConnectedCargo.Remove(removeBlock);

                foreach (IMyCubeBlock newBlock in newConnections)
                    item.ConnectedCargo.Add(newBlock);
            }
        }
    }
}