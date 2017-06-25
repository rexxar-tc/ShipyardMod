using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
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
                var gts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

                var blocks = new List<IMyTerminalBlock>();
                gts.GetBlocks(blocks);

                //assume that all the tools are connected, so only check against the first one in the list
                var cornerInventory = (IMyInventory)((MyEntity)item.Tools[0]).GetInventory();

                //check new blocks on the grid
                
                var disconnectedInventories = new HashSet<IMyTerminalBlock>();

                //remove blocks which are closed or no longer in the terminal system
                foreach (var block in item.ConnectedCargo)
                {
                    if (block.Closed || !blocks.Contains(block))
                        disconnectedInventories.Add(block);
                }

                foreach (var dis in disconnectedInventories)
                {
                    item.ConnectedCargo.Remove(dis);
                }

                var newConnections = new HashSet<IMyTerminalBlock>();
                Utilities.InvokeBlocking(() =>
                                         {
                                             //check our cached inventories for connected-ness
                                             foreach (IMyTerminalBlock cargo in item.ConnectedCargo)
                                             {
                                                 if (cornerInventory == null)
                                                     return;

                                                 if (!cornerInventory.IsConnectedTo(((MyEntity)cargo).GetInventory()))
                                                     disconnectedInventories.Add(cargo);
                                             }

                                             foreach (var block in blocks)
                                             {
                                                 //avoid duplicate checks
                                                 if (disconnectedInventories.Contains(block) || item.ConnectedCargo.Contains(block))
                                                     continue;

                                                 //to avoid shipyard corners pulling from each other. Circles are no fun.
                                                 if (block.BlockDefinition.SubtypeName.Contains("ShipyardCorner"))
                                                     continue;

                                                 //ignore reactors
                                                 if (block is IMyReactor)
                                                     continue;

                                                 //ignore oxygen generators and tanks
                                                 if (block is IMyGasGenerator || block is IMyGasTank)
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
                                         });

                foreach (IMyTerminalBlock removeBlock in disconnectedInventories)
                    item.ConnectedCargo.Remove(removeBlock);

                foreach (IMyTerminalBlock newBlock in newConnections)
                    item.ConnectedCargo.Add(newBlock);
            }
        }
    }
}