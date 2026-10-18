using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Construction;
using Cosmoteer.Ships.Parts.Construction;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Scene2D;
using Halfling.Timing;
using ZTX.BioCirculation.Core;
using Vector2 = Halfling.Geometry.Vector2;

namespace ZTX.BioCirculation.Game
{
    internal class ZtxHaulManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, ZtxHaulManager> s_managers =
            new ConditionalWeakTable<Ship, ZtxHaulManager>();

        internal static ZtxHaulManager Find(Ship ship)
        {
            return ship != null && s_managers.TryGetValue(ship, out ZtxHaulManager m) ? m : null;
        }

        internal void TraceState(ref TraceHash h)
        {
            h.Add(_haulers.Count).Add(_supplyTickCounter);
            for (int i = 0; i < _haulers.Count; i++)
            {
                ZtxHauler o = _haulers[i];
                if (o == null) { h.Add(-1); continue; }
                o.TraceState(ref h);
            }
            h.Add(_cargo.Count);
            foreach (KeyValuePair<Nugget, CargoTag> kv in _cargo)
            {
                Nugget n = kv.Key;
                CargoTag t = kv.Value;
                h.Add(n.UniqueID.ToString()).Add(n.Quantity)
                 .Add(n.DetWorldLocation.X).Add(n.DetWorldLocation.Y)
                 .Add(ZtxDesyncTrace.JobKey(t.Job)).Add(t.FailedPasses);
            }
            h.Add(_filedSupplyJobs.Count);
            for (int i = 0; i < _filedSupplyJobs.Count; i++) h.Add(ZtxDesyncTrace.JobKey(_filedSupplyJobs[i]));
            h.Add(_filedChainJobs.Count);
            for (int i = 0; i < _filedChainJobs.Count; i++) h.Add(ZtxDesyncTrace.JobKey(_filedChainJobs[i]));
            h.Add(_candidates.Count);
            for (int i = 0; i < _candidates.Count; i++) h.Add(ZtxDesyncTrace.JobKey(_candidates[i]));
            h.Add(_nuggetJobs.Count);
            for (int i = 0; i < _nuggetJobs.Count; i++) h.Add(ZtxDesyncTrace.JobKey(_nuggetJobs[i]));
        }

        internal string TraceCounts()
        {
            return "organs=" + _haulers.Count + " cargo=" + _cargo.Count + " filed=" + _filedSupplyJobs.Count +
                   "+" + _filedChainJobs.Count + " cand=" + _candidates.Count + " nuggetjobs=" + _nuggetJobs.Count;
        }

        private readonly List<ZtxHauler> _haulers = new List<ZtxHauler>();
        private ZtxHauler[] _snapshot = new ZtxHauler[0];
        private int _snapshotCount;
        private bool _orderDirty = true;

        private readonly List<ResourceTransferJob> _candidates = new List<ResourceTransferJob>();

        private readonly HashSet<ResourceTransferJob> _claimed = new HashSet<ResourceTransferJob>();

        private readonly List<Part> _buildSites = new List<Part>();

        private readonly HashSet<Part> _claimedSites = new HashSet<Part>();

        private readonly List<Part> _repairSites = new List<Part>();

        private readonly HashSet<Part> _claimedRepairs = new HashSet<Part>();

        private readonly List<SalvageJob> _salvageJobs = new List<SalvageJob>();

        private readonly HashSet<SalvageJob> _claimedSalvage = new HashSet<SalvageJob>();

        private readonly List<ResourceTransferJob> _nuggetJobs = new List<ResourceTransferJob>();

        private readonly HashSet<ResourceTransferJob> _claimedCarries = new HashSet<ResourceTransferJob>();

        private readonly List<ResourceTransferJob> _filedSupplyJobs = new List<ResourceTransferJob>();

        private readonly List<ResourceTransferJob> _filedChainJobs = new List<ResourceTransferJob>();

        internal bool HasLiveConstructionSupply(MPValueType valueType, bool pendingPickupOnly)
        {
            for (int i = 0; i < _filedSupplyJobs.Count; i++)
            {
                ResourceTransferJob job = _filedSupplyJobs[i];
                if (job?.Sink == null) continue;
                int floor = pendingPickupOnly ? job.ResourcesInHand : 0;
                if (job.ResourcesRequested.GetValue(valueType) > floor && job.Sink.IsConstructionSink &&
                    !job.GetFinished(valueType, checkSourceValid: true))
                    return true;
            }
            return false;
        }

        private sealed class CargoTag
        {
            public ResourceTransferJob Job;
            public IResourceSink IntendedSink;
            public IResourceSource Origin;
            public bool LoggedParked;
            public bool LoggedHijack;
            public int FailedPasses;
        }

        private const int FallbackGracePasses = 4;

        private readonly Dictionary<Nugget, CargoTag> _cargo = new Dictionary<Nugget, CargoTag>();

        private static readonly ConditionalWeakTable<Nugget, ZtxHaulManager> s_cargoOwners =
            new ConditionalWeakTable<Nugget, ZtxHaulManager>();

