using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipyardMod.Utility;
using VRage.Game.ModAPI;
using VRageMath;

namespace ShipyardMod.ItemClasses
{
    public class BlockTarget
    {
        public BlockTarget(IMySlimBlock block, ShipyardItem item)
        {
            Block = block;
            if (CubeGrid.Physics == null && Projector != null)
                CenterDist = Vector3D.DistanceSquared(block.GetPosition(), Projector.GetPosition());
            else
                CenterDist = Vector3D.DistanceSquared(block.GetPosition(), block.CubeGrid.Center());

            ToolDist = new Dictionary<long, double>();
            foreach (IMyCubeBlock tool in item.Tools)
                ToolDist.Add(tool.EntityId, Vector3D.DistanceSquared(block.GetPosition(), tool.GetPosition()));
            var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
            //IntegrityPointsPerSec = MaxIntegrity / BuildTimeSeconds
            //this is much, much faster than pulling the objectbuilder and getting the data from there.
            BuildTime = blockDef.MaxIntegrity / blockDef.IntegrityPointsPerSec;
        }


        public IMyCubeGrid CubeGrid
        {
            get { return Block.CubeGrid; }
        }

        public IMyProjector Projector
        {
            get { return ((MyCubeGrid)Block.CubeGrid).Projector; }
        }

        public Vector3I GridPosition
        {
            get { return Block.Position; }
        }

        public bool CanBuild
        {
            get
            {
                if (CubeGrid.Physics != null)
                    return true;

                return Projector?.CanBuild(Block, false) == BuildCheckResult.OK;
            }
        }

        public IMySlimBlock Block { get; private set; }
        public float BuildTime { get; }
        public double CenterDist { get; }
        public Dictionary<long, double> ToolDist { get; }

        public void UpdateAfterBuild()
        {
            Vector3D pos = Block.GetPosition();
            IMyCubeGrid grid = Projector.CubeGrid;
            Vector3I gridPos = grid.WorldToGridInteger(pos);
            IMySlimBlock newBlock = grid.GetCubeBlock(gridPos);
            if (newBlock != null)
                Block = newBlock;
        }
    }
}