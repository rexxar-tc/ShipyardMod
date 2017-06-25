using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ShipyardMod.ItemClasses
{
    public class PacketManager
    {
        private readonly List<PacketItem> _packets = new List<PacketItem>();
        private readonly double _spacingSq;
        private bool _init;
        private double _multiplier;
        private double _travelDist;
        public Vector4 Color;
        public Vector3D Origin { get; private set; }
        public Vector3D Target { get; private set; }
        private readonly bool _invert;

        public PacketManager(Vector3D origin, Vector3D target, Vector4 color, double spacing = 10, bool invert = false)
        {
            Origin = origin;
            Target = target;
            Color = color;
            _invert = invert;
            _spacingSq = spacing * spacing;
        }

        public void Update(Vector3D origin, Vector3D target)
        {
            if (_invert)
            {
                Origin = target;
                Target = origin;
            }
            else
            {
                Origin = origin;
                Target = target;
            }
        }

        private void Init()
        {
            _travelDist = Vector3D.Distance(Origin, Target);
            _packets.Add(new PacketItem(Origin));
            //packets move at 20 - 40m/s
            Color.W = 1;
            double speed = Math.Max(20, Math.Min(40, _travelDist / 3));
            _multiplier = 1 / (_travelDist / speed * 60);
        }

        public void DrawPackets()
        {
            UpdatePackets();

            foreach (PacketItem packet in _packets)
            {
                //thanks to Digi for showing me how this thing works
                MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("ShipyardPacket"), Color, packet.Position, 0.3f, packet.Ticks);
            }
        }

        private void UpdatePackets()
        {
            if (!_init)
            {
                _init = true;
                Init();
            }

            var toRemove = new List<PacketItem>();
            foreach (PacketItem packet in _packets)
            {
                packet.Ticks++;
                packet.Position = Vector3D.Lerp(Origin, Target, _multiplier * packet.Ticks);

                //delete the packet once it gets to the destination
                if (_multiplier * packet.Ticks > 1)
                    toRemove.Add(packet);
            }

            foreach (PacketItem removePacket in toRemove)
                _packets.Remove(removePacket);

            //if the last packet to go out is more than 10m from origin, add a new one
            PacketItem lastPacket = _packets.LastOrDefault();
            if (lastPacket != null && Vector3D.DistanceSquared(lastPacket.Position, Origin) > _spacingSq)
                _packets.Add(new PacketItem(Origin));
        }

        private class PacketItem
        {
            public Vector3D Position;
            public int Ticks;

            public PacketItem(Vector3D position)
            {
                Position = position;
                Ticks = 0;
            }
        }
    }
}