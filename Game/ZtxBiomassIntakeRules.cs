using Cosmoteer.Data;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxBiomassIntake")]
    internal class ZtxBiomassIntakeRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public ID<PartComponentRules> Source;

        [Serialize]
        [PortableHash]
        public ID<PartComponentRules> Tank;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        [Serialize(Optional = true)]
        [PortableHash]
        public int UnitsPerCycle = 2;

        public override PartComponent CreateComponent()
        {
            return new ZtxBiomassIntake(this);
        }
    }
}
