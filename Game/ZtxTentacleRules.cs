using Cosmoteer.Ships.Rendering;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.MediaEffects;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxTentacle — persistent, procedurally animated tentacle arms attached to a part.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxTentacle")]
    internal class ZtxTentacleRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Hauler;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        [Serialize(Optional = true)]
        [PortableHash]
        public int TentacleCount = 4;

        [Serialize(Optional = true)]
        [PortableHash]
        public Vector2[] AnchorOffsets;

        [Serialize(Optional = true)]
        [PortableHash]
        public int SegmentCount = 6;

        [Serialize(Optional = true)]
        [PortableHash]
        public float SegmentLength = 0.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float FollowSpeed = 6f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float TipSpeed = 8f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float LimpDamping = 0.96f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float RagdollDrag = 2.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float TipPull = 30f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float SwayAmplitude = 0.35f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float SwaySpeed = 0.8f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float IdleExtension = 0.6f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float BaseWaveFraction = 0.35f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float IdleSweepFraction = 0.28f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float IdleSweepDegrees = 300f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float TravelStraighten = 0.3f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float MaxBendAngle = 45f;

        [Serialize(Optional = true)]
        public MultiMediaEffectRules SegmentEffect;

        [Serialize(Optional = true)]
        public MultiMediaEffectRules TipEffect;

        [Serialize(Optional = true)]
        public AtlasSprite SegmentSprite;

        [Serialize(Optional = true)]
        public AtlasSprite TipSprite;

        [Serialize(Optional = true)]
        public ID<ShipRenderLayerRules> Layer;

        public override IEnumerable<AtlasSprite> GetAtlasSprites(ID<ShipRenderLayerRules> layerID)
        {
            if (layerID != Layer) yield break;
            if (SegmentSprite != null) yield return SegmentSprite;
            if (TipSprite != null) yield return TipSprite;
        }

        public override PartComponent CreateComponent()
        {
            return new ZtxTentacle(this);
        }
    }
}
