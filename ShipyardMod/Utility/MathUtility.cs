using System;
using System.Collections.Generic;
using System.Linq;
using ShipyardMod.ItemClasses;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ShipyardMod.Utility
{
    public static class MathUtility
    {
        private static readonly Vector3D[] Offsets =
        {
            new Vector3D(0.8103569, 0.2971599, 0.7976569),
            new Vector3D(0.81244801, -0.813483256, -0.312517739),
            new Vector3D(-0.311901606, -0.81946053, 0.802108187)
        };

        private static readonly Vector3D[] SmallOffsets =
        {
            new Vector3D(-0.84227, 0.84227, 0.34794),
            new Vector3D(0.34649, 0.84227, -0.84083),
            new Vector3D(-0.84227, -0.34649, -0.84083),
        };

        /// <summary>
        ///     Determines if a list of 8 Vector3I defines a rectangular prism aligned to the grid
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public static bool ArePointsOrthogonal(List<Vector3I> points)
        {
            //get a list of unique Z values
            int[] zVals = points.Select(p => p.Z).Distinct().ToArray();

            //we should only have two
            if (zVals.Length != 2)
                return false;
            
            //get a list of all points in the two Z planes
            List<Vector3I> zPlane0 = points.FindAll(p => p.Z == zVals[0]);
            List<Vector3I> zPlane1 = points.FindAll(p => p.Z == zVals[1]);

            //we should have four of each
            if (zPlane1.Count != 4 || zPlane0.Count != 4)
                return false;

            //make sure each vertex in the maxZ plane has the same X and Y as only one point in the minZ plane
            foreach (Vector3I zMaxPoint in zPlane0)
            {
                if (zPlane1.Count(zMinPoint => zMinPoint.X == zMaxPoint.X && zMinPoint.Y == zMaxPoint.Y) != 1)
                    return false;
            }

            return true;
        }

        /// <summary>
        ///     Create an OBB that encloses a grid
        /// </summary>
        /// <param name="grid"></param>
        /// <returns></returns>
        public static MyOrientedBoundingBoxD CreateOrientedBoundingBox(IMyCubeGrid grid)
        {
            //quaternion to rotate the box
            Quaternion gridQuaternion = Quaternion.CreateFromForwardUp(
                grid.WorldMatrix.Forward,
                grid.WorldMatrix.Up);

            //get the width of blocks for this grid size
            float blocksize = grid.GridSize;

            //get the halfextents of the grid, then multiply by block size to get world halfextents
            //add one so the line sits on the outside edge of the block instead of the center
            var halfExtents = new Vector3D(
                (Math.Abs(grid.Max.X - grid.Min.X) + 1) * blocksize / 2,
                (Math.Abs(grid.Max.Y - grid.Min.Y) + 1) * blocksize / 2,
                (Math.Abs(grid.Max.Z - grid.Min.Z) + 1) * blocksize / 2);

            return new MyOrientedBoundingBoxD(grid.PositionComp.WorldAABB.Center, halfExtents, gridQuaternion);
        }

        /// <summary>
        ///     Create an OBB from a list of 8 verticies and align it to a grid
        /// </summary>
        /// <param name="grid"></param>
        /// <param name="verticies"></param>
        /// <param name="offset">Allows you to expand or shrink the box by a given number of meters</param>
        /// <returns></returns>
        public static MyOrientedBoundingBoxD CreateOrientedBoundingBox(IMyCubeGrid grid, List<Vector3D> verticies, double offset = 0)
        {
            //create the quaternion to rotate the box around
            Quaternion yardQuaternion = Quaternion.CreateFromForwardUp(
                grid.WorldMatrix.Forward,
                grid.WorldMatrix.Up);

            //find the center of the volume
            var yardCenter = new Vector3D();

            foreach (Vector3D vertex in verticies)
                yardCenter = Vector3D.Add(yardCenter, vertex);

            yardCenter = Vector3D.Divide(yardCenter, verticies.Count);

            //find the dimensions of the box.

            //convert verticies to grid coordinates to find adjoining neighbors
            var gridVerticies = new List<Vector3I>(verticies.Count);

            foreach (Vector3D vertext in verticies)
                gridVerticies.Add(grid.WorldToGridInteger(vertext));

            double xLength = 0d;
            double yLength = 0d;
            double zLength = 0d;

            //finds the length of each axis
            for (int i = 1; i < gridVerticies.Count; ++i)
            {
                if (gridVerticies[0].Y == gridVerticies[i].Y && gridVerticies[0].Z == gridVerticies[i].Z)
                {
                    xLength = Math.Abs(gridVerticies[0].X - gridVerticies[i].X) * grid.GridSize;
                    continue;
                }

                if (gridVerticies[0].X == gridVerticies[i].X && gridVerticies[0].Z == gridVerticies[i].Z)
                {
                    yLength = Math.Abs(gridVerticies[0].Y - gridVerticies[i].Y) * grid.GridSize;
                    continue;
                }

                if (gridVerticies[0].X == gridVerticies[i].X && gridVerticies[0].Y == gridVerticies[i].Y)
                {
                    zLength = Math.Abs(gridVerticies[0].Z - gridVerticies[i].Z) * grid.GridSize;
                }
            }

            var halfExtents = new Vector3D(offset + xLength / 2, offset + yLength / 2, offset + zLength / 2);

            //FINALLY we can make the bounding box
            return new MyOrientedBoundingBoxD(yardCenter, halfExtents, yardQuaternion);
        }

        public static Vector3D CalculateEmitterOffset(IMyCubeBlock tool, byte index)
        {
            if (tool.BlockDefinition.SubtypeId.EndsWith("_Large"))
                return Vector3D.Transform(Offsets[index], tool.WorldMatrix);
            else
                return Vector3D.Transform(SmallOffsets[index], tool.WorldMatrix);
        }

        /// <summary>
        ///     Calculates an array of LineItems that describes an oriented bounding box
        /// </summary>
        /// <param name="obb"></param>
        /// <returns></returns>
        public static LineItem[] CalculateObbLines(MyOrientedBoundingBoxD obb)
        {
            //     ZMax    ZMin
            //    0----1  4----5
            //    |    |  |    |
            //    |    |  |    |
            //    3----2  7----6

            var corners = new Vector3D[8];
            obb.GetCorners(corners, 0);
            var lines = new LineItem[12];

            //ZMax face
            lines[0] = new LineItem(corners[0], corners[1]);
            lines[1] = new LineItem(corners[1], corners[2]);
            lines[2] = new LineItem(corners[2], corners[3]);
            lines[3] = new LineItem(corners[3], corners[0]);

            //ZMin face
            lines[4] = new LineItem(corners[4], corners[5]);
            lines[5] = new LineItem(corners[5], corners[6]);
            lines[6] = new LineItem(corners[6], corners[7]);
            lines[7] = new LineItem(corners[7], corners[4]);

            //vertical lines
            lines[8] = new LineItem(corners[0], corners[4]);
            lines[9] = new LineItem(corners[1], corners[5]);
            lines[10] = new LineItem(corners[2], corners[6]);
            lines[11] = new LineItem(corners[3], corners[7]);

            return lines;
        }

        /// <summary>
        ///     Calculates an array of LineItems that describes a bounding box
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public static LineItem[] CalculateBoxLines(BoundingBoxD box)
        {
            //     ZMax    ZMin
            //    0----1  4----5
            //    |    |  |    |
            //    |    |  |    |
            //    3----2  7----6

            Vector3D[] corners = box.GetCorners();
            var lines = new LineItem[12];

            //ZMax face
            lines[0] = new LineItem(corners[0], corners[1]);
            lines[1] = new LineItem(corners[1], corners[2]);
            lines[2] = new LineItem(corners[2], corners[3]);
            lines[3] = new LineItem(corners[3], corners[0]);

            //ZMin face
            lines[4] = new LineItem(corners[4], corners[5]);
            lines[5] = new LineItem(corners[5], corners[6]);
            lines[6] = new LineItem(corners[6], corners[7]);
            lines[7] = new LineItem(corners[7], corners[4]);

            //vertical lines
            lines[8] = new LineItem(corners[0], corners[4]);
            lines[9] = new LineItem(corners[1], corners[5]);
            lines[10] = new LineItem(corners[2], corners[6]);
            lines[11] = new LineItem(corners[3], corners[7]);

            return lines;
        }

        /// <summary>
        ///     Iterates through Vector3I positions between two points in a straight line
        /// </summary>
        public struct Vector3ILineIterator
        {
            private readonly Vector3I _start;
            private readonly Vector3I _end;
            private readonly Vector3I _direction;

            public Vector3I Current;

            public Vector3ILineIterator(Vector3I start, Vector3I end)
            {
                if (start == end)
                    throw new ArgumentException("Start and end cannot be equal");

                _start = start;
                _end = end;
                Current = start;
                _direction = Vector3I.Clamp(end - start, -Vector3I.One, Vector3I.One);

                if (_direction.RectangularLength() > 1)
                    throw new ArgumentException("Start and end are not in a straight line");
            }

            public bool IsValid()
            {
                return (_end - _start).RectangularLength() >= (Current - _start).RectangularLength();
            }

            public void MoveNext()
            {
                Current += _direction;
            }
        }

        private static readonly double[] SqrtCache = new double[50000];

        /// <summary>
        /// Caching square root method
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public static double Sqrt(int val)
        {
            if (val < 0)
                return double.NaN;

            if (val == 0)
                return 0;

            if (val >= SqrtCache.Length)
                return Math.Sqrt(val);

            if (SqrtCache[val] != default(double))
                return SqrtCache[val];

            SqrtCache[val] = Math.Sqrt(val);
            return SqrtCache[val];
        }

        public static double Sqrt(double val)
        {
            return Sqrt((int)val);
        }

        /// <summary>
        /// Raises a value to the one-half power using our cached sqrt method
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public static double Pow15(double val)
        {
            return val * Sqrt(val);
        }

        public static double MatchShipVelocity(IMyEntity modify, IMyEntity dest, bool recoil)
        {
            // local velocity of dest
            var velTarget = dest.Physics.GetVelocityAtPoint(modify.Physics.CenterOfMassWorld);
            var distanceFromTargetCom = modify.Physics.CenterOfMassWorld - dest.Physics.CenterOfMassWorld;

            var accelLinear = dest.Physics.LinearAcceleration;
            var omegaVector = dest.Physics.AngularVelocity + dest.Physics.AngularAcceleration * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
            var omegaSquared = omegaVector.LengthSquared();
            // omega^2 * r == a
            var accelRotational = omegaSquared * -distanceFromTargetCom;
            var accelTarget = accelLinear + accelRotational;

            var velTargetNext = velTarget + accelTarget * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
            var velModifyNext = modify.Physics.LinearVelocity;// + modify.Physics.LinearAcceleration * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;

            var linearImpulse = modify.Physics.Mass * (velTargetNext - velModifyNext);

            // Angular matching.
            // (dAA*dt + dAV) == (mAA*dt + mAV + tensorInverse*mAI)
            var avelModifyNext = modify.Physics.AngularVelocity + modify.Physics.AngularAcceleration * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
            var angularDV = omegaVector - avelModifyNext;
            var angularImpulse = Vector3.Zero;
            // var angularImpulse = Vector3.TransformNormal(angularDV, modify.Physics.RigidBody.InertiaTensor); not accessible :/

            // based on the large grid, small ion thruster.
            const double wattsPerNewton = (3.36e6 / 288000);
            // based on the large grid gyro
            const double wattsPerNewtonMeter = (0.00003 / 3.36e7);
            // (W/N) * (N*s) + (W/(N*m))*(N*m*s) == W
            var powerCorrectionInJoules = (wattsPerNewton * linearImpulse.Length()) + (wattsPerNewtonMeter * angularImpulse.Length());
            modify.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, linearImpulse, modify.Physics.CenterOfMassWorld, angularImpulse);
            if(recoil)
                dest.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, -linearImpulse, dest.Physics.CenterOfMassWorld, -angularImpulse);

            return powerCorrectionInJoules * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
        }
    }
}