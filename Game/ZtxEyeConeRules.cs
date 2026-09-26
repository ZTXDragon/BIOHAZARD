using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxEyeCone: directional sight, shaped like a weapon's firing arc instead of the
    // circle every vanilla sensor produces.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxEyeCone")]
    internal class ZtxEyeConeRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public Angle Arc;

        [Serialize]
        [PortableHash]
        public float Range;

        [Serialize(Optional = true)]
        [PortableHash]
        public OrthogonalRotation Facing = OrthogonalRotation.Up;

        [Serialize(Optional = true)]
        [PortableHash]
        public int MaxDiscs = 40;

        [Serialize(Optional = true)]
        [PortableHash]
        public float StartDistance = 10f;

        [Serialize(Optional = true)]
        [PortableHash]
        public int ScanRays = 16;

        [Serialize(Optional = true)]
        [PortableHash]
        public int ScanDistance = 8;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        public override PartComponent CreateComponent()
        {
            return new ZtxEyeCone(this);
        }
    }
}
