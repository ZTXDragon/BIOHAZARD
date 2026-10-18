using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxConscious")]
    internal class ZtxConsciousRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxConscious(this);
        }
    }
}
