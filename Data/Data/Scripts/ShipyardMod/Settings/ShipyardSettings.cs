using System;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using ShipyardMod.Utility;
using VRage.Serialization;

namespace ShipyardMod.Settings
{
    public enum BuildPatternEnum : byte
    {
        FromProjector,
        FromCenter,
        FromCorners,
    }

    [ProtoContract]
    public struct YardSettingsStruct
    {
        public YardSettingsStruct(long entityId)
        {
            EntityId = entityId;
            BeamCount = 3;
            GuideEnabled = true;
            WeldMultiplier = 0.1f;
            GrindMultiplier = 0.1f;
            AdvancedLocking = false;
            BuildPattern = BuildPatternEnum.FromProjector;
            FillInventory = true;
        }
        
        [ProtoMember]
        public long EntityId;
        [ProtoMember]
        public int BeamCount;
        [ProtoMember]
        public bool GuideEnabled;
        [ProtoMember]
        public float WeldMultiplier;
        [ProtoMember]
        public float GrindMultiplier;
        [ProtoMember]
        public bool AdvancedLocking;
        [ProtoMember]
        public BuildPatternEnum BuildPattern;
        [ProtoMember]
        public bool FillInventory;
    }

    [ProtoContract]
    public struct ShipyardOverrideStruct
    {
        [ProtoMember]
        public int BeamCount;

        [ProtoMember]
        public double PowerMultiplier;

        [ProtoMember]
        public double EfficiencyMultiplier;

        [ProtoMember]
        public bool EnableMobileYard;

        [ProtoMember]
        public int ShipyardMaxSize;
    }

    [XmlInclude(typeof(YardSettingsStruct))]
    public class ShipyardSettings
    {
        private static ShipyardSettings _instance;

        private ShipyardOverrideStruct _overrides;
        public SerializableDictionary<long, YardSettingsStruct> BlockSettings;

        public ShipyardSettings()
        {
            BlockSettings = new SerializableDictionary<long, YardSettingsStruct>();
        }

        public static ShipyardSettings Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                if (!Load())
                    _instance = new ShipyardSettings();

                return _instance;
            }
        }

        public ShipyardOverrideStruct Overrides
        {
            get { return _overrides; }
        }

        public YardSettingsStruct GetYardSettings(long entityId)
        {
            YardSettingsStruct result;
            if (!BlockSettings.Dictionary.TryGetValue(entityId, out result))
            {
                result = new YardSettingsStruct(entityId);
                SetYardSettings(entityId, result);
            }
            return result;
        }

        public void SetYardSettings(long entityId, YardSettingsStruct newSet)
        {
            BlockSettings[entityId] = newSet;
        }

        public void Save()
        {
            Logging.Instance.WriteLine("Saving settings");
            string serialized = MyAPIGateway.Utilities.SerializeToXML(this);
            MyAPIGateway.Utilities.SetVariable("ShipyardSettings", serialized);
            Logging.Instance.WriteLine("Saving overrides");
            string serialize = MyAPIGateway.Utilities.SerializeToXML(_overrides);
            var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("ShipyardOverride.xml", typeof(ShipyardSettings));
            writer.Write(serialize);
            writer.Flush();
            writer.Close();
            Logging.Instance.WriteLine("Done saving settings");
        }

        private static bool Load()
        {
            Logging.Instance.WriteLine("Loading settings");
            try
            {
                string value;
                if (!MyAPIGateway.Utilities.GetVariable("ShipyardSettings", out value))
                {
                    Logging.Instance.WriteLine("Settings do not exist in world file");
                    return false;
                }
                _instance = MyAPIGateway.Utilities.SerializeFromXML<ShipyardSettings>(value);
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage("ShipyardOverride.xml", typeof(ShipyardSettings)))
                {
                    Logging.Instance.WriteLine("Loading overrides");
                    var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("ShipyardOverride.xm", typeof(ShipyardSettings));
                    string ser = reader.ReadToEnd();
                    reader.Close();
                    _instance._overrides = MyAPIGateway.Utilities.SerializeFromXML<ShipyardOverrideStruct>(ser);
                }
                else
                    Logging.Instance.WriteLine("Overrides file not found.");
                Logging.Instance.WriteLine("Done loading settings");
                return true;
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Error loading settings: " + ex);
                return false;
            }
        }
    }
}