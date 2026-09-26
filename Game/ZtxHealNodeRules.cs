using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxHealNode — a part that spends banked ztx.healing to repair itself.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxHealNode")]
    internal class ZtxHealNodeRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public ID<PartComponentRules> Storage;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<ResourceRules> Resource;

        [Serialize(Optional = true)]
        [PortableHash]
        public int HpPerUnit = 200;

        [Serialize(Optional = true)]
        [PortableHash]
        public int MaxHpPerCycle = 200;

        [Serialize(Optional = true)]
        [PortableHash]
        public float HealInterval = 1.0f;

        [Serialize(Optional = true)]
        [PortableHash]
        public int IntakePriority;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        public override PartComponent CreateComponent()
        {
            return new ZtxHealNode(this);
        }
    }
}
