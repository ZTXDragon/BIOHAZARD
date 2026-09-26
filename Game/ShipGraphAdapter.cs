using System.Collections.Generic;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer;
using Halfling.Geometry;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Turns a ship's live ZtxFlowNode components into a pure-core BloodGraph, and copies volumes
    // back afterwards.
    internal static class ShipGraphAdapter
    {
        public static int EdgeCount { get; private set; }

        public static int DirectedEdgeCount { get; private set; }

        public static int PumpCount { get; private set; }

        public static int ConsideredEdgeCount { get; private set; }

        public static int BlockedEdgeCount { get; private set; }

        public static BloodGraph Build(List<ZtxFlowNode> flowNodes,
                                      Dictionary<ZtxFlowNode, int> indexByNode,
                                      Dictionary<ZtxFlowNode, BloodNode> nodeByFlow)
        {
            var graph = new BloodGraph();
            indexByNode.Clear();
            nodeByFlow.Clear();

            var byPart = new Dictionary<Part, List<ZtxFlowNode>>();
            for (int i = 0; i < flowNodes.Count; i++)
            {
                ZtxFlowNode fn = flowNodes[i];
                if (fn?.Part == null) continue;

                int decay = fn.Rules.PressureDecay < 1 ? 1 : fn.Rules.PressureDecay;
                var core = new BloodNode(i, ToCoreRole(fn.Rules.Role), fn.EffectiveCapacity,
                                         decay, fn.EffectiveFlowRate)
                {
                    PMax = fn.EffectivePMax,
                    Volume = fn.Volume,
                    PumpDriven = fn.Rules.Role == FlowRole.Valve || fn.Rules.Directional,
                    PropagatePressure = fn.Rules.PropagatePressure,
                    Diffusion = fn.Rules.Diffusion,
                    PinPressureZero = fn.Rules.AlwaysZeroPressure,
                    Retain = fn.Rules.RetainVolume < 0 ? 0 : fn.Rules.RetainVolume,
                    Reserve = fn.Rules.ReserveVolume < 0 ? 0 : fn.Rules.ReserveVolume,
                };
                graph.Add(core);
                indexByNode[fn] = i;
                nodeByFlow[fn] = core;
                if (!byPart.TryGetValue(fn.Part, out var onPart))
                    byPart[fn.Part] = onPart = new List<ZtxFlowNode>();
                onPart.Add(fn);
            }

            var seenOrdered = new HashSet<long>();
            var neighbours = new Dictionary<ZtxFlowNode, List<ZtxFlowNode>>();
            EdgeCount = 0;
            DirectedEdgeCount = 0;
            PumpCount = 0;
            ConsideredEdgeCount = 0;
            BlockedEdgeCount = 0;

            foreach (KeyValuePair<ZtxFlowNode, int> entry in indexByNode)
            {
                ZtxFlowNode a = entry.Key;
                int aId = entry.Value;

                foreach (ZtxFlowNode b in GetNetworkNeighbours(a, byPart))
                {
                    if (!indexByNode.TryGetValue(b, out int bId)) continue;
                    if (bId == aId) continue;

                    if (!neighbours.TryGetValue(a, out var list))
                        neighbours[a] = list = new List<ZtxFlowNode>();
                    if (!list.Contains(b)) list.Add(b);

                    TryConnect(graph, seenOrdered, a, aId, b, bId);
                    TryConnect(graph, seenOrdered, b, bId, a, aId);
                }
            }

            var seenPumps = new HashSet<long>();
            foreach (KeyValuePair<ZtxFlowNode, int> entry in indexByNode)
            {
                ZtxFlowNode v = entry.Key;
                if (v.Rules.Role != FlowRole.Valve && !v.Rules.Directional) continue;
                if (!neighbours.TryGetValue(v, out var around)) continue;

                foreach (ZtxFlowNode n in around)
                {
                    if (!indexByNode.TryGetValue(n, out int nId)) continue;
                    int side = SideOf(v, n);
                    if (side < 0) TryPump(graph, seenPumps, n, nId, v, entry.Value);       // behind -> valve
                    else if (side > 0) TryPump(graph, seenPumps, v, entry.Value, n, nId);  // valve -> front
                }
            }

            foreach (KeyValuePair<ZtxFlowNode, int> entry in indexByNode)
            {
                ZtxFlowNode v = entry.Key;
                if (!v.Rules.StackPump) continue;
                if (v.Rules.Role == FlowRole.Valve || v.Rules.Directional) continue;
                if (!neighbours.TryGetValue(v, out var around)) continue;

                foreach (ZtxFlowNode n in around)
                {
                    if (!n.Rules.StackPump) continue;
                    if (!indexByNode.TryGetValue(n, out int nId)) continue;
                    if (SideOf(v, n) > 0)
                        TryPump(graph, seenPumps, v, entry.Value, n, nId);   // self -> peer in front
                }
            }

            return graph;
        }


        private static void TryConnect(BloodGraph graph, HashSet<long> seen,
                                       ZtxFlowNode from, int fromId, ZtxFlowNode to, int toId)
        {
            if (!seen.Add(((long)fromId << 32) | (uint)toId)) return;

            ConsideredEdgeCount++;

            if (!PermitsOutflow(from, to) || !PermitsInflow(to, from))
            {
                BlockedEdgeCount++;
                return;
            }

            graph.Connect(fromId, toId);
            DirectedEdgeCount++;
            if (fromId < toId) EdgeCount++;
        }

        private static bool PermitsOutflow(ZtxFlowNode self, ZtxFlowNode other)
        {
            if (!self.Rules.Directional) return true;
            return SideOf(self, other) > 0;          // strictly in front
        }

        private static bool PermitsInflow(ZtxFlowNode self, ZtxFlowNode other)
        {
            if (!self.Rules.Directional) return true;
            // A directional pressure source is an origin, never a destination: blood must not
            // run back into the heart even from behind.
            if (self.Rules.Role == FlowRole.Source) return false;
            return SideOf(self, other) < 0;          // strictly behind
        }

        private static int SideOf(ZtxFlowNode self, ZtxFlowNode other)
        {
            return other?.Part == null ? 0 : SideOfPart(self, other.Part);
        }

        private static int SideOfPart(ZtxFlowNode self, Part otherPart)
        {
            Part sp = self.Part;
            if (sp == null || otherPart == null) return 0;

            IntVector2 fwd = ForwardOf(self, sp);
            IntRect sr = new PartInfo(sp).PhysicalRect;
            IntRect or = new PartInfo(otherPart).PhysicalRect;

            if (fwd.Y < 0) return or.Bottom <= sr.Top ? 1 : (or.Top >= sr.Bottom ? -1 : 0);
            if (fwd.Y > 0) return or.Top >= sr.Bottom ? 1 : (or.Bottom <= sr.Top ? -1 : 0);
            if (fwd.X < 0) return or.Right <= sr.Left ? 1 : (or.Left >= sr.Right ? -1 : 0);
            if (fwd.X > 0) return or.Left >= sr.Right ? 1 : (or.Right <= sr.Left ? -1 : 0);
            return 0;
        }

        private static bool FacePermits(ZtxFlowNode self, ZtxFlowNode other,
                                        Dictionary<Part, List<ZtxFlowNode>> byPart)
        {
            Part op = other?.Part;
            if (op == null) return false;

            if (self.Rules.FaceOnly) return SideOfPart(self, op) > 0;

            if (byPart.TryGetValue(self.Part, out var siblings))
            {
                for (int i = 0; i < siblings.Count; i++)
                {
                    ZtxFlowNode sib = siblings[i];
                    if (sib == self || !sib.Rules.FaceOnly) continue;
                    if (sib.Rules.Network != self.Rules.Network) continue;
                    if (SideOfPart(sib, op) > 0) return false;
                }
            }
            return true;
        }

        private static IntVector2 ForwardOf(ZtxFlowNode node, Part part)
        {
            return node.Rules.Facing.GetShipRelative(part.Rotation, part.FlipX).ToVector();
        }

        public static string DescribeFacing(ZtxFlowNode node)
        {
            if (node?.Part == null) return "?";
            IntVector2 v = ForwardOf(node, node.Part);
            if (v.X > 0) return "Right";
            if (v.X < 0) return "Left";
            if (v.Y < 0) return "Up";
            if (v.Y > 0) return "Down";
            return "?";
        }

        private static void TryPump(BloodGraph graph, HashSet<long> seen,
                                    ZtxFlowNode from, int fromId, ZtxFlowNode to, int toId)
        {
            if (fromId == toId) return;
            if (!PermitsOutflow(from, to)) return;
            if (!PermitsInflow(to, from)) return;
            if (!seen.Add(((long)fromId << 32) | (uint)toId)) return;

            graph.AddPump(fromId, toId);
            PumpCount++;
        }

        public static int ScaleSignature(List<ZtxFlowNode> nodes)
        {
            int h = 17;
            for (int i = 0; i < nodes.Count; i++)
            {
                ZtxFlowNode fn = nodes[i];
                if (fn?.Part == null) continue;
                h = h * 31 + fn.EffectivePMax;
                h = h * 31 + fn.EffectiveFlowRate;
            }
            return h;
        }

        public static void RefreshScaling(Dictionary<ZtxFlowNode, BloodNode> map)
        {
            foreach (KeyValuePair<ZtxFlowNode, BloodNode> entry in map)
            {
                entry.Value.PMax = entry.Key.EffectivePMax;
                entry.Value.FlowRate = entry.Key.EffectiveFlowRate;
            }
        }

        public static void ReadForward(BloodGraph graph, Dictionary<ZtxFlowNode, BloodNode> map)
        {
            foreach (KeyValuePair<ZtxFlowNode, BloodNode> entry in map)
            {
                int live = entry.Key.Volume;
                BloodNode core = entry.Value;
                core.Volume = live > core.Capacity ? core.Capacity : (live < 0 ? 0 : live);
            }
        }

        public static int WriteBack(BloodGraph graph, Dictionary<ZtxFlowNode, BloodNode> map)
        {
            int written = 0;
            foreach (KeyValuePair<ZtxFlowNode, BloodNode> entry in map)
            {
                int volume = entry.Value.Volume;
                if (entry.Key.Volume != volume)
                {
                    entry.Key.Volume = volume;
                    written++;
                }
                entry.Key.Pressure = entry.Value.Pressure;
            }
            return written;
        }

        private static IEnumerable<ZtxFlowNode> GetNetworkNeighbours(
            ZtxFlowNode self, Dictionary<Part, List<ZtxFlowNode>> byPart)
        {
            Part part = self.Part;
            if (part == null) yield break;
            string wantedNetwork = self.Rules.Network;

            IReadOnlyList<PartComponent> comps = part.Components;
            for (int i = 0; i < comps.Count; i++)
            {
                var port = comps[i] as BasePartNetworkPort;
                if (port == null) continue;
                if (!PortCarriesNetwork(port, wantedNetwork)) continue;

                foreach (ComponentNode<Part, PartComponent> node in NodesOf(port))
                {
                    foreach (var route in node.OutboundRoutes)
                    {
                        ZtxFlowNode other = FlowNodeOf(route.Endpoint, byPart, wantedNetwork, part);
                        if (other != null && other != self && FacePermits(self, other, byPart))
                            yield return other;
                    }
                    foreach (var route in node.InboundRoutes)
                    {
                        ZtxFlowNode other = FlowNodeOf(route.Origin, byPart, wantedNetwork, part);
                        if (other != null && other != self && FacePermits(self, other, byPart))
                            yield return other;
                    }
                }
            }
        }

        private static bool PortCarriesNetwork(BasePartNetworkPort port, string network)
        {
            var types = port.Rules?.Types;
            if (types == null || string.IsNullOrEmpty(network)) return false;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i].ToString() == network) return true;
            }
            return false;
        }

        private static IEnumerable<ComponentNode<Part, PartComponent>> NodesOf(BasePartNetworkPort port)
        {
            ComponentNode<Part, PartComponent> endpoint = port.RouteEndpoint?.NetworkNode;
            if (endpoint != null) yield return endpoint;

            ComponentNode<Part, PartComponent> source = port.RouteSource?.NetworkNode;
            if (source != null && source != endpoint) yield return source;
        }

        private static ZtxFlowNode FlowNodeOf(
            ComponentNode<Part, PartComponent> node,
            Dictionary<Part, List<ZtxFlowNode>> byPart,
            string network,
            Part callerPart)
        {
            Part owner = node?.Component?.Part;
            if (owner == null) return null;
            if (!byPart.TryGetValue(owner, out var onPart)) return null;

            ZtxFlowNode fallback = null;
            for (int i = 0; i < onPart.Count; i++)
            {
                ZtxFlowNode cand = onPart[i];
                if (cand.Rules.Network != network) continue;
                if (cand.Rules.FaceOnly)
                {
                    if (callerPart != null && SideOfPart(cand, callerPart) > 0) return cand;
                }
                else if (fallback == null)
                {
                    fallback = cand;
                }
            }
            return fallback;   // null when the part is on a different network entirely
        }

        private static NodeRole ToCoreRole(FlowRole role)
        {
            switch (role)
            {
                case FlowRole.Source:  return NodeRole.PressureSource;
                case FlowRole.Station: return NodeRole.ResetStation;
                case FlowRole.Valve:   return NodeRole.Valve;
                case FlowRole.Sack:    return NodeRole.Sack;
                case FlowRole.Sink:    return NodeRole.Sink;
                default:               return NodeRole.Pipe;
            }
        }
    }
}
