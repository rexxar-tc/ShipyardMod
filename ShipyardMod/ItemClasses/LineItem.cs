using VRageMath;

namespace ShipyardMod.ItemClasses
{
    public class LineItem
    {
        public Vector4 Color;
        public bool Descend;
        public Vector3D End;
        public float FadeVal;
        public byte Index;
        public PacketManager LinePackets;
        public bool Pulse;
        public double PulseVal;
        public Vector3D Start;

        public LineItem(Vector3D start, Vector3D end)
        {
            Start = start;
            End = end;
        }

        public LineItem()
        {
        }
    }
}