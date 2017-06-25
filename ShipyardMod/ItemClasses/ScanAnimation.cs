using ShipyardMod.Utility;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ShipyardMod.ItemClasses
{
    public class ScanAnimation
    {
        //each sweep takes 3 seconds (times 60 updates)
        //Lerp takes a percentage, to just take the reciprocal of 3*60
        private const double MULTIPLIER = 1d / 180d;
        private static readonly MyStringId TextureId = MyStringId.GetOrCompute("ScanTexture");
        private readonly Vector4 _color = Color.Green.ToVector4();
        private readonly Vector3D[] _endpoints = new Vector3D[4];

        private readonly ShipyardItem _shipyardItem;
        private bool _init;
        private ScanLine _line;
        private bool _scanningZ = true;
        private int _ticks;

        public ScanAnimation(ShipyardItem item)
        {
            _shipyardItem = item;
        }

        //    OBB corner structure
        //     ZMax    ZMin
        //    0----1  4----5
        //    |    |  |    |
        //    |    |  |    |
        //    3----2  7----6

        private void Init()
        {
            var corners = new Vector3D[8];
            _shipyardItem.ShipyardBox.GetCorners(corners, 0);

            //our endpoints are in the center of the faces
            //z plane
            _endpoints[0] = (corners[0] + corners[1] + corners[2] + corners[3]) / 4;
            _endpoints[1] = (corners[4] + corners[5] + corners[6] + corners[7]) / 4;
            // x plane
            _endpoints[2] = (corners[2] + corners[3] + corners[6] + corners[7]) / 4;
            _endpoints[3] = (corners[0] + corners[1] + corners[4] + corners[5]) / 4;

            /*
             * Scanning Z moves from [0] to [4]
             * Scanning X moves from [0] to [3]
             */

            //start by scanning the line on the z plane, from zmax to zmin
            _line = new ScanLine
                    {
                        Origin = _endpoints[0],
                        //get half the dimensions for each face
                        ZWidth = (float)_shipyardItem.ShipyardBox.HalfExtent.X,
                        ZLength = (float)_shipyardItem.ShipyardBox.HalfExtent.Y,
                        XWidth = (float)_shipyardItem.ShipyardBox.HalfExtent.X,
                        XLength = (float)_shipyardItem.ShipyardBox.HalfExtent.Z,
                        //we need the up and left vectors to align the billboard to the shipyard grid
                        ZLeft = _shipyardItem.ShipyardBox.Orientation.Right,
                        ZUp = -_shipyardItem.ShipyardBox.Orientation.Up,
                        XLeft = -_shipyardItem.ShipyardBox.Orientation.Right,
                        XUp = _shipyardItem.ShipyardBox.Orientation.Forward
                    };
        }

        public bool Draw()
        {
            if (!Update())
                return false;

            //draw the texture oriented to the shipyard grid
            if (_scanningZ)
                MyTransparentGeometry.AddBillboardOriented(TextureId, _color, _line.Origin, _line.ZLeft, _line.ZUp, _line.ZWidth, _line.ZLength);
            else
                MyTransparentGeometry.AddBillboardOriented(TextureId, _color, _line.Origin, _line.XLeft, _line.XUp, _line.XWidth, _line.XLength);

            return true;
        }

        private bool Update()
        {
            if (!_init)
            {
                _init = true;
                Init();
            }

            if (_scanningZ)
            {
                //calculate the next position
                _line.Origin = Vector3D.Lerp(_endpoints[0], _endpoints[1], _ticks * MULTIPLIER);

                //line has reached the end
                //flip the flag so we start scanning the x plane
                if (_ticks * MULTIPLIER >= 1)
                {
                    _ticks = 0;
                    _scanningZ = false;
                }
            }
            else
            {
                _line.Origin = Vector3D.Lerp(_endpoints[2], _endpoints[3], _ticks * MULTIPLIER);

                //line has reached the end
                //we're done, so return false to let the caller know to stop drawing
                if (_ticks * MULTIPLIER >= 1)
                {
                    return false;
                }
            }

            _ticks++;
            return true;
        }

        private class ScanLine
        {
            public Vector3D Origin;
            public Vector3D XLeft;
            public float XLength;
            public Vector3D XUp;
            public float XWidth;
            public Vector3D ZLeft;
            public float ZLength;
            public Vector3D ZUp;
            public float ZWidth;
        }
    }
}