using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Halfling.Scene2D;
using Halfling.Timing;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Drives blood movement for one ship.
    internal class BloodFlowManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, BloodFlowManager> s_managers =
            new ConditionalWeakTable<Ship, BloodFlowManager>();

        private readonly List<ZtxFlowNode> _nodes = new List<ZtxFlowNode>();
        private readonly Dictionary<ZtxFlowNode, int> _indexByNode = new Dictionary<ZtxFlowNode, int>();
        private readonly Dictionary<ZtxFlowNode, BloodNode> _nodeByFlow = new Dictionary<ZtxFlowNode, BloodNode>();
        private BloodGraph _graph;
        private bool _topologyDirty = true;
        private int _tickCounter;
        private int _scaleSignature;
        private int _scaleCheckCounter;
        private int[] _beforeVolumes;
        private int _movedThisWindow;
        private int _ticksWithMovement;
        private int _outputRefreshCounter;


        public BloodFlowManager()
        {
            base.FixedUpdateBucket = FixedUpdateBuckets.RailgunProjectiles;
            base.FixedUpdatingEnabled = true;
        }

        public static BloodFlowManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out BloodFlowManager manager)) return manager;

            manager = new BloodFlowManager();
            BloodFlowManager captured = manager;
            Ship capturedShip = ship;

            if (ship.Sim != null)
            {
                ship.Sim.EnqueueDeterministic(ship.UniqueID, delegate
                {
                    capturedShip.Components.Add(captured);
                });
                Log.Info("BloodFlowManager created for ship " + ship.UniqueID + " (deferred attach, sim running)");
            }
            else
            {
                ship.Components.Add(manager);
                Log.Info("BloodFlowManager created for ship " + ship.UniqueID + " (direct attach, no sim yet)");
            }

            s_managers.Add(ship, manager);
            return manager;
        }

        public void Register(ZtxFlowNode node)
        {
            if (node == null) return;
            _nodes.Add(node);
            _topologyDirty = true;
        }

        public void Deregister(ZtxFlowNode node)
        {
            if (node == null) return;
            if (_nodes.Remove(node))
            {
                _topologyDirty = true;
            }
        }

        // =============== SYSTEM-FULLNESS GATES (2026-08-24) ===============
        // Producer toggles driven by CONNECTED-SYSTEM blood fullness — see ZtxFlowGate.
        private readonly List<ZtxFlowGate> _gates = new List<ZtxFlowGate>();

        private int[] _systemLabels;
        private readonly Dictionary<Part, int> _systemByPart = new Dictionary<Part, int>();

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private void RebuildSystemLabels()
        {
            int n = _graph.Nodes.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            void Union(int a, int b)
            {
                int ra = Find(parent, a), rb = Find(parent, b);
                if (ra != rb) parent[ra] = rb;
            }

            // Within-network adjacency: gradient edges AND pumps (a valve's edges are
            // one-way but it is still the same system).
            foreach (KeyValuePair<int, List<int>> kv in _graph.Downstream)
                for (int i = 0; i < kv.Value.Count; i++) Union(kv.Key, kv.Value[i]);
            foreach ((int from, int to) in _graph.Pumps) Union(from, to);

            // Cross-network merge on SystemBridge parts: all pressure-model nodes of
            // the part join one system.
            var byPart = new Dictionary<Part, List<int>>();
            bool bridged;
            foreach (KeyValuePair<ZtxFlowNode, int> kv in _indexByNode)
            {
                ZtxFlowNode fn = kv.Key;
                if (fn.Part == null || fn.Rules.Diffusion) continue;
                if (!byPart.TryGetValue(fn.Part, out var list))
                    byPart[fn.Part] = list = new List<int>();
                list.Add(kv.Value);
            }
            foreach (KeyValuePair<Part, List<int>> kv in byPart)
            {
                bridged = false;
                foreach (KeyValuePair<ZtxFlowNode, int> e in _indexByNode)
                {
                    if (e.Key.Part == kv.Key && e.Key.Rules.SystemBridge) { bridged = true; break; }
                }
                if (bridged)
                    for (int i = 1; i < kv.Value.Count; i++) Union(kv.Value[0], kv.Value[i]);
            }

            _systemLabels = new int[n];
            for (int i = 0; i < n; i++) _systemLabels[i] = Find(parent, i);

            _systemByPart.Clear();
            foreach (KeyValuePair<Part, List<int>> kv in byPart)
                _systemByPart[kv.Key] = _systemLabels[kv.Value[0]];
        }

        public void RegisterGate(ZtxFlowGate gate)
        {
            if (gate != null) _gates.Add(gate);
        }

        public void DeregisterGate(ZtxFlowGate gate)
        {
            if (gate != null) _gates.Remove(gate);
        }

        private void UpdateGates()
        {
            if (_gates.Count == 0) return;

            long totalVol = 0, totalCap = 0;
            var sums = new Dictionary<(int Sys, string Net), (long Vol, long Cap)>();
            foreach (KeyValuePair<ZtxFlowNode, int> entry in _indexByNode)
            {
                ZtxFlowNode fn = entry.Key;
                if (fn.Rules.Diffusion) continue;
                BloodNode core = _nodeByFlow[fn];
                int vol = core.Volume;
                int cap = core.Capacity;
                totalVol += vol;
                totalCap += cap;

                if (_systemLabels == null || entry.Value >= _systemLabels.Length) continue;
                int sys = _systemLabels[entry.Value];
                sums.TryGetValue((sys, ""), out var aggAll);
                sums[(sys, "")] = (aggAll.Vol + vol, aggAll.Cap + cap);
                sums.TryGetValue((sys, fn.Rules.Network), out var aggNet);
                sums[(sys, fn.Rules.Network)] = (aggNet.Vol + vol, aggNet.Cap + cap);
            }

            for (int i = 0; i < _gates.Count; i++)
            {
                ZtxFlowGate gate = _gates[i];

                float fullness;
                long gVol, gCap;
                if (gate.Part != null && _systemByPart.TryGetValue(gate.Part, out int sys))
                {
                    sums.TryGetValue((sys, gate.Rules.Network), out var e);
                    gVol = e.Vol; gCap = e.Cap;
                }
                else
                {
                    // Ship-wide fallback when the gate's part has no pressure-model
                    // node (should not happen for the marrow).
                    gVol = totalVol; gCap = totalCap;
                }
                fullness = gCap <= 0 ? 0f : (float)gVol / gCap;
                gate.LastFullness = fullness;
                gate.LastVolume = gVol;
                gate.LastCapacity = gCap;
                if (gate.IsToggleOn)
                {
                    if (fullness >= gate.EffectiveStopAbove) gate.SetOn(false);
                }
                else if (fullness <= gate.EffectiveResumeBelow)
                {
                    gate.SetOn(true);
                }
            }
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            if (!Config.Enabled) return;
            if (_nodes.Count == 0) return;   // nothing to circulate on this ship

            if (_topologyDirty)
            {
                _topologyDirty = false;
                long rebuildStart = ZtxPerfProbe.Mark();
                RebuildGraphAndField();
                ZtxPerfProbe.Section("blood graph rebuild (all)", rebuildStart);
            }
            else if (++_scaleCheckCounter >= 30)
            {
                long scaleStart = ZtxPerfProbe.Mark();
                _scaleCheckCounter = 0;
                int sig = ShipGraphAdapter.ScaleSignature(_nodes);
                if (sig != _scaleSignature)
                {
                    _scaleSignature = sig;
                    ShipGraphAdapter.RefreshScaling(_nodeByFlow);
                    FieldBuilder.Build(_graph);
                    Log.Info("heart strength changed - pressure field repainted");
                }
                ZtxPerfProbe.Section("blood heart-strength check", scaleStart);
            }

            if (_graph == null) return;

            MeasureAndTick();
            long gatesStart = ZtxPerfProbe.Mark();
            UpdateGates();
            ZtxPerfProbe.Section("blood fullness gates", gatesStart);

            if (++_outputRefreshCounter >= 300)
            {
                _outputRefreshCounter = 0;
                long watchdogStart = ZtxPerfProbe.Mark();
                RefreshNetworkOutputs();
                ZtxPerfProbe.Section("blood network watchdog", watchdogStart);
            }

            if (++_tickCounter >= 300)
            {
                _tickCounter = 0;
                long statusStart = ZtxPerfProbe.Mark();
                LogFlowState();
                ZtxPerfProbe.Section("blood status log", statusStart);
            }
        }

        private void MeasureAndTick()
        {
            long flowStart = ZtxPerfProbe.Mark();
            // Pull converter output (marrow / lung / distributor) into the model first.
            ShipGraphAdapter.ReadForward(_graph, _nodeByFlow);

            int n = _graph.Nodes.Count;
            if (_beforeVolumes == null || _beforeVolumes.Length < n) _beforeVolumes = new int[n];
            for (int i = 0; i < n; i++) _beforeVolumes[i] = _graph.Nodes[i].Volume;

            int ticksPerSecond = (int)System.Math.Round(
                GameApp.Rules.Simulation.PhysicsUpdatesPerSecond);
            FlowStep.Tick(_graph, Config.GradientGain, ticksPerSecond);
            if (_graph.HasDiffusionNodes) DiffusionStep.Tick(_graph);

            int moved = 0;
            for (int i = 0; i < n; i++)
            {
                int d = _graph.Nodes[i].Volume - _beforeVolumes[i];
                if (d > 0) moved += d;              // count arrivals only, so each unit counts once
            }
            _movedThisWindow += moved;
            if (moved > 0) _ticksWithMovement++;
            ZtxPerfProbe.Section("blood flow math", flowStart);

            // Timed apart from the maths: every storage write runs the engine's own reaction to
            // it (fill sprites, toggles, network queries) synchronously, inside this call.
            long writeStart = ZtxPerfProbe.Mark();
            int written = ShipGraphAdapter.WriteBack(_graph, _nodeByFlow);
            ZtxPerfProbe.Section("blood write-back", writeStart);
            ZtxPerfProbe.Tally("blood nodes ticked", n);
            ZtxPerfProbe.Tally("blood storages written", written);
        }

        private void LogFlowState()
        {
            int filled = 0, total = 0, maxP = 0, minP = int.MaxValue, live = 0, stuck = 0, stuckUnits = 0;
            for (int i = 0; i < _graph.Nodes.Count; i++)
            {
                BloodNode n = _graph.Nodes[i];
                if (n.Volume > 0) filled++;
                total += n.Volume;
                // Blood sitting at zero pressure can never move: no gradient, no downhill
                // neighbour. This is what a starved organ source looks like from the outside.
                if (n.Volume > 0 && n.Pressure == 0) { stuck++; stuckUnits += n.Volume; }
                if (n.Pressure > maxP) maxP = n.Pressure;
                if (n.Pressure < minP) minP = n.Pressure;
            }
            if (minP == int.MaxValue) minP = 0;
            for (int i = 0; i < _nodes.Count; i++) if (_nodes[i].HasBackingStorage) live++;

            if (Config.VerboseLog)
            {
                var byRole = new Dictionary<string, int[]>();   // role|network -> {nodes, units, withBlood}
                foreach (KeyValuePair<ZtxFlowNode, BloodNode> kv in _nodeByFlow)
                {
                    string key = kv.Key.Rules.Role + "/" + kv.Key.Rules.Network;
                    if (!byRole.TryGetValue(key, out int[] agg)) byRole[key] = agg = new int[3];
                    agg[0]++;
                    agg[1] += kv.Value.Volume;
                    if (kv.Value.Volume > 0) agg[2]++;
                }
                var sb2 = new System.Text.StringBuilder("  by role:");
                foreach (KeyValuePair<string, int[]> kv in byRole)
                    sb2.Append(' ').Append(kv.Key).Append('=').Append(kv.Value[2]).Append('/')
                       .Append(kv.Value[0]).Append(" (").Append(kv.Value[1]).Append("u)");
                Log.Verbose(sb2.ToString());
            }

            int hearts = 0;
            foreach (KeyValuePair<ZtxFlowNode, BloodNode> kv in _nodeByFlow)
                if (kv.Key.Rules.Role == FlowRole.Source) hearts++;

            Log.Info("flow: " + filled + "/" + _graph.Nodes.Count + " node(s) hold blood, " +
                     total + " total units, pressure " + minP + ".." + maxP +
                     ", " + live + "/" + _nodes.Count + " driving real storage" +
                     (hearts == 0
                        ? " | NO HEART on this network - static by design, not a stall"
                        : " | THROUGHPUT " + _movedThisWindow + " units over " + _ticksWithMovement +
                          "/300 active ticks" +
                          (stuck > 0
                             ? " | STUCK " + stuckUnits + " units in " + stuck + " zero-pressure node(s)"
                             : "")));
            _movedThisWindow = 0;
            _ticksWithMovement = 0;
        }

        private void RebuildGraphAndField()
        {
            _graph = ShipGraphAdapter.Build(_nodes, _indexByNode, _nodeByFlow);
            FieldBuilder.Build(_graph);
            _scaleSignature = ShipGraphAdapter.ScaleSignature(_nodes);
            RebuildSystemLabels();

            // Everything below only reports; timed on its own because it writes a line per
            // directional part on every rebuild.
            long logStart = ZtxPerfProbe.Mark();
            try
            {
                LogRebuild();
            }
            finally
            {
                ZtxPerfProbe.Section("blood graph rebuild (logging)", logStart);
            }
        }

        private void LogRebuild()
        {
            int providers = 0, isolated = 0, maxPressure = 0, unreached = 0;
            var isolatedRoles = new Dictionary<NodeRole, int>();
            foreach (BloodNode n in _graph.Nodes)
            {
                if (n.IsResetPoint && n.PMax > 0) providers++;
                if (n.Pressure == 0) unreached++;
                if (n.Pressure > maxPressure) maxPressure = n.Pressure;
                if (!_graph.Downstream.TryGetValue(n.Id, out var outs) || outs.Count == 0)
                {
                    isolated++;
                    isolatedRoles.TryGetValue(n.Role, out int c);
                    isolatedRoles[n.Role] = c + 1;
                }
            }

            if (Config.VerboseLog && isolated > 0)
            {
                var sb = new System.Text.StringBuilder("isolated by role:");
                foreach (KeyValuePair<NodeRole, int> kv in isolatedRoles)
                    sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
                Log.Verbose(sb.ToString());
            }

            // Directional parts: what the model thinks each one is aimed at. Rotate a part,
            // reload, and the line should change - the facing check when art is in doubt.
            if (Config.VerboseLog)
            {
                foreach (KeyValuePair<ZtxFlowNode, int> kv in _indexByNode)
                {
                    ZtxFlowNode fn = kv.Key;
                    if (!fn.Rules.Directional || fn.Part == null) continue;
                    _graph.Downstream.TryGetValue(kv.Value, out var outs);
                    Log.Verbose("  directional " + fn.Rules.Role +
                                " at " + fn.Part.Location +
                                " rot=" + fn.Part.Rotation +
                                " facing=" + ShipGraphAdapter.DescribeFacing(fn) +
                                " -> " + (outs == null ? 0 : outs.Count) + " downstream");
                }
            }

            Log.Info("graph rebuilt on ship " + (base.Ship != null ? base.Ship.UniqueID.ToString() : "?") +
                     ": " + _graph.Nodes.Count + " node(s), " + ShipGraphAdapter.EdgeCount + " edge(s), " +
                     providers + " provider(s), " + isolated + " isolated, " +
                     unreached + " at zero pressure, maxP=" + maxPressure +
                     ", directed=" + ShipGraphAdapter.DirectedEdgeCount + " of " + ShipGraphAdapter.ConsideredEdgeCount + " considered" +
                     " (blocked=" + ShipGraphAdapter.BlockedEdgeCount + ")" +
                     ", pumps=" + ShipGraphAdapter.PumpCount);
        }

        private void RefreshNetworkOutputs()
        {
            Ship ship = base.Ship;
            if (ship?.Parts == null) return;

            foreach (Part part in ship.Parts)
            {
                IReadOnlyList<PartComponent> comps = part.Components;
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i] is not BasePartNetworkResourceStorage netStorage) continue;
                    try
                    {
                        if (netStorage.NetworkNode == null)
                        {
                            ship.PartNetworks.RequestComponentRegistration(netStorage);
                            Log.Info("network watchdog: re-registered orphaned " +
                                     netStorage.GetType().Name + " on " + part);
                        }
                        else if (netStorage is PartNetworkResourceOutput output)
                        {
                            output.OnSubnetworkChanged();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Exception("BloodFlowManager.RefreshNetworkOutputs", ex);
                    }
                }
            }
        }

    }
}
