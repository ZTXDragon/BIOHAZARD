using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Buffs;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxFlowNode — the mod's own blood-movement component.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxFlowNode")]
    internal class ZtxFlowNodeRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public ID<ResourceRules> Resource;

        [Serialize]
        [PortableHash]
        public string Network = "";

        [Serialize(Optional = true)]
        [PortableHash]
        public bool Directional;

        [Serialize(Optional = true)]
        [PortableHash]
        public OrthogonalRotation Facing = OrthogonalRotation.Up;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool StackPump;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool Diffusion;

        [Serialize(Optional = true)]
        [PortableHash]
        public int RetainVolume;

        [Serialize(Optional = true)]
        [PortableHash]
        public int ReserveVolume;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool PropagatePressure = true;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool FaceOnly;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool AlwaysZeroPressure;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool SystemBridge;

        [Serialize(Optional = true)]
        [PortableHash]
        public FlowRole Role = FlowRole.Pipe;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Storage;

        [Serialize(Optional = true)]
        [PortableHash]
        public int Capacity = 10;

        [Serialize(Optional = true)]
        [PortableHash]
        public int FlowRate = 4;

        [Serialize(Optional = true)]
        [PortableHash]
        public int PressureDecay = 8;

        [Serialize(Optional = true)]
        [PortableHash]
        public int PMax;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<BuffType> PMaxBuff;

        [Serialize(Optional = true)]
        [PortableHash]
        public int BleedRate;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        public override PartComponent CreateComponent()
        {
            return new ZtxFlowNode(this);
        }
    }
}
