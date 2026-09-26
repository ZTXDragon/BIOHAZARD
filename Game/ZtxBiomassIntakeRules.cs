using Cosmoteer.Data;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxBiomassIntake - how a part that burns biomass takes its share of the biomass
    // network (2026-09-25).
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
