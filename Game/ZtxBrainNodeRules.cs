using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.MediaEffects;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxBrainNode — one modular 1x1 brain block.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxBrainNode")]
    internal class ZtxBrainNodeRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public float CPBase = 5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float CPExponent = 1.679f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float RadiusBase = 6f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float RadiusPerTile = 1.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public Halfling.Geometry.Vector2[] RadarCurve;

        [Serialize(Optional = true)]
        public MultiMediaEffectRules RangeEffect;

        public override PartComponent CreateComponent()
        {
            return new ZtxBrainNode(this);
        }
    }
}
