using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.ItemClasses;
using ShipyardMod.Utility;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.ProcessHandlers
{
    internal class ProcessLocalYards : ProcessHandlerBase
    {
        public static MyConcurrentHashSet<ShipyardItem> LocalYards = new MyConcurrentHashSet<ShipyardItem>();
        private static readonly string FullName = typeof(ProcessLocalYards).FullName;

        public override int GetUpdateResolution()
        {
            return 500;
        }

        public override bool ServerOnly()
        {
            return false;
        }

        public override bool ClientOnly()
        {
            return true;
        }

        public override void Handle()
        {
            Logging.Instance.WriteDebug("ProcessLocalYards Start");
            var removeYards = new HashSet<ShipyardItem>();

            foreach (ShipyardItem item in LocalYards)
            {
                //see if the shipyard has been deleted
                if (item.YardEntity.Closed || item.YardEntity.Physics == null || item.YardType == ShipyardType.Invalid
                    || (item.StaticYard && !item.YardEntity.Physics.IsStatic))
                {
                    //the client shouldn't tell the server the yard is invalid
                    //item.Disable();
                    removeYards.Add(item);
                    continue;
                }

                if (item.StaticYard)
                    UpdateBoxLines(item);

                //don't draw boxes inside active yards, it's distracting
                if (item.YardType != ShipyardType.Disabled)
                    continue;

                var corners = new Vector3D[8];
                item.ShipyardBox.GetCorners(corners, 0);
                double dist = Vector3D.DistanceSquared(corners[0], item.ShipyardBox.Center);

                var sphere = new BoundingSphereD(item.ShipyardBox.Center, dist);

                //Utilities.InvokeBlocking(()=> entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere));
                List<IMyEntity> entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

                if (entities.Count == 0)
                {
                    Logging.Instance.WriteDebug("Couldn't get entities in ProcessLocalYards");
                    continue;
                }
                var removeGrids = new HashSet<IMyCubeGrid>();
                foreach (IMyEntity entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null)
                        continue;

                    if (grid.EntityId == item.EntityId)
                        continue;

                    //workaround to not blind people with digi's helmet mod
                    if (grid.Physics == null && grid.Projector() == null)
                        continue;

                    if (grid.Closed || grid.MarkedForClose)
                    {
                        removeGrids.Add(grid);
                        continue;
                    }

                    if (LocalYards.Any(x => x.EntityId == grid.EntityId))
                        continue;

                    //create a bounding box around the ship
                    MyOrientedBoundingBoxD gridBox = MathUtility.CreateOrientedBoundingBox(grid);

                    //check if the ship bounding box is completely inside the yard box
                    ContainmentType result = item.ShipyardBox.Contains(ref gridBox);

                    if (result == ContainmentType.Contains)
                    {
                        item.ContainsGrids.Add(grid);
                        item.IntersectsGrids.Remove(grid);
                    }
                    else if (result == ContainmentType.Intersects)
                    {
                        item.IntersectsGrids.Add(grid);
                        item.ContainsGrids.Remove(grid);
                    }
                    else
                    {
                        removeGrids.Add(grid);
                    }
                }

                foreach (IMyCubeGrid containGrid in item.ContainsGrids)
                    if (!entities.Contains(containGrid))
                        removeGrids.Add(containGrid);

                foreach (IMyCubeGrid intersectGrid in item.IntersectsGrids)
                    if (!entities.Contains(intersectGrid))
                        removeGrids.Add(intersectGrid);

                foreach (IMyCubeGrid removeGrid in removeGrids)
                {
                    ShipyardCore.BoxDict.Remove(removeGrid.EntityId);
                    item.ContainsGrids.Remove(removeGrid);
                    item.IntersectsGrids.Remove(removeGrid);
                }
            }

            foreach (ShipyardItem removeItem in removeYards)
            {
                foreach (IMyCubeGrid grid in removeItem.ContainsGrids)
                {
                    ShipyardCore.BoxDict.Remove(grid.EntityId);
                }
                foreach (IMyCubeGrid grid in removeItem.IntersectsGrids)
                {
                    ShipyardCore.BoxDict.Remove(grid.EntityId);
                }

                LocalYards.Remove(removeItem);
            }
        }

        //    OBB corner structure
        //     ZMax    ZMin
        //    0----1  4----5
        //    |    |  |    |
        //    |    |  |    |
        //    3----2  7----6
        /// <summary>
        ///     Updates the internal list of lines so we only draw a laser if there is a frame to contain it
        /// </summary>
        /// <param name="item"></param>
        private void UpdateBoxLines(ShipyardItem item)
        {
            var lineBlock = Profiler.Start(FullName, nameof(UpdateBoxLines));
            var corners = new Vector3D[8];
            item.ShipyardBox.GetCorners(corners, 0);
            var grid = (IMyCubeGrid)item.YardEntity;

            var gridCorners = new Vector3I[8];
            for (int i = 0; i < 8; i++)
                gridCorners[i] = grid.WorldToGridInteger(corners[i]);

            item.BoxLines.Clear();

            //okay, really long unrolled loop coming up, but it's the simplest way to do it
            //zMax face
            if (WalkLine(gridCorners[0], gridCorners[1], grid))
                item.BoxLines.Add(new LineItem(corners[0], corners[1]));
            if (WalkLine(gridCorners[1], gridCorners[2], grid))
                item.BoxLines.Add(new LineItem(corners[1], corners[2]));
            if (WalkLine(gridCorners[2], gridCorners[3], grid))
                item.BoxLines.Add(new LineItem(corners[2], corners[3]));
            if (WalkLine(gridCorners[3], gridCorners[0], grid))
                item.BoxLines.Add(new LineItem(corners[3], corners[0]));
            //zMin face
            if (WalkLine(gridCorners[4], gridCorners[5], grid))
                item.BoxLines.Add(new LineItem(corners[4], corners[5]));
            if (WalkLine(gridCorners[5], gridCorners[6], grid))
                item.BoxLines.Add(new LineItem(corners[5], corners[6]));
            if (WalkLine(gridCorners[6], gridCorners[7], grid))
                item.BoxLines.Add(new LineItem(corners[6], corners[7]));
            if (WalkLine(gridCorners[7], gridCorners[4], grid))
                item.BoxLines.Add(new LineItem(corners[7], corners[4]));
            //connecting lines
            if (WalkLine(gridCorners[0], gridCorners[4], grid))
                item.BoxLines.Add(new LineItem(corners[0], corners[4]));
            if (WalkLine(gridCorners[1], gridCorners[5], grid))
                item.BoxLines.Add(new LineItem(corners[1], corners[5]));
            if (WalkLine(gridCorners[2], gridCorners[6], grid))
                item.BoxLines.Add(new LineItem(corners[2], corners[6]));
            if (WalkLine(gridCorners[3], gridCorners[7], grid))
                item.BoxLines.Add(new LineItem(corners[3], corners[7]));

            lineBlock.End();
        }
        
        private bool WalkLine(Vector3I start, Vector3I end, IMyCubeGrid grid)
        {
            var it = new MathUtility.Vector3ILineIterator(start, end);
            while (it.IsValid())
            {
                IMySlimBlock block = grid.GetCubeBlock(it.Current);
                it.MoveNext();

                if (block == null)
                    return false;

                if (!block.BlockDefinition.Id.SubtypeName.Contains("Shipyard"))
                    return false;

                if (block.BuildPercent() < ((MyCubeBlockDefinition)block.BlockDefinition).CriticalIntegrityRatio)
                    return false;
            }
            return true;
        }
    }
}