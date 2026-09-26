using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Construction;
using Cosmoteer.Ships.Parts.Construction;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.HitEffects;
using Halfling.Serialization.Generic;
using Halfling.Timing;
using ZTX.BioCirculation.Core;
using Vector2 = Halfling.Geometry.Vector2;

namespace ZTX.BioCirculation.Game
{
    // The organ that hauls resources in place of crew.
    internal sealed class ArmTipSlot
    {
        public ResourceTransferJob Job;

        public bool Reaching;

        public Vector2 PrevTip;

        public Vector2 Tip;

        public bool Initialized;

        public bool Homing;
    }

    internal class ZtxHauler : PartComponent
    {
        private ZtxHaulManager _manager;
        private IComponentToggleProvider _toggle;

        private float _cooldown;

        private ResourceTransferJob _claim;

        public ResourceTransferJob Claim => _claim;

        public void SetClaim(ResourceTransferJob job) { _claim = job; }

        private Part _buildClaim;

        public Part BuildClaim => _buildClaim;

        public void SetBuildClaim(Part part) { _buildClaim = part; }

        private SalvageJob _salvageClaim;

        private float _salvageCooldown;

        public SalvageJob SalvageClaim => _salvageClaim;

        public void SetSalvageClaim(SalvageJob job) { _salvageClaim = job; }

        public bool CanSalvage => Rules.ServeSalvage && Rules.SalvageEffects != null;

        public bool CanMine => CanSalvage && Rules.ServeMining;

        public bool CanRepair => Rules.ServeRepair;

        private Part _repairClaim;

        public Part RepairClaim => _repairClaim;

        public void SetRepairClaim(Part site) { _repairClaim = site; }

        private ArmTipSlot[] _slots;

        public ArmTipSlot[] Slots
        {
            get
            {
                int n = Rules.WorkSlots > 0 ? Rules.WorkSlots : 1;
                if (_slots == null || _slots.Length != n)
                {
                    _slots = new ArmTipSlot[n];
                    for (int i = 0; i < n; i++) _slots[i] = new ArmTipSlot();
                }
                return _slots;
            }
        }

        public bool CanCarry => Rules.CarryMode;

        public bool CanBuild => Rules.ServeConstruction && Rules.BuildWorkPerSecond.GetValue(base.Part, 1f) > 0f;

