using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxPlacementSnap")]
    internal class ZtxPlacementSnapRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public bool ArteryDrawnRight;

        public override bool HasPhysicalComponent => false;

        public override PartComponent CreateComponent()
        {
            return null;
        }
    }
}
