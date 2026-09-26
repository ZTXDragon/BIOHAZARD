using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Cosmoteer.Simulation.HitEffects;
using Cosmoteer.Simulation.Overlays;
using Cosmoteer.Ships.Blueprints.Graphics;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxHauler — the organ that does a crew member's hauling job.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxHauler")]
    internal class ZtxHaulerRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableInt Range = 7f;

        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableInt MaxPerHaul = 5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableTime HaulInterval;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool ServeConstruction = true;

        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableFloat BuildWorkPerSecond = 4f;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool ServeSalvage = true;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool ServeMining = false;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool ServeRepair = true;

        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableFloat RepairWorkPerSecond = 4f;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool RepairRequiresStationary = true;

        [Serialize(Optional = true)]
        [PortableHash]
        public float StationarySpeed = 0.5f;

        [Serialize(Optional = true)]
        public MultiHitEffectRules SalvageEffects;

        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableTime SalvageInterval;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool CarryMode;

        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableFloat CarrySpeed = 6f;

        [Serialize(Optional = true)]
        [PortableHash]
        public ModifiableFloat GrabSpeed = 8f;

        [Serialize(Optional = true)]
        [PortableHash]
        public int WorkSlots = 1;

        [Serialize(Optional = true)]
        public CappedLine ReachRingLine;

        static ZtxHaulerRules()
        {
            try
            {
                BlueprintOverlayManager.RegisterOverlay(1100, () => new ZtxReachRingOverlay());
            }
            catch (System.Exception ex)
            {
                Log.Exception("ZtxHaulerRules reach ring registration", ex);
            }
        }

        public override PartComponent CreateComponent()
        {
            return new ZtxHauler(this);
        }
    }
}
