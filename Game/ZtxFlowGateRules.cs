using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxFlowGate — a toggle driven by SYSTEM-WIDE blood fullness (2026-08-24, user
    // request).
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxFlowGate")]
    internal class ZtxFlowGateRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public float StopAbove = 0.85f;

        [Serialize]
        [PortableHash]
        public float ResumeBelow = 0.70f;

        [Serialize(Optional = true)]
        [PortableHash]
        public string Network = "";

        [Serialize(Optional = true)]
        [PortableHash]
        public Cosmoteer.Data.ID<PartComponentRules> ModeToggle;

        public override PartComponent CreateComponent()
        {
            return new ZtxFlowGate(this);
        }
    }
}