        public new ZtxHaulerRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxHaulerRules>(base.Rules); }
        }

        public ZtxHauler(ZtxHaulerRules rules) : base(rules)
        {
        }

        public bool IsUsable
        {
            get
            {
                Part p = base.Part;
                if (p == null) return false;

                if (p.IsUnderConstruction) return false;

                // Consciousness range: an organ beyond every brain cluster's radius has no
                // "artificial crew" and does nothing. True on ships without brains.
                if (!ZtxBrainManager.IsConscious(p)) return false;

                // No toggle authored means always on. A toggle that resolved to nothing is
                // reported once at attach rather than silently disabling the organ.
                return _toggle == null || _toggle.IsToggleOn;
            }
        }

        public float RangeTiles
        {
            get
            {
                return ReachRing.Tiles(Rules.Range.GetValue(base.Part, 1f));
            }
        }

        public int Tick(float seconds, ZtxHaulManager manager)
        {
            if (!IsUsable) return 0;

            _cooldown -= seconds;
            if (_cooldown > 0f) return 0;

            float interval = (float)Rules.HaulInterval.GetValue(base.Part, 1f).Seconds;
            _cooldown += interval > 0f ? interval : 1f;
            if (_cooldown < 0f) _cooldown = 0f;   // never bank a burst after a stall

            ResourceTransferJob job = manager.FindJobFor(this);
            if (job == null) return 0;

            int moved = StartHaul(job, manager);

            // Release a job we could not draw anything from, so it is offered to another organ
            // next tick instead of being held indefinitely.
            if (moved <= 0) _claim = null;
            return moved;
        }

        public bool TickSalvage(float seconds, ZtxHaulManager manager)
        {
            if (!IsUsable || !CanSalvage) return false;

            _salvageCooldown -= seconds;
            if (_salvageCooldown > 0f) return false;

            SalvageJob job = manager.FindSalvageJobFor(this);
            if (job == null) return false;

            Part target = job.SalvagePart;
            Ship myShip = base.Part?.Ship;
            if (target == null || myShip?.Sim == null) return false;
            if (!target.Rules.IsCrewSalvageable || !target.ExistsInSim(myShip.Sim)) return false;

            float interval = (float)Rules.SalvageInterval.GetValue(base.Part, 1f).Seconds;
            _salvageCooldown += interval > 0f ? interval : 0.1f;
            if (_salvageCooldown < 0f) _salvageCooldown = 0f;

            bool markedDontKill = false;
            if (job.IsDeconstruction)
            {
                ConstructionManager cm = target.Ship?.OptionalConstructionManager;
                if (cm == null || !cm.AllowFullDeconstruction(target))
                {
                    target.Flags |= PartFlags.DontKill;
                    markedDontKill = true;
                }
            }

            Vector2 worldPoint = target.Ship.DetTransformPointToWorld(target.LocalCenter);
            using (HitEffectParams ep = HitEffectParams.Alloc(myShip.Sim, worldPoint))
            {
                ep.HitPart = target;
                ep.HitShip = target.Ship;
                ep.SourceShip = myShip;
                ep.SourcePart = base.Part;
                ep.SourcePlayerIndex = myShip.Metadata.PlayerIndex;
                ep.TargetShip = target.Ship;
                ep.CurrentActionSourcePlayer =
                    new ActionSourcePlayer(myShip.Metadata.PlayerIndex, myShip.Sim.LogicalTime);

                ep.Flags |= job.IsDeconstruction
                    ? HitEffectFlags.IsDeconstruction
                    : HitEffectFlags.DontSpawnUnderlying;

                Rules.SalvageEffects.DoEffect(ep);
            }

            if (markedDontKill) target.Flags &= ~PartFlags.DontKill;
            return true;
        }

        public bool TickBuild(Time dt, ZtxHaulManager manager)
        {
            if (!IsUsable || !CanBuild) return false;

            Part site = manager.FindBuildSiteFor(this);
            if (site == null) return false;

            PartConstructionTracker tracker = PartConstructionTracker.GetConstructionTracker(site);
            if (tracker == null) return false;

            if (tracker.Progress >= tracker.MaxProgress)
            {
                _buildClaim = null;
                return false;
            }

            float rate = Rules.BuildWorkPerSecond.GetValue(base.Part, 1f);
            if (rate <= 0f) return false;

            Ship ship = base.Part.Ship;
            Vector2 worldLoc = ship != null
                ? ship.DetTransformPointToWorld(base.Part.LocalCenter)
                : Vector2.Zero;

            // job: null is supported — the parameter is nullable and is only used to notify a
            // crew job on completion. There is no crew job here.
            tracker.DoConstruction(dt, rate, worldLoc, null);
            return true;
        }

        public bool TickRepair(Time dt, ZtxHaulManager manager)
        {
            if (!IsUsable || !CanRepair) return false;

            Ship ship = base.Part?.Ship;
            if (ship == null) return false;

            // A ship with no physics body cannot report a speed. Pass a negative rather than
            // zero: "I cannot tell how fast we are" must not be read as "we are stopped".
            float speedSq = ship.Physics?.Body != null
                ? ship.Physics.Body.LinearVelocity.LengthSquared()
                : -1f;
            if (!RepairRule.CanRepairNow(Rules.RepairRequiresStationary, speedSq, Rules.StationarySpeed))
                return false;

            Part site = manager.FindRepairSiteFor(this);
            if (site == null) return false;

            PartRepairTracker tracker = PartRepairTracker.GetRepairTracker(site);
            if (tracker == null || !tracker.NeedsRepairWork) return false;

            float rate = Rules.RepairWorkPerSecond.GetValue(base.Part, 1f);
            if (rate <= 0f) return false;

            tracker.DoRepair(dt, rate);
            return true;
        }

        public bool InRange(Part other)
        {
            Part self = base.Part;
            if (self == null || other == null) return false;
            float range = RangeTiles;
            if (range <= 0f) return false;

            Ship mine = self.Ship;
            Ship theirs = other.Ship;
            if (mine == null || theirs == null) return false;

            if (ReferenceEquals(mine, theirs))
                return self.LocalCenter.DistanceSquaredTo(other.LocalCenter) <= range * range;

            Vector2 a = mine.DetTransformPointToWorld(self.LocalCenter);
            Vector2 b = theirs.DetTransformPointToWorld(other.LocalCenter);

            Vector2 s = mine.Scale;
            float scale = s.X < s.Y ? s.X : s.Y;
            return MiningRule.InWorldRange(a.DistanceTo(b), range, scale);
        }

        public bool CanReach(ResourceTransferJob job)
        {
            Part self = base.Part;
            if (self == null) return false;

            Ship ship = self.Ship;
            if (ship == null) return false;

            if (job.Source?.Part?.Ship != ship) return false;
            if (job.Sink?.Ship != ship) return false;

            float range = RangeTiles;
            if (range <= 0f) return false;
            float rangeSq = range * range;

            Vector2 here = self.LocalCenter;

            // Source centre. Safe because we just proved Source.Part is non-null — the default
            // IResourceSource.Center THROWS NotSupportedException when there is no part.
            if (here.DistanceSquaredTo(job.Source.Part.LocalCenter) > rangeSq) return false;

            return here.DistanceSquaredTo(job.Sink.Center) <= rangeSq;
        }

        private int StartHaul(ResourceTransferJob job, ZtxHaulManager manager)
        {
            Part self = base.Part;
            Ship ship = self?.Ship;
            if (ship?.Sim == null) return 0;

            IResourceSource source = job.Source;
            IResourceSink sink = job.Sink;
            if (source?.Part == null || sink == null) return 0;

            int budget = job.ResourcesRequested.Confirmed - job.ResourcesInHand;
            if (budget <= 0) return 0;

            int cap = Rules.MaxPerHaul.GetValue(self, 1f);
            if (cap > 0 && budget > cap) budget = cap;
            if (budget > source.Resources) budget = source.Resources;

            int room = sink.GetRemainingCapacity(job.ResourceType, true);
            foreach (ResourceTransferJob other in
                     ResourceTransferJob.GetTransferJobsFor(sink, MPValueType.Confirmed))
            {
                if (ReferenceEquals(other, job)) continue;
                room -= other.ResourcesRequested.Confirmed - other.ResourcesInHand;
                if (room <= 0) break;
            }
            if (budget > room) budget = room;
            if (budget <= 0)
            {
                if (ZtxHaulDebug.Verbose)
                    Log.Info("carve skip: no netted room at " + ZtxHaulDebug.Sink(sink) +
                             " for " + job.ResourceType);
                return 0;
            }

            // The hard per-nugget ceiling. Never take more than this in one chunk.
            ResourceRules rr = job.ResourceType.GetResourceRules();
            int perNugget = rr != null ? rr.MaxPerNugget : 1;
            if (perNugget < 1) perNugget = 1;

            Vector2 origin = ship.DetTransformPointToWorld(source.Center);

            Vector2 inherit = ship.Physics.Body != null
                ? (Vector2)ship.Physics.Body.GetLinearVelocityFromWorldPoint(origin)
                : Vector2.Zero;

            int moved = 0;
            int index = 0;
            while (budget > 0 && index < MaxNuggetsPerCycle)
            {
                int chunk = budget < perNugget ? budget : perNugget;

                int taken = source.SubtractResources(chunk);
                if (taken <= 0) break;

                Nugget nugget = new Nugget(job.ResourceType, taken, origin,
                                           default(Halfling.Geometry.Direction),
                                           NuggetFlags.BelongsToPlayer, inherit);

                // taken <= perNugget by construction, so the clamp cannot bite. Verified rather
                // than assumed, because when it does bite it destroys resources silently.
                if (nugget.Quantity != taken)
                {
                    Log.Error("ZtxHauler on " + self + ": nugget clamped " + taken + " -> " +
                              nugget.Quantity + " of " + job.ResourceType +
                              " (MaxPerNugget=" + perNugget + "). Units were lost.");
                }

                ship.Sim.Nuggets.AddNugget(nugget);

                job.Cancel(taken, false);
                ResourceTransferJob nuggetJob = ship.Resources.ManualResourceTransfer(
                    nugget, sink, taken, false, TradeType.None, 0,
                    false, true, ship.Jobs.AllocGlobalJobID());

                manager.RegisterCargo(nugget, nuggetJob, sink, source);

                moved += taken;
                budget -= taken;
                index++;
            }

            if (ZtxHaulDebug.Verbose && moved > 0)
                Log.Info("carve: " + moved + " " + job.ResourceType + " " +
                         ZtxHaulDebug.Src(source) + " -> " + ZtxHaulDebug.Sink(sink) +
                         " in " + index + " nugget(s)");
            return moved;
        }

        private const int MaxNuggetsPerCycle = 16;


        public override string GetDebuggerInlineInfo()
        {
            return "range=" + RangeTiles + "t cd=" + _cooldown.ToString("0.00") +
                   (IsUsable ? "" : " [OFF]");
        }

        public override bool HasSaveState => true;

        public override void WriteTo(GenericSerialWriter writer)
        {
            base.WriteTo(writer);
            writer.WriteToPath("Cooldown", _cooldown);
        }

        public override void ReadFrom(GenericSerialReader reader)
        {
            base.ReadFrom(reader);
            _cooldown = reader.ReadOptionalFromPath("Cooldown", 0f);
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();

            if (Rules.Toggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                    Log.Error("ZtxHauler on " + base.Part + ": Toggle=" + Rules.Toggle +
                              " is not a toggle; hauling will run ungated.");
            }

            _manager = ZtxHaulManager.GetOrCreate(base.Ship);
            _manager?.Register(this);

            if (Rules.ServeSalvage && Rules.SalvageEffects == null)
                Log.Error("ZtxHauler on " + base.Part + ": ServeSalvage is true but SalvageEffects " +
                          "is null — salvage is disabled. Check the SalvageEffects reference.");

            Log.Info("hauler attached on " + base.Part + " at " + base.Part.Location +
                     (base.Part.IsUnderConstruction ? " [UNDER CONSTRUCTION]" : " [built]") +
                     " range=" + RangeTiles + "t build=" + Rules.BuildWorkPerSecond.GetValue(base.Part, 1f) +
                     " salvage=" + (Rules.SalvageEffects != null ? "yes" : "NO EFFECTS"));
        }

        public override void OnPartDetaching()
        {
            _manager?.Deregister(this);
            _manager = null;
            _toggle = null;
            _claim = null;
            _buildClaim = null;
            _salvageClaim = null;
            _slots = null;
            base.OnPartDetaching();
        }
    }
}