        private readonly HashSet<Nugget> _carriedNuggets = new HashSet<Nugget>();

        internal static bool IsTrackedCargo(Nugget n)
        {
            return n != null && s_cargoOwners.TryGetValue(n, out ZtxHaulManager m)
                && m._cargo.ContainsKey(n);
        }

        internal static bool IsBeingCarried(Nugget n)
        {
            return n != null && s_cargoOwners.TryGetValue(n, out ZtxHaulManager m)
                && m._carriedNuggets.Contains(n);
        }

        internal void RegisterCargo(Nugget n, ResourceTransferJob job,
                                    IResourceSink intendedSink, IResourceSource origin)
        {
            if (n == null) return;
            _cargo[n] = new CargoTag { Job = job, IntendedSink = intendedSink, Origin = origin };
            s_cargoOwners.AddOrUpdate(n, this);
        }

        private void ForgetCargo(Nugget n)
        {
            if (n == null) return;
            _cargo.Remove(n);
            s_cargoOwners.Remove(n);
        }

        private const int SupplyInterval = 15;

        private int _supplyTickCounter;
        private int _tickCounter;
        private int _unitsThisWindow;
        private int _haulsThisWindow;
        private int _buildTicksThisWindow;
        private int _repairTicksThisWindow;
        private int _salvagePulsesThisWindow;
        private int _carriedThisWindow;
        private int _supplyJobsThisWindow;

        public ZtxHaulManager()
        {
            base.FixedUpdateBucket = FixedUpdateBuckets.Construction;
            base.FixedUpdatingEnabled = true;
        }

        public static bool HasHaulers(Ship ship)
        {
            return ship != null
                && s_managers.TryGetValue(ship, out ZtxHaulManager m)
                && m._haulers.Count > 0;
        }

