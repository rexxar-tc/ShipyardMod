using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using ParallelTasks;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.Utility;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

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
                
                var data = new ConveyorWork.ConveyorWorkData(item, blocks.Where(b => b.HasInventory), (IMyTerminalBlock)item.Tools[0]);

                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                          {
                                                              for (int i = 0; i < blocks.Count; i++)
                                                                  MyAPIGateway.Parallel.Start(ConveyorWork.DoWork, Callback, data);
                                                          });
            }
        }

        public void Callback(WorkData workData = null)
        {
            var data = workData as ConveyorWork.ConveyorWorkData;
            if (data == null)
                return;

            if (data.QueryBlocks.Count != 0 || data.Pending > 0)
                return;


            data.Item.ConnectedCargo.Clear();

            if (data.Connected.Count == 0)
                return;

            //MyAPIGateway.Utilities.ShowMessage("Callback", $"Added {data.Connected.Count}");

            foreach (IMyTerminalBlock newBlock in data.Connected)
                data.Item.ConnectedCargo.Add(newBlock);
        }

        public static class ConveyorWork
        {
            public static void DoWork(WorkData workData = null)
            {
                var data = workData as ConveyorWorkData;
                if (data == null)
                {
                    //MyAPIGateway.Utilities.ShowMessage("DoWork", "Null data");
                    return;
                }

                IMyTerminalBlock workBlock;
                if (!data.QueryBlocks.TryDequeue(out workBlock))
                {
                    //Utilities.ShowMessage("DoWork", "No work");
                    return;
                }

                Interlocked.Increment(ref data.Pending);

                try
                {
                    IMyInventory compareInv = data.CompareInventory;
                    IMyInventory workInv = workBlock.GetInventory();

                    //if (compareInv == null)
                    //    Utilities.ShowMessage("DoWork", "Null compare");

                    //if(workInv == null)
                    //    Utilities.ShowMessage("DoWork", "Null Work");

                    if (compareInv == null || workInv == null)
                        return;

                    //to avoid shipyard corners pulling from each other. Circles are no fun.
                    if (workBlock.BlockDefinition.SubtypeName.Contains("ShipyardCorner"))
                    {
                        //Utilities.ShowMessage("DoWork", "BlockType C");
                        return;
                    }

                    //ignore reactors
                    if (workBlock is IMyReactor)
                    {
                        //Utilities.ShowMessage("DoWork", "BlockType R");
                        return;
                    }

                    //ignore oxygen generators and tanks
                    if (workBlock is IMyGasGenerator || workBlock is IMyGasTank)
                    {
                        // Utilities.ShowMessage("DoWork", "BlockType G");
                        return;
                    }

                    if (compareInv.IsConnectedTo(workInv))
                        data.Connected.Add(workBlock);
                }
                finally
                {
                    Interlocked.Decrement(ref data.Pending);
                }
            }

            public class ConveyorWorkData : WorkData
            {
                public readonly ShipyardItem Item;
                public readonly MyConcurrentHashSet<IMyTerminalBlock> Connected;
                public readonly MyConcurrentQueue<IMyTerminalBlock> QueryBlocks;
                public readonly IMyInventory CompareInventory;
                public int Pending;

                public ConveyorWorkData(ShipyardItem item, IEnumerable<IMyTerminalBlock> queryBlocks, IMyTerminalBlock compareBlock)
                {
                    Item = item;
                    QueryBlocks = new MyConcurrentQueue<IMyTerminalBlock>();
                    foreach (var b in queryBlocks)
                        QueryBlocks.Enqueue(b);

                    //MyAPIGateway.Utilities.ShowMessage("WorkData", $"Enqueued {QueryBlocks.Count}");

                    Connected = new MyConcurrentHashSet<IMyTerminalBlock>();
                    CompareInventory = compareBlock.GetInventory();
                }
            }
        }
    }
}