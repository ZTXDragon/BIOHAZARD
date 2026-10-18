using System.Collections.Generic;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer;
using Halfling.Geometry;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
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

            var ordered = new List<ZtxFlowNode>();
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
                ordered.Add(fn);
                nodeByFlow[fn] = core;
            }

            var stats = new TopologyStats();
            BuildTopology(graph, ordered, indexByNode, stats);
            EdgeCount = stats.Edges;
            DirectedEdgeCount = stats.DirectedEdges;
            PumpCount = stats.Pumps;
            ConsideredEdgeCount = stats.Considered;
            BlockedEdgeCount = stats.Blocked;
            return graph;
        }

        private static IEnumerable<int> CollectLinks(ZtxFlowNode self, Dictionary<Part, int> partIndex)
        {
            Part part = self.Part;
            if (part == null) yield break;
            IReadOnlyList<PartComponent> comps = part.Components;
            for (int i = 0; i < comps.Count; i++)
            {
                var port = comps[i] as BasePartNetworkPort;
                if (port == null || !PortCarriesNetwork(port.Rules, self.Rules.Network)) continue;
                foreach (ComponentNode<Part, PartComponent> node in NodesOf(port))
                {
                    foreach (var route in node.OutboundRoutes)
                        if (route.Endpoint?.Component?.Part is Part p && partIndex.TryGetValue(p, out int idx)) yield return idx;
                    foreach (var route in node.InboundRoutes)
                        if (route.Origin?.Component?.Part is Part p && partIndex.TryGetValue(p, out int idx)) yield return idx;
                }
            }
        }

        private static void BuildTopology(BloodGraph graph, List<ZtxFlowNode> ordered,
                                          Dictionary<ZtxFlowNode, int> indexByNode, TopologyStats stats)
        {
            var partIndex = new Dictionary<Part, int>();
            var rects = new List<TopoRect>();
            var nodes = new List<TopoNode>(ordered.Count);
            foreach (ZtxFlowNode fn in ordered)
            {
                Part p = fn.Part;
                if (!partIndex.TryGetValue(p, out int pi))
                {
                    pi = rects.Count;
                    partIndex[p] = pi;
                    IntRect r = new PartInfo(p).PhysicalRect;
                    rects.Add(new TopoRect(r.Left, r.Top, r.Right, r.Bottom));
                }
                IntVector2 f = ForwardOf(fn, p);
                nodes.Add(new TopoNode
                {
                    Id = indexByNode[fn], Part = pi, Network = fn.Rules.Network, Role = ToCoreRole(fn.Rules.Role),
                    Directional = fn.Rules.Directional, FaceOnly = fn.Rules.FaceOnly,
                    StackPump = fn.Rules.StackPump, FacingX = f.X, FacingY = f.Y,
                });
            }
            var links = new List<(int, int)>();
            for (int i = 0; i < ordered.Count; i++)
                foreach (int other in CollectLinks(ordered[i], partIndex)) links.Add((i, other));
            TopologyBuilder.Wire(graph, nodes, rects, links, stats);
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

        internal static bool PortCarriesNetwork(BasePartNetworkPortRules rules, string network)
        {
            var types = rules?.Types;
            if (types == null || string.IsNullOrEmpty(network)) return false;
            for (int i = 0; i < types.Length; i++)
            {
                if (PortNetwork.Carries(types[i].ToString(), network)) return true;
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

        internal static NodeRole ToCoreRole(FlowRole role)
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
