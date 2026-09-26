using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxPartRotationToggle: a toggle that is ON while the part is placed at Rotation
    // clockwise quarter turns.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxPartRotation")]
    internal class ZtxPartRotationToggleRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public int Rotation;

        public override PartComponent CreateComponent()
        {
            return new ZtxPartRotationToggle(this);
        }
    }
}
