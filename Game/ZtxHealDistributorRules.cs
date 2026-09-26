using Cosmoteer.Data;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxHealDistributor — the part that hands its banked ztx.healing out to the damaged
    // bio parts around it.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxHealDistributor")]
    internal class ZtxHealDistributorRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public ID<PartComponentRules> Storage;

        [Serialize(Optional = true)]
        [PortableHash]
        public int Radius = 3;

        [Serialize(Optional = true)]
        [PortableHash]
        public int UnitsPerCycle = 10;

        [Serialize(Optional = true)]
        [PortableHash]
        public float Interval = 0.1f;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        public override PartComponent CreateComponent()
        {
            return new ZtxHealDistributor(this);
        }
    }
}
