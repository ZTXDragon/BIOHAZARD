using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Commands;
using Cosmoteer.Ships.Statuses;
using Cosmoteer.Simulation.MediaEffects;
using Vector2 = Halfling.Geometry.Vector2;
using Halfling.Scene2D;
using Halfling.Timing;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Brain-cluster manager for one ship: finds contiguous blobs of modular brain blocks and turns
    // blob size into a NON-LINEAR command-point bonus.
    internal class ZtxBrainManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, ZtxBrainManager> s_managers =
            new ConditionalWeakTable<Ship, ZtxBrainManager>();

        private readonly List<ZtxBrainNode> _brains = new List<ZtxBrainNode>();

        private readonly Dictionary<Part, ZtxBrainNode> _byPart = new Dictionary<Part, ZtxBrainNode>();

        private readonly List<int> _clusterSizes = new List<int>();

        private readonly List<List<Part>> _clusterMembers = new List<List<Part>>();

        private readonly List<float> _clusterRadii = new List<float>();

        private readonly List<Disc> _discs = new List<Disc>();

        private readonly List<ZtxConscious> _gates = new List<ZtxConscious>();

        private readonly HashSet<Part> _unconscious = new HashSet<Part>();

        private readonly HashSet<Part> _nextUnconscious = new HashSet<Part>();

        private readonly HashSet<Part> _statusApplied = new HashSet<Part>();

        private static StatusType s_unconscious;
        private static bool s_unconsciousLookedUp;

        private Action<Part> _onPartAdded;
        private Action<Part> _onPartRemoved;

        private readonly List<(Vector2 Pos, float Rot)> _ringGeo = new List<(Vector2, float)>();

        private readonly List<MultiMediaEffectNode> _ringNodes = new List<MultiMediaEffectNode>();

        private bool _ringsDirty;

        private bool _ringsStale = true;

        private MultiMediaEffectRules _rangeEffect;

        private bool _ringsVisible;

        private bool _dirty = true;

        private float _radar;

        private int _appliedBonus;

        private int _appliedBlueprintBonus;

        private Action<BlueprintPart> _onBpAdded;
        private Action<BlueprintPart> _onBpRemoved;

        public ZtxBrainManager()
        {
            // A DECLARED constant (FixedUpdateBuckets.GetBucketName throws on invented
            // ints, reachable from the MP desync handler) and a SEQUENTIAL bucket.
            base.FixedUpdateBucket = FixedUpdateBuckets.Timers;
            base.FixedUpdatingEnabled = true;
        }

        public static ZtxBrainManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out ZtxBrainManager manager)) return manager;

            manager = new ZtxBrainManager();
            ZtxBrainManager captured = manager;
            Ship capturedShip = ship;

            // Same Sim-null branch as every other manager in this DLL: a ship's Sim is
            // null while parts attach, and EnqueueDeterministic silently drops there.
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

        public void Register(ZtxBrainNode b)
        {
            if (b?.Part == null) return;
            _brains.Add(b);
            _byPart[b.Part] = b;
            _dirty = true;
        }

        public void Deregister(ZtxBrainNode b)
        {
            if (b == null) return;
            if (_brains.Remove(b))
            {
                if (b.Part != null) _byPart.Remove(b.Part);
                _dirty = true;
            }
        }

        public void RegisterGate(ZtxConscious g)
        {
            if (g?.Part == null) return;
            if (!_gates.Contains(g)) _gates.Add(g);
            _dirty = true;
        }

        public void DeregisterGate(ZtxConscious g)
        {
            if (g == null) return;
            if (_gates.Remove(g)) _dirty = true;
        }

        public override void OnShipComponentAttaching(Ship ship)
        {
            base.OnShipComponentAttaching(ship);
            if (ship?.BlueprintParts == null) return;
            _onBpAdded = delegate (BlueprintPart bp) { OnBlueprintChanged(bp); };
            _onBpRemoved = delegate (BlueprintPart bp) { OnBlueprintChanged(bp); };
            ship.BlueprintParts.PartAdded += _onBpAdded;
            ship.BlueprintParts.PartRemoved += _onBpRemoved;
            RecomputeBlueprintBonus(ship);

            // Any part joining or leaving the hull changes who is in range of what.
            _onPartAdded = delegate (Part pp) { _dirty = true; };
            _onPartRemoved = delegate (Part pp) { _dirty = true; };
            ship.Parts.PartAdded += _onPartAdded;
            ship.Parts.PartRemoved += _onPartRemoved;
        }

        private void OnBlueprintChanged(BlueprintPart bp)
        {
            try
            {
                if (bp == null || FindBrainRules(bp.Rules) == null) return;
                Ship ship = base.Ship;
                if (ship?.BlueprintParts == null) return;
                RecomputeBlueprintBonus(ship);
                // The range ring is drawn from this same layout - a brain appearing,
                // moving or vanishing changes where control ends.
                _ringsStale = true;
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBrainManager.OnBlueprintChanged", ex);
            }
        }

        private static ZtxBrainNodeRules FindBrainRules(PartRules pr)
        {
            if (pr == null) return null;
            foreach (PartComponentRules cr in pr.ComponentsByID.Values)
                if (cr is ZtxBrainNodeRules b) return b;
            return null;
        }

        // One cluster of the BLUEPRINT brain layout.
        private struct BlueprintCluster
        {
            public List<Vector2> Centers;

            public int Tiles;

            public ZtxBrainNodeRules Rules;
        }

        private static List<BlueprintCluster> CollectBlueprintClusters(Ship ship)
        {
            var clusters = new List<BlueprintCluster>();
            if (ship?.BlueprintParts == null) return clusters;

            var order = new List<BlueprintPart>();
            var brains = new Dictionary<BlueprintPart, ZtxBrainNodeRules>();
            foreach (BlueprintPart bp in ship.BlueprintParts)
            {
                ZtxBrainNodeRules r = FindBrainRules(bp?.Rules);
                if (r == null) continue;
                order.Add(bp);
                brains[bp] = r;
            }
            if (order.Count == 0) return clusters;

            var visited = new HashSet<BlueprintPart>();
            var queue = new Queue<BlueprintPart>();
            for (int i = 0; i < order.Count; i++)
            {
                BlueprintPart seed = order[i];
                if (visited.Contains(seed)) continue;

                int tiles = 0;
                var centers = new List<Vector2>();
                queue.Enqueue(seed);
                visited.Add(seed);
                while (queue.Count > 0)
                {
                    BlueprintPart cur = queue.Dequeue();
                    tiles += cur.Rules.Size.X * cur.Rules.Size.Y;
                    centers.Add(cur.LocalCenter);
                    foreach (BlueprintPart adj in ship.BlueprintParts.GetAdjacentParts(cur.Rect))
                    {
                        if (adj == null || visited.Contains(adj) || !brains.ContainsKey(adj)) continue;
                        visited.Add(adj);
                        queue.Enqueue(adj);
                    }
                }
                clusters.Add(new BlueprintCluster
                {
                    Centers = centers,
                    Tiles = tiles,
                    Rules = brains[seed],
                });
            }
            return clusters;
        }

        private void RecomputeBlueprintBonus(Ship ship)
        {
            List<BlueprintCluster> clusters = CollectBlueprintClusters(ship);

            int bonus = 0;
            if (clusters.Count > 0)
            {
                ZtxBrainNodeRules rules = clusters[0].Rules;
                double total = 0;
                int totalTiles = 0;
                for (int i = 0; i < clusters.Count; i++)
                {
                    int tiles = clusters[i].Tiles;
                    totalTiles += tiles;
                    total += Math.Round(rules.CPBase * Math.Pow(tiles, rules.CPExponent));
                }
                int b = (int)(total - rules.CPBase * totalTiles);
                bonus = b > 0 ? b : 0;
            }

            if (bonus == _appliedBlueprintBonus) return;
            ship.BlueprintParts.CommandPointsProvided += bonus - _appliedBlueprintBonus;
            _appliedBlueprintBonus = bonus;
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            if (!Config.Enabled) return;
            if (!_dirty) return;
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;   // stay dirty; retry when the sim is live
            _dirty = false;

            long rebuildStart = ZtxPerfProbe.Mark();
            try
            {
                RebuildClusters(ship);
                ApplyBonus(ship, ComputeBonus());
                ApplyRadar(ship);
                RefreshConsciousness(ship);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBrainManager.FixedUpdate", ex);
            }
            finally
            {
                ZtxPerfProbe.Section("brain range rebuild", rebuildStart);
            }
        }

        private void RebuildClusters(Ship ship)
        {
            _clusterSizes.Clear();
            _clusterMembers.Clear();
            _clusterRadii.Clear();
            _discs.Clear();          // cleared BEFORE the early return: no brains, no awake region

            // Drop stale registrations: a ship split re-parents the SAME Part instances.
            for (int i = _brains.Count - 1; i >= 0; i--)
            {
                Part p = _brains[i]?.Part;
                if (p == null || !ReferenceEquals(p.Ship, ship))
                {
                    if (p != null) _byPart.Remove(p);
                    _brains.RemoveAt(i);
                }
            }
            if (_brains.Count == 0) return;

            _brains.Sort(CompareByLocation);

            var visited = new HashSet<Part>();
            var queue = new Queue<Part>();
            foreach (ZtxBrainNode seed in _brains)
            {
                Part sp = seed.Part;
                if (sp == null || visited.Contains(sp)) continue;

                int tiles = 0;
                var members = new List<Part>();
                queue.Enqueue(sp);
                visited.Add(sp);
                while (queue.Count > 0)
                {
                    Part cur = queue.Dequeue();
                    tiles += cur.Rules.Size.X * cur.Rules.Size.Y;
                    members.Add(cur);
                    foreach (Part adj in ship.Parts.GetAdjacentParts(cur.Rect))
                    {
                        if (adj == null || visited.Contains(adj)) continue;
                        if (!_byPart.ContainsKey(adj)) continue;   // not a brain block
                        visited.Add(adj);
                        queue.Enqueue(adj);
                    }
                }
                _clusterSizes.Add(tiles);
                _clusterMembers.Add(members);
                ZtxBrainNodeRules br = seed.Rules;
                float radius = br.RadiusBase + br.RadiusPerTile * tiles;
                _clusterRadii.Add(radius);
                for (int m = 0; m < members.Count; m++)
                {
                    Vector2 mc = members[m].LocalCenter;
                    _discs.Add(new Disc(mc.X, mc.Y, radius));
                }
            }
        }

        private int ComputeBonus()
        {
            if (_brains.Count == 0 || _clusterSizes.Count == 0) return 0;

            ZtxBrainNodeRules r = _brains[0].Rules;
            double total = 0;
            for (int i = 0; i < _clusterSizes.Count; i++)
                total += Math.Round(r.CPBase * Math.Pow(_clusterSizes[i], r.CPExponent));

            double authored = r.CPBase * _brains.Count;
            int bonus = (int)(total - authored);
            return bonus > 0 ? bonus : 0;
        }

        private void RefreshConsciousness(Ship ship)
        {
            _nextUnconscious.Clear();

            foreach (Part part in ship.Parts)
            {
                if (part == null || _byPart.ContainsKey(part)) continue;   // brains: always
                Vector2 c = part.LocalCenter;
                if (!ConsciousnessRule.IsConscious(c.X, c.Y, _discs)) _nextUnconscious.Add(part);
            }

            // Push the answer to the gates, dropping any that left this ship. A gate whose
            // part moved to another ship is re-registered there by its own OnPartAttached.
            for (int i = _gates.Count - 1; i >= 0; i--)
            {
                ZtxConscious g = _gates[i];
                Part p = g?.Part;
                if (p == null || !ReferenceEquals(p.Ship, ship))
                {
                    _gates.RemoveAt(i);
                    continue;
                }
                g.SetConscious(!_nextUnconscious.Contains(p));
            }

            StatusType st = ResolveUnconsciousStatus();
            if (st != null)
            {
                for (int i = 0; i < _gates.Count; i++)
                {
                    Part part = _gates[i].Part;
                    if (part == null || _statusApplied.Contains(part)) continue;
                    if (!_nextUnconscious.Contains(part)) continue;
                    ship.Statuses.ApplyStatus(st, part, (Time)3600f, 1f, ship, null, null);
                    _statusApplied.Add(part);
                }
                _statusApplied.RemoveWhere(delegate (Part part)
                {
                    if (_nextUnconscious.Contains(part) && ReferenceEquals(part.Ship, ship))
                        return false;
                    if (ReferenceEquals(part.Ship, ship))
                        ship.Statuses.GetPartHandler(st).RemoveStatuses(part);
                    return true;
                });
            }

            _unconscious.Clear();
            foreach (Part part in _nextUnconscious) _unconscious.Add(part);
        }

        private static StatusType ResolveUnconsciousStatus()
        {
            if (s_unconsciousLookedUp) return s_unconscious;
            s_unconsciousLookedUp = true;
            try
            {
                s_unconscious = GameApp.Rules.Statuses[new Cosmoteer.Data.ID<StatusType>("ztx.unconscious")];
            }
            catch (Exception)
            {
                Log.Error("StatusType ztx.unconscious not found — consciousness gates DLL systems only.");
                s_unconscious = null;
            }
            return s_unconscious;
        }

        private void ApplyRadar(Ship ship)
        {
            float radar = 0f;
            Vector2[] curve = _brains.Count > 0 ? _brains[0]?.Rules?.RadarCurve : null;
            if (curve != null && curve.Length > 0)
            {
                var tiles = new float[curve.Length];
                var reach = new float[curve.Length];
                for (int i = 0; i < curve.Length; i++)
                {
                    tiles[i] = curve[i].X;
                    reach[i] = curve[i].Y;
                }
                radar = BrainRadar.ShipRadius(_clusterSizes, tiles, reach);
            }
            if (radar.Equals(_radar)) return;
            _radar = radar;
            ship.Sensors?.ClearCaches();
            Log.Info("brain radar on ship " + ship.UniqueID + ": " + Math.Round(radar) +
                     " (largest cluster of [" + string.Join(",", _clusterSizes) + "] tile(s))");
        }

        public static float RadarOf(Ship ship)
        {
            if (ship == null || !s_managers.TryGetValue(ship, out ZtxBrainManager m)) return 0f;
            return m._radar;
        }

        public static bool IsConscious(Part part)
        {
            Ship ship = part?.Ship;
            if (ship == null) return true;
            if (!s_managers.TryGetValue(ship, out ZtxBrainManager m)) return true;
            return !m._unconscious.Contains(part);
        }

        private void RebuildRingGeometry(Ship ship)
        {
            _ringGeo.Clear();
            _ringsDirty = true;
            _ringsStale = false;
            _rangeEffect = null;

            List<BlueprintCluster> clusters = CollectBlueprintClusters(ship);
            var radii = new List<float>(clusters.Count);
            for (int i = 0; i < clusters.Count; i++)
            {
                ZtxBrainNodeRules br = clusters[i].Rules;
                if (_rangeEffect == null) _rangeEffect = br.RangeEffect;
                radii.Add(br.RadiusBase + br.RadiusPerTile * clusters[i].Tiles);
            }

            const float SegLen = 1.0f;
            for (int i = 0; i < clusters.Count; i++)
            {
                float r = radii[i];
                if (r <= 0f) continue;
                List<Vector2> members = clusters[i].Centers;
                int steps = (int)Math.Ceiling(2.0 * Math.PI * r / SegLen);
                if (steps < 24) steps = 24;

                for (int m = 0; m < members.Count; m++)
                {
                    Vector2 cm = members[m];
                    for (int k = 0; k < steps; k++)
                    {
                        float ang = (k + 0.5f) * (6.2831853f / steps);
                        Vector2 pt = cm + Vector2.FromAngle(ang) * r;

                        // Inside any OTHER block's circle (any cluster)? Then this arc is
                        // interior, not boundary. Small epsilon keeps butt joints closed.
                        bool interior = false;
                        for (int j = 0; j < clusters.Count && !interior; j++)
                        {
                            float rj = radii[j];
                            float rjSq = (rj - 0.05f) * (rj - 0.05f);
                            List<Vector2> mj = clusters[j].Centers;
                            for (int n = 0; n < mj.Count; n++)
                            {
                                if (i == j && n == m) continue;
                                if (pt.DistanceSquaredTo(mj[n]) < rjSq)
                                {
                                    interior = true;
                                    break;
                                }
                            }
                        }
                        if (!interior) _ringGeo.Add((pt, ang + 1.5707964f));   // tangent
                    }
                }
            }
        }

        public override void Update(SceneRoot root)
        {
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            bool blueprintMode = ship.Sim.Overlays != null && ship.Sim.Overlays.ShowBlueprints;
            if (blueprintMode && _ringsStale) RebuildRingGeometry(ship);

            MultiMediaEffectRules fx = _rangeEffect;
            bool show = fx != null && _ringGeo.Count > 0 && ship.IsWithinSightOfLocalPlayer
                        && blueprintMode;

            if (!show)
            {
                if (_ringsVisible) ReleaseRings(false);
                return;
            }

            if (_ringsDirty || !_ringsVisible)
            {
                ReleaseRings(true);
                for (int i = 0; i < _ringGeo.Count; i++)
                {
                    MultiMediaEffectNode node = MultiMediaEffectNode.Alloc(fx, ship, _ringGeo[i].Pos);
                    node.PlayContinuous();
                    _ringNodes.Add(node);
                }
                _ringsDirty = false;
                _ringsVisible = true;
            }

            float shipRot = ship.Rotation;
            for (int i = 0; i < _ringNodes.Count && i < _ringGeo.Count; i++)
            {
                MultiMediaEffectNode node = _ringNodes[i];
                if (node != null) node.Rotation = new Halfling.Geometry.Direction(_ringGeo[i].Rot + shipRot);
            }
        }

        private void ReleaseRings(bool immediate)
        {
            for (int i = 0; i < _ringNodes.Count; i++)
            {
                try { _ringNodes[i]?.EndContinuous(immediate); }
                catch (Exception ex) { Log.Exception("ZtxBrainManager.ReleaseRings", ex); }
            }
            _ringNodes.Clear();
            _ringsVisible = false;
        }

        private void ApplyBonus(Ship ship, int bonus)
        {
            if (bonus == _appliedBonus) return;
            int diff = bonus - _appliedBonus;

            // LIVE tally: ship.Commands (control, CanExecuteCommands, flight HUD) — via
            // the same interface real providers call; refreshes CanExecuteCommands itself.
            var commands = (CommandProvider.ICommandManagerMembers)ship.Commands;
            if (diff > 0) commands.OnCommandProviderOperational(diff);
            else commands.OnCommandProviderNonOperational(-diff);

            ship.Parts.CommandPointsProvided += diff;

            _appliedBonus = bonus;

            var sizes = new System.Text.StringBuilder();
            for (int i = 0; i < _clusterSizes.Count; i++)
                sizes.Append(i > 0 ? "," : "").Append(_clusterSizes[i]);
            Log.Info("brain clusters on ship " + ship.UniqueID + ": [" + sizes + "] tile(s), CP bonus " +
                     _appliedBonus + " (" + _brains.Count + " block(s))");
        }

        public override void OnShipComponentDetached(Ship ship)
        {
            try
            {
                if (_appliedBonus != 0 && ship?.Commands != null)
                {
                    ((CommandProvider.ICommandManagerMembers)ship.Commands)
                        .OnCommandProviderNonOperational(_appliedBonus);
                    if (ship.Parts != null) ship.Parts.CommandPointsProvided -= _appliedBonus;
                    _appliedBonus = 0;
                }
                if (_radar != 0f)
                {
                    // Same symmetry as the CP bonus: a manager leaving its ship takes its radar.
                    _radar = 0f;
                    ship?.Sensors?.ClearCaches();
                }
                ReleaseRings(true);

                if (ship?.Parts != null)
                {
                    if (_onPartAdded != null) ship.Parts.PartAdded -= _onPartAdded;
                    if (_onPartRemoved != null) ship.Parts.PartRemoved -= _onPartRemoved;
                }
                _onPartAdded = null;
                _onPartRemoved = null;

                if (ship?.BlueprintParts != null)
                {
                    if (_onBpAdded != null) ship.BlueprintParts.PartAdded -= _onBpAdded;
                    if (_onBpRemoved != null) ship.BlueprintParts.PartRemoved -= _onBpRemoved;
                    if (_appliedBlueprintBonus != 0)
                    {
                        ship.BlueprintParts.CommandPointsProvided -= _appliedBlueprintBonus;
                        _appliedBlueprintBonus = 0;
                    }
                }
                _onBpAdded = null;
                _onBpRemoved = null;
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBrainManager.OnShipComponentDetached", ex);
            }
            base.OnShipComponentDetached(ship);
        }

        private static int CompareByLocation(ZtxBrainNode a, ZtxBrainNode b)
        {
            Part pa = a?.Part, pb = b?.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }
    }
}