        public static ZtxHaulManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out ZtxHaulManager manager)) return manager;

            manager = new ZtxHaulManager();
            ZtxHaulManager captured = manager;
            Ship capturedShip = ship;

            if (ship.Sim != null)
            {
                ship.Sim.EnqueueDeterministic(ship.UniqueID, delegate
                {
                    capturedShip.Components.Add(captured);
                });
            }
            else
            {
                ship.Components.Add(manager);
            }

            s_managers.Add(ship, manager);
            return manager;
        }

        public void Register(ZtxHauler h)
        {
            if (h == null) return;
            _haulers.Add(h);
            _orderDirty = true;
        }

        public void Deregister(ZtxHauler h)
        {
            if (h == null) return;
            if (_haulers.Remove(h)) _orderDirty = true;
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            if (!Config.Enabled || _haulers.Count == 0) return;

            if (_orderDirty)
            {
                _haulers.Sort(CompareByLocation);
                _orderDirty = false;
            }

            if (++_supplyTickCounter >= SupplyInterval)
            {
                _supplyTickCounter = 0;
                MaintainCargo();
                if (AnyHaulerServesConstruction())
                {
                    _supplyJobsThisWindow += ZtxConstructionSupply.Update(base.Ship, _filedSupplyJobs, this);
                }
                _supplyJobsThisWindow += ZtxChainSupply.Update(base.Ship, _filedChainJobs);
            }

            RebuildCandidates();
            RefreshClaims();
            RebuildBuildSites();
            RefreshBuildClaims();
            RebuildRepairSites();
            RefreshRepairClaims();
            RebuildSalvageJobs();
            RefreshSalvageClaims();
            RebuildNuggetJobs();
            RefreshCarries();

            if (_snapshot.Length < _haulers.Count) _snapshot = new ZtxHauler[_haulers.Count * 2];
            _snapshotCount = _haulers.Count;
            _haulers.CopyTo(0, _snapshot, 0, _snapshotCount);

            float seconds = (float)fixedUpdater.Interval.Seconds;

            for (int i = 0; i < _snapshotCount; i++)
            {
                ZtxHauler h = _snapshot[i];
                if (h?.Part == null) continue;
                if (!ReferenceEquals(h.Part.Ship, base.Ship)) continue;

                int moved = h.Tick(seconds, this);
                if (moved > 0)
                {
                    _unitsThisWindow += moved;
                    _haulsThisWindow++;
                }

                if (h.TickBuild(fixedUpdater.Interval, this)) _buildTicksThisWindow++;
                if (h.TickRepair(fixedUpdater.Interval, this)) _repairTicksThisWindow++;
                if (h.TickSalvage(seconds, this)) _salvagePulsesThisWindow++;
                _carriedThisWindow += TickCarry(h, seconds);
            }

            if (++_tickCounter >= 300)
            {
                _tickCounter = 0;
                if (_haulsThisWindow > 0 || _buildTicksThisWindow > 0 || _repairTicksThisWindow > 0 || _salvagePulsesThisWindow > 0 || _carriedThisWindow > 0 || _supplyJobsThisWindow > 0)
                {
                    Log.Info("haul: " + _unitsThisWindow + " unit(s) in " + _haulsThisWindow +
                             " trip(s), " + _buildTicksThisWindow + " build tick(s), " +
                             _repairTicksThisWindow + " repair tick(s), " +
                             _salvagePulsesThisWindow + " salvage pulse(s), " +
                             _carriedThisWindow + " unit(s) carried-delivered across " +
                             _snapshotCount + " organ(s), " + _buildSites.Count + " site(s), " +
                             _salvageJobs.Count + " salvage job(s), " + _nuggetJobs.Count +
                             " nugget job(s), " + _supplyJobsThisWindow + " supply job(s) filed");
                }
                _unitsThisWindow = 0;
                _haulsThisWindow = 0;
                _buildTicksThisWindow = 0;
                _repairTicksThisWindow = 0;
                _salvagePulsesThisWindow = 0;
                _carriedThisWindow = 0;
                _supplyJobsThisWindow = 0;
            }
        }

        private readonly List<Nugget> _cargoScratch = new List<Nugget>();

        private readonly Dictionary<Nugget, ResourceTransferJob> _liveNuggetJobs =
            new Dictionary<Nugget, ResourceTransferJob>();

        private void MaintainCargo()
        {
            if (_cargo.Count == 0) return;
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            _liveNuggetJobs.Clear();
            foreach (ResourceTransferJob j in ResourceTransferJob.GetTransferJobsFor(ship, MPValueType.Confirmed))
            {
                if (j?.Source is Nugget jn && j.ResourcesRequested.Confirmed - j.ResourcesInHand > 0)
                    _liveNuggetJobs[jn] = j;
            }

            _cargoScratch.Clear();
            foreach (KeyValuePair<Nugget, CargoTag> kv in _cargo)
            {
                Nugget n = kv.Key;
                CargoTag tag = kv.Value;

                if (n.Sim != ship.Sim || n.Quantity <= 0) { _cargoScratch.Add(n); continue; }

                if (_liveNuggetJobs.TryGetValue(n, out ResourceTransferJob live))
                {
                    if (!ReferenceEquals(live, tag.Job))
                    {
                        if (ZtxHaulDebug.Verbose)
                            Log.Info("cargo: foreign job adopted on tracked nugget (" +
                                     n.Quantity + " " + n.ResourceType + ") - new intent " +
                                     ZtxHaulDebug.Sink(live.Sink) + " (was " +
                                     ZtxHaulDebug.Sink(tag.IntendedSink) + ")");
                        tag.Job = live;
                        tag.IntendedSink = live.Sink;
                        tag.LoggedHijack = false;
                    }
                    continue;
                }

                tag.Job = null;
                ResourceTransferJob refiled = TryRefileCargo(ship, n, tag.IntendedSink);
                string route = "intended ";
                if (refiled == null)
                {
                    tag.FailedPasses++;
                    if (tag.FailedPasses >= FallbackGracePasses)
                    {
                        IResourceSink alt = FindNearestAlternateSink(ship, n, tag.IntendedSink);
                        if (alt != null)
                        {
                            refiled = TryRefileCargo(ship, n, alt);
                            if (refiled != null)
                            {
                                tag.IntendedSink = alt;
                                route = "nearest alternate ";
                            }
                        }
                        if (refiled == null && tag.Origin is IResourceSink home &&
                            !ReferenceEquals(home, tag.IntendedSink))
                        {
                            refiled = TryRefileCargo(ship, n, home);
                            if (refiled != null) { tag.IntendedSink = home; route = "ORIGIN "; }
                        }
                    }
                }
                if (refiled != null)
                {
                    ZtxDesyncTrace.Event(ship, "refile cargo nugget#" + n.UniqueID + " " + route.Trim() + " " +
                                         ZtxDesyncTrace.JobText(refiled));
                    tag.Job = refiled;
                    tag.LoggedParked = false;
                    tag.FailedPasses = 0;
                    if (ZtxHaulDebug.Verbose)
                        Log.Info("cargo re-route: " + n.Quantity + " " + n.ResourceType +
                                 " -> " + route + ZtxHaulDebug.Sink(tag.IntendedSink));
                }
                else if (ZtxHaulDebug.Verbose && !tag.LoggedParked)
                {
                    tag.LoggedParked = true;
                    Log.Info("cargo PARKED: no room anywhere for " + n.Quantity + " " +
                             n.ResourceType + " (intended " + ZtxHaulDebug.Sink(tag.IntendedSink) +
                             ", origin " + ZtxHaulDebug.Src(tag.Origin) + ") - will retry");
                }
            }
            for (int i = 0; i < _cargoScratch.Count; i++) ForgetCargo(_cargoScratch[i]);
        }

        private static IResourceSink FindNearestAlternateSink(Ship ship, Nugget n, IResourceSink exclude)
        {
            IResourceSink best = null;
            float bestD = float.MaxValue;
            Vector2 local = ship.DetTransformPointFromWorld(n.DetWorldLocation);
            Part excludePart = exclude?.Part;
            foreach (TypedResourceGrid grid in TypedResourceGrid.GetAllOnShip(ship))
            {
                if (grid.ResourceType != n.ResourceType) continue;
                IResourceSink sink = grid;
                if (ReferenceEquals(sink, exclude)) continue;
                if (excludePart != null && grid.Part == excludePart) continue;
                if (!sink.AllowExternalDelivery) continue;
                if (sink.GetRemainingCapacity(n.ResourceType, true) <= 0) continue;
                Part p = grid.Part;
                if (p == null) continue;
                float d = local.DistanceSquaredTo(p.LocalCenter);
                if (d < bestD) { bestD = d; best = sink; }
            }
            return best;
        }

        private static ResourceTransferJob TryRefileCargo(Ship ship, Nugget n, IResourceSink sink)
        {
            if (sink == null || sink.Ship != ship || sink.Sim != ship.Sim) return null;
            int room = sink.GetRemainingCapacity(n.ResourceType, true);
            if (room <= 0) return null;
            foreach (ResourceTransferJob j in ResourceTransferJob.GetTransferJobsFor(sink, MPValueType.Confirmed))
            {
                room -= j.ResourcesRequested.Confirmed - j.ResourcesInHand;
                if (room <= 0) return null;
            }
            int amount = n.Quantity < room ? n.Quantity : room;
            return ship.Resources.ManualResourceTransfer(n, sink, amount, false, TradeType.None,
                                                         0, false, true,
                                                         ship.Jobs.AllocGlobalJobID());
        }

        private void RebuildCandidates()
        {
            _candidates.Clear();
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            foreach (ResourceTransferJob job in ResourceTransferJob.GetTransferJobsFor(ship, MPValueType.Confirmed))
            {
                if (job?.Source == null || job.Sink == null) continue;

                if (job.Source is Nugget) continue;

                if (job.ResourcesRequested.Confirmed - job.ResourcesInHand <= 0) continue;
                if (job.Source.Resources <= 0) continue;
                if (job.Source.Sim != ship.Sim || job.Sink.Sim != ship.Sim) continue;

                if (!job.Sink.AllowExternalDelivery) continue;

                if (job.Sink.IsConstructionSink && !AnyHaulerServesConstruction()) continue;
                if (CollectNuggetTarget.IsSinkBlocked(job.Sink)) continue;

                _candidates.Add(job);
            }

            if (_candidates.Count > 1) _candidates.Sort(CompareJobs);
        }

        internal static bool ServesConstruction(Ship ship)
        {
            return ship != null
                && s_managers.TryGetValue(ship, out ZtxHaulManager m)
                && m.AnyHaulerServesConstruction();
        }

        private bool AnyHaulerServesConstruction()
        {
            for (int i = 0; i < _haulers.Count; i++)
                if (_haulers[i] != null && _haulers[i].Rules.ServeConstruction) return true;
            return false;
        }

        internal bool AnyBuilderCovers(Vector2 source, Vector2 sink)
        {
            for (int i = 0; i < _haulers.Count; i++)
            {
                ZtxHauler h = _haulers[i];
                if (h?.Part == null || !h.Rules.ServeConstruction || !h.IsUsable) continue;
                if (!ReferenceEquals(h.Part.Ship, base.Ship)) continue;
                Vector2 here = h.Part.LocalCenter;
                if (SupplyRule.Covers(here.X, here.Y, h.RangeTiles,
                                      source.X, source.Y, sink.X, sink.Y))
                    return true;
            }
            return false;
        }

        private void RefreshClaims()
        {
            _claimed.Clear();
            for (int i = 0; i < _haulers.Count; i++)
            {
                ZtxHauler h = _haulers[i];
                if (h == null) continue;

                ResourceTransferJob c = h.Claim;
                if (c == null || !h.IsUsable || !IsServiceable(c) || !h.CanReach(c))
                {
                    h.SetClaim(null);
                    continue;
                }
                _claimed.Add(c);
            }
        }

        private static bool IsServiceable(ResourceTransferJob job)
        {
            if (job?.Source == null || job.Sink == null) return false;
            if (job.ResourcesRequested.Confirmed - job.ResourcesInHand <= 0) return false;
            return job.Source.Resources > 0;
        }

        public ResourceTransferJob FindJobFor(ZtxHauler hauler)
        {
            ResourceTransferJob held = hauler.Claim;
            if (held != null && IsServiceable(held) && ZtxHauler.NettedRoom(held) > 0) return held;

            for (int i = 0; i < _candidates.Count; i++)
            {
                ResourceTransferJob job = _candidates[i];
                if (!IsServiceable(job)) continue;
                if (_claimed.Contains(job)) continue;
                if (job.Sink.IsConstructionSink && !hauler.Rules.ServeConstruction) continue;
                if (!hauler.CanReach(job)) continue;
                if (ZtxHauler.NettedRoom(job) <= 0) continue;

                hauler.SetClaim(job);
                _claimed.Add(job);
                return job;
            }

            hauler.SetClaim(null);
            return null;
        }

        private void RebuildBuildSites()
        {
            _buildSites.Clear();
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            foreach (Part p in ship.Parts)
            {
                if (p != null && p.IsUnderConstruction) _buildSites.Add(p);
            }
        }

        private void RebuildRepairSites()
        {
            _repairSites.Clear();
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            ConstructionManager cm = ship.OptionalConstructionManager;
            if (cm == null) return;

            IReadOnlyList<QueuedPartRepair> queued = cm.QueuedPartRepairs;
            for (int i = 0; i < queued.Count; i++)
            {
                QueuedPartRepair q = queued[i];
                Part p = q?.Part;
                if (p == null || p.IsDestroyed) continue;
                if (!ReferenceEquals(p.Ship, ship)) continue;

                PartRepairTracker t = PartRepairTracker.GetRepairTracker(p);
                if (t == null || !t.NeedsRepairWork) continue;
                _repairSites.Add(p);
            }
        }

        private void RefreshRepairClaims()
        {
            _claimedRepairs.Clear();
            for (int i = 0; i < _haulers.Count; i++)
            {
                ZtxHauler h = _haulers[i];
                if (h == null) continue;

                Part c = h.RepairClaim;
                if (c == null || !h.IsUsable || !h.CanRepair || c.IsDestroyed ||
                    !_repairSites.Contains(c) || !h.InRange(c))
                {
                    h.SetRepairClaim(null);
                    continue;
                }
                _claimedRepairs.Add(c);
            }
        }

        public Part FindRepairSiteFor(ZtxHauler hauler)
        {
            Part held = hauler.RepairClaim;
            if (held != null && _repairSites.Contains(held)) return held;

            for (int i = 0; i < _repairSites.Count; i++)
            {
                Part site = _repairSites[i];
                if (site == null || _claimedRepairs.Contains(site)) continue;
                if (!hauler.InRange(site)) continue;

                hauler.SetRepairClaim(site);
                _claimedRepairs.Add(site);
                return site;
            }

            hauler.SetRepairClaim(null);
            return null;
        }

        private void RefreshBuildClaims()
        {
            _claimedSites.Clear();
            for (int i = 0; i < _haulers.Count; i++)
            {
                ZtxHauler h = _haulers[i];
                if (h == null) continue;

                Part c = h.BuildClaim;
                if (c == null || !h.IsUsable || !h.CanBuild || !c.IsUnderConstruction ||
                    !ReferenceEquals(c.Ship, base.Ship) || !h.InRange(c))
                {
                    h.SetBuildClaim(null);
                    continue;
                }
                _claimedSites.Add(c);
            }
        }

        private static bool HasBuildWork(Part site)
        {
            PartConstructionTracker tracker = PartConstructionTracker.GetConstructionTracker(site);
            return tracker != null && tracker.Progress < tracker.MaxProgress;
        }

        public Part FindBuildSiteFor(ZtxHauler hauler)
        {
            Part held = hauler.BuildClaim;
            if (held != null && held.IsUnderConstruction && HasBuildWork(held)) return held;

            for (int i = 0; i < _buildSites.Count; i++)
            {
                Part site = _buildSites[i];
                if (site == null || _claimedSites.Contains(site)) continue;
                if (!HasBuildWork(site)) continue;
                if (!hauler.InRange(site)) continue;

                hauler.SetBuildClaim(site);
                _claimedSites.Add(site);
                return site;
            }

            hauler.SetBuildClaim(null);
            return null;
        }

        private void RebuildSalvageJobs()
        {
            _salvageJobs.Clear();
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            bool anyMiner = false;
            for (int i = 0; i < _haulers.Count && !anyMiner; i++)
                anyMiner = _haulers[i] != null && _haulers[i].CanMine;

            ConstructionManager cm = ship.OptionalConstructionManager;
            if (cm != null)
            {
                IReadOnlyList<QueuedPartDeconstruction> queued = cm.QueuedPartDeconstructions;
                for (int i = 0; i < queued.Count; i++)
                {
                    QueuedPartDeconstruction q = queued[i];
                    if (q?.SalvageJob == null) continue;
                    if (IsSalvageable(q.SalvageJob, ship, false)) _salvageJobs.Add(q.SalvageJob);
                }
            }

            foreach (SalvageJob job in ship.Resources.GetSalvageJobs(MPValueType.Confirmed))
            {
                if (!IsSalvageable(job, ship, anyMiner)) continue;
                if (_salvageJobs.Contains(job)) continue;
                _salvageJobs.Add(job);
            }

            if (_salvageJobs.Count > 1) _salvageJobs.Sort(CompareSalvage);
        }

        private void RefreshSalvageClaims()
        {
            _claimedSalvage.Clear();
            for (int i = 0; i < _haulers.Count; i++)
            {
                ZtxHauler h = _haulers[i];
                if (h == null) continue;

                SalvageJob c = h.SalvageClaim;
                if (c == null || !h.IsUsable || !h.CanSalvage || !IsSalvageable(c, base.Ship, h.CanMine) ||
                    !h.InRange(c.SalvagePart))
                {
                    h.SetSalvageClaim(null);
                    continue;
                }
                _claimedSalvage.Add(c);
            }
        }

        private static bool IsSalvageable(SalvageJob job, Ship ship, bool allowExternal)
        {
            Part p = job?.SalvagePart;
            if (p == null || ship?.Sim == null) return false;
            if (!MiningRule.AcceptsTarget(ReferenceEquals(p.Ship, ship), allowExternal)) return false;
            if (p.Ship == null) return false;
            if (p.IsDestroyed || !p.ExistsInSim(ship.Sim)) return false;
            if (!p.Rules.IsCrewSalvageable) return false;
            return !job.GetFinished(false);
        }

        public SalvageJob FindSalvageJobFor(ZtxHauler hauler)
        {
            bool mines = hauler.CanMine;

            SalvageJob held = hauler.SalvageClaim;
            if (held != null && IsSalvageable(held, base.Ship, mines)) return held;

            for (int i = 0; i < _salvageJobs.Count; i++)
            {
                SalvageJob job = _salvageJobs[i];
                if (!IsSalvageable(job, base.Ship, mines)) continue;
                if (_claimedSalvage.Contains(job)) continue;
                if (!hauler.InRange(job.SalvagePart)) continue;

                hauler.SetSalvageClaim(job);
                _claimedSalvage.Add(job);
                return job;
            }

            hauler.SetSalvageClaim(null);
            return null;
        }

        private static int CompareSalvage(SalvageJob a, SalvageJob b)
        {
            Part pa = a?.SalvagePart, pb = b?.SalvagePart;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = pa.Location.X.CompareTo(pb.Location.X);
            if (c != 0) return c;
            c = pa.Location.Y.CompareTo(pb.Location.Y);
            if (c != 0) return c;

            Ship sa = pa.Ship, sb = pb.Ship;
            if (ReferenceEquals(sa, sb)) return 0;
            if (sa == null) return sb == null ? 0 : 1;
            if (sb == null) return -1;
            c = sa.DetLocation.X.CompareTo(sb.DetLocation.X);
            return c != 0 ? c : sa.DetLocation.Y.CompareTo(sb.DetLocation.Y);
        }

        private void RebuildNuggetJobs()
        {
            _nuggetJobs.Clear();
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            foreach (ResourceTransferJob job in ResourceTransferJob.GetTransferJobsFor(ship, MPValueType.Confirmed))
            {
                if (!IsCarryable(job, ship)) continue;
                if (job.Source is Nugget n && _cargo.TryGetValue(n, out CargoTag tag) &&
                    !ReferenceEquals(job, tag.Job))
                {
                    if (ZtxHaulDebug.Verbose && !tag.LoggedHijack)
                    {
                        tag.LoggedHijack = true;
                        Log.Info("cargo: SKIPPING foreign job on tracked nugget (" +
                                 n.Quantity + " " + n.ResourceType + ") -> " +
                                 ZtxHaulDebug.Sink(job.Sink) + " (tag intends " +
                                 ZtxHaulDebug.Sink(tag.IntendedSink) + ")");
                    }
                    continue;
                }
                _nuggetJobs.Add(job);
            }
            if (_nuggetJobs.Count > 1) _nuggetJobs.Sort(CompareNuggetJobs);
        }

        private static bool IsCarryable(ResourceTransferJob job, Ship ship)
        {
            if (job?.Sink == null) return false;
            if (!(job.Source is Nugget n)) return false;
            if (n.Sim != ship.Sim || n.Quantity <= 0) return false;
            if (job.Sink.Ship != ship || job.Sink.Sim != ship.Sim) return false;
            return job.ResourcesRequested.Confirmed - job.ResourcesInHand > 0;
        }

        private void RefreshCarries()
        {
            _claimedCarries.Clear();
            _carriedNuggets.Clear();
            for (int i = 0; i < _haulers.Count; i++)
            {
                ZtxHauler h = _haulers[i];
                if (h == null) continue;

                ArmTipSlot[] hslots = h.Slots;
                for (int j = 0; j < hslots.Length; j++)
                {
                    ResourceTransferJob c = hslots[j].Job;
                    if (c == null) continue;
                    if (!IsCarryable(c, base.Ship))
                    {
                        if (c.Source is Nugget cn && cn.Sim != null)
                            cn.LinearVelocity = Vector2.Zero;
                        ReleaseSlot(hslots[j]);
                        continue;
                    }
                    _claimedCarries.Add(c);
                    if (c.Source is Nugget held) _carriedNuggets.Add(held);
                }
            }
        }

        private readonly List<Vector2> _workPoints = new List<Vector2>();

        private int TickCarry(ZtxHauler h, float seconds)
        {
            if (!h.CanCarry) return 0;
            if (!h.IsUsable)
            {
                ArmTipSlot[] idle = h.Slots;
                for (int i = 0; i < idle.Length; i++) { idle[i].Homing = false; idle[i].Holding = false; }
                return 0;
            }
            Ship ship = base.Ship;
            if (ship?.Sim == null) return 0;
            Part self = h.Part;
            if (self == null) return 0;

            ArmTipSlot[] slots = h.Slots;
            float range = h.RangeTiles;
            float cdt = seconds > 0f ? seconds : (1f / 30f);
            var shipBody = ship.Physics.Body;
            ZtxTentacle vt = ZtxTentacle.GetFor(self);

            float grab = h.Rules.GrabSpeed.GetValue(self, 1f);
            if (grab <= 0f) grab = 8f;
            float carrySpeed = h.Rules.CarrySpeed.GetValue(self, 1f);

            _workPoints.Clear();
            Part workSalvage = h.SalvageClaim?.SalvagePart;
            if (workSalvage != null && !workSalvage.IsDestroyed && workSalvage.Ship != null)
                _workPoints.Add(workSalvage.Ship.DetTransformPointToWorld(workSalvage.LocalCenter));
            Part workBuild = h.BuildClaim;
            if (workBuild != null && workBuild.IsUnderConstruction && ReferenceEquals(workBuild.Ship, ship))
                _workPoints.Add(ship.DetTransformPointToWorld(workBuild.LocalCenter));
            Part workRepair = h.RepairClaim;
            if (workRepair != null && !workRepair.IsDestroyed && ReferenceEquals(workRepair.Ship, ship))
                _workPoints.Add(ship.DetTransformPointToWorld(workRepair.LocalCenter));
            Part workHaul = h.Claim?.Source?.Part;
            if (workHaul != null && ReferenceEquals(workHaul.Ship, ship))
                _workPoints.Add(ship.DetTransformPointToWorld(workHaul.LocalCenter));

            int delivered = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                ArmTipSlot slot = slots[i];

                Vector2 restLocal;
                if (vt == null || !vt.TryGetRestTipLocal(i, out restLocal))
                    restLocal = self.LocalCenter;
                Vector2 restWorld = ship.DetTransformPointToWorld(restLocal);

                if (!slot.Initialized)
                {
                    slot.Tip = restWorld;
                    slot.Initialized = true;
                }
                slot.PrevTip = slot.Tip;

                if (shipBody != null)
                    slot.Tip += (Vector2)shipBody.GetLinearVelocityFromWorldPoint(slot.Tip) * cdt;

                if (slot.Job == null && range > 0f)
                {
                    float rangeSq = range * range;
                    for (int c = 0; c < _nuggetJobs.Count; c++)
                    {
                        ResourceTransferJob cand = _nuggetJobs[c];
                        if (_claimedCarries.Contains(cand)) continue;
                        if (!IsCarryable(cand, ship)) continue;

                        Nugget cn = (Nugget)cand.Source;
                        Vector2 local = ship.DetTransformPointFromWorld(cn.DetWorldLocation);
                        if (self.LocalCenter.DistanceSquaredTo(local) > rangeSq) continue;
                        if (self.LocalCenter.DistanceSquaredTo(cand.Sink.Center) > rangeSq) continue;

                        slot.Job = cand;
                        ZtxDesyncTrace.Event(ship, "adopt organ@" + ZtxDesyncTrace.Loc(self) + " slot=" + i +
                                             " " + ZtxDesyncTrace.JobText(cand));
                        slot.Reaching = true;
                        slot.Holding = false;
                        _claimedCarries.Add(cand);
                        if (cand.Source is Nugget adopted) _carriedNuggets.Add(adopted);
                        break;
                    }
                }

                if (slot.Job == null)
                {
                    int work = TentacleFollow.WorkTarget(i, _workPoints.Count);
                    if (work >= 0)
                    {
                        TentacleFollow.HoldOffset(i, slots.Length, TentacleFollow.HoldFanTiles, out float dx, out float dy);
                        slot.Holding = true;
                        slot.Homing = false;
                        MoveTip(slot, _workPoints[work] + new Vector2(dx, dy), grab * cdt);
                        continue;
                    }
                    slot.Holding = false;
                    slot.Homing = TentacleFollow.StillHoming((restWorld - slot.Tip).Length, grab * cdt);
                    MoveTip(slot, restWorld, grab * cdt);
                    continue;
                }

                ResourceTransferJob job = slot.Job;
                if (!IsCarryable(job, ship))
                {
                    if (job.Source is Nugget dead && dead.Sim != null)
                        dead.LinearVelocity = Vector2.Zero;
                    ReleaseSlot(slot);
                    continue;
                }
                Nugget nugget = (Nugget)job.Source;

                Vector2 nugLocal = ship.DetTransformPointFromWorld(nugget.DetWorldLocation);
                float dropRange = range * ArmReach.DropBand;
                if (self.LocalCenter.DistanceSquaredTo(nugLocal) > dropRange * dropRange)
                {
                    nugget.LinearVelocity = Vector2.Zero;
                    ReleaseSlot(slot);
                    continue;
                }

                if (slot.Reaching)
                {
                    MoveTip(slot, nugget.DetWorldLocation, grab * cdt);
                    if ((nugget.DetWorldLocation - slot.Tip).Length <= GrabDistance)
                        slot.Reaching = false;
                    else
                        continue;
                }

                IResourceSink sink = job.Sink;
                Ship sinkShip = sink.Ship;
                if (sinkShip == null) { ReleaseSlot(slot); continue; }

                Vector2 target = sinkShip.DetTransformPointToWorld(sink.Center);
                if (carrySpeed <= 0f) continue;
                MoveTip(slot, target, carrySpeed * cdt);
                nugget.LinearVelocity = (slot.Tip - nugget.DetWorldLocation) * (1f / cdt);

                if ((target - slot.Tip).Length > CarryDeliverDistance) continue;

                nugget.LinearVelocity = Vector2.Zero;
                int want = job.ResourcesRequested.Confirmed - job.ResourcesInHand;
                if (want > nugget.Quantity) want = nugget.Quantity;
                if (want <= 0) { ReleaseSlot(slot); continue; }

                int received = sink.ReceiveResources(job.ResourceType, want, false, false);
                if (received >= nugget.Quantity)
                {
                    ZtxDesyncTrace.Event(ship, "deliver organ@" + ZtxDesyncTrace.Loc(self) + " slot=" + i +
                                         " " + received + " (nugget emptied) " + ZtxDesyncTrace.JobText(job));
                    job.Cancel();
                    ForgetCargo(nugget);
                    nugget.SubtractResources(received);
                    delivered += received;
                }
                else if (received >= want)
                {
                    ZtxDesyncTrace.Event(ship, "deliver organ@" + ZtxDesyncTrace.Loc(self) + " slot=" + i +
                                         " " + received + " (nugget kept) " + ZtxDesyncTrace.JobText(job));
                    nugget.SubtractResources(received);
                    job.Cancel(received, false);
                    delivered += received;
                }
                else
                {
                    if (ZtxHaulDebug.Verbose)
                        Log.Info("carry choke: " + ZtxHaulDebug.Sink(sink) + " took " +
                                 received + "/" + want + " " + job.ResourceType +
                                 " - job canceled, remainder stranded for re-route");
                    ZtxDesyncTrace.Event(ship, "choke organ@" + ZtxDesyncTrace.Loc(self) + " slot=" + i +
                                         " took " + received + "/" + want + " " + ZtxDesyncTrace.JobText(job));
                    if (received > 0)
                    {
                        nugget.SubtractResources(received);
                        delivered += received;
                    }
                    job.Cancel();
                }

                ReleaseSlot(slot);
            }
            return delivered;
        }

        private static void ReleaseSlot(ArmTipSlot slot)
        {
            slot.Job = null;
            slot.Homing = true;
            slot.Holding = false;
        }

        private static void MoveTip(ArmTipSlot slot, Vector2 target, float maxStep)
        {
            Vector2 d = target - slot.Tip;
            float dist = d.Length;
            slot.Tip = (dist <= maxStep || dist <= 1e-6f) ? target : slot.Tip + d * (maxStep / dist);
        }

        private const float CarryDeliverDistance = 0.45f;

        private const float GrabDistance = 0.35f;

        private static int CompareNuggetJobs(ResourceTransferJob a, ResourceTransferJob b)
        {
            Nugget na = a.Source as Nugget, nb = b.Source as Nugget;
            if (na == null) return nb == null ? 0 : 1;
            if (nb == null) return -1;
            int c = na.DetWorldLocation.X.CompareTo(nb.DetWorldLocation.X);
            if (c != 0) return c;
            c = na.DetWorldLocation.Y.CompareTo(nb.DetWorldLocation.Y);
            if (c != 0) return c;
            return string.CompareOrdinal(a.ResourceType.ToString(), b.ResourceType.ToString());
        }

        private static int CompareJobs(ResourceTransferJob a, ResourceTransferJob b)
        {
            Part pa = a.Source?.Part, pb = b.Source?.Part;
            if (pa == null) { if (pb != null) return 1; }
            else if (pb == null) return -1;
            else
            {
                int c = pa.Location.X.CompareTo(pb.Location.X);
                if (c != 0) return c;
                c = pa.Location.Y.CompareTo(pb.Location.Y);
                if (c != 0) return c;
            }

            int d = a.Sink.Center.X.CompareTo(b.Sink.Center.X);
            if (d != 0) return d;
            d = a.Sink.Center.Y.CompareTo(b.Sink.Center.Y);
            if (d != 0) return d;

            return string.CompareOrdinal(a.ResourceType.ToString(), b.ResourceType.ToString());
        }

        private static int CompareByLocation(ZtxHauler a, ZtxHauler b)
        {
            Part pa = a?.Part, pb = b?.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }
    }
}
