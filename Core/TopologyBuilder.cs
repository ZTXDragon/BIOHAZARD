using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public readonly struct TopoRect
    {
        public readonly int Left, Top, Right, Bottom;

        public TopoRect(int left, int top, int right, int bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }
    }

    public sealed class TopoNode
    {
        public int Id;
        public int Part;
        public string Network;
        public NodeRole Role;
        public bool Directional;
        public bool FaceOnly;
        public bool StackPump;
        public int FacingX;
        public int FacingY;
    }

    public sealed class TopologyStats
    {
        public int Edges, DirectedEdges, Pumps, Considered, Blocked;
    }

    public static class TopologyBuilder
    {
        public static List<int>[] Wire(BloodGraph graph, IReadOnlyList<TopoNode> nodes, IReadOnlyList<TopoRect> parts,
                                       IEnumerable<(int Node, int OtherPart)> links, TopologyStats stats)
        {
            stats = stats ?? new TopologyStats();
            for (int i = 1; i < nodes.Count; i++)
            {
                if (nodes[i].Id <= nodes[i - 1].Id)
                    throw new System.ArgumentException("TopologyBuilder.Wire: node ids must rise with the index; node " + i +
                                                       " has id " + nodes[i].Id + " after " + nodes[i - 1].Id);
            }
            var byPart = new Dictionary<int, List<int>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!byPart.TryGetValue(nodes[i].Part, out var onPart))
                    byPart[nodes[i].Part] = onPart = new List<int>();
                onPart.Add(i);
            }

            var neighbours = new List<int>[nodes.Count];
            var seenOrdered = new HashSet<long>();
            foreach ((int a, int otherPart) in links)
            {
                int b = NodeOf(otherPart, nodes[a].Network, nodes[a].Part, nodes, parts, byPart);
                if (b < 0 || b == a) continue;
                if (!FacePermits(a, otherPart, nodes, parts, byPart)) continue;
                List<int> list = neighbours[a] ?? (neighbours[a] = new List<int>());
                if (!list.Contains(b)) list.Add(b);
                TryConnect(graph, seenOrdered, a, b, nodes, parts, stats);
                TryConnect(graph, seenOrdered, b, a, nodes, parts, stats);
            }

            var seenPumps = new HashSet<long>();
            for (int v = 0; v < nodes.Count; v++)
            {
                TopoNode vn = nodes[v];
                if (vn.Role != NodeRole.Valve && !vn.Directional) continue;
                if (neighbours[v] == null) continue;
                foreach (int n in neighbours[v])
                {
                    int side = SideOf(vn, parts[vn.Part], parts[nodes[n].Part]);
                    if (side < 0) TryPump(graph, seenPumps, n, v, nodes, parts, stats);
                    else if (side > 0) TryPump(graph, seenPumps, v, n, nodes, parts, stats);
                }
            }

            for (int v = 0; v < nodes.Count; v++)
            {
                TopoNode vn = nodes[v];
                if (!vn.StackPump || vn.Role == NodeRole.Valve || vn.Directional) continue;
                if (neighbours[v] == null) continue;
                foreach (int n in neighbours[v])
                {
                    if (!nodes[n].StackPump) continue;
                    if (SideOf(vn, parts[vn.Part], parts[nodes[n].Part]) > 0)
                        TryPump(graph, seenPumps, v, n, nodes, parts, stats);
                }
            }
            return neighbours;
        }

        public static int SideOf(TopoNode self, TopoRect sr, TopoRect or)
        {
            if (self.FacingY < 0) return or.Bottom <= sr.Top ? 1 : (or.Top >= sr.Bottom ? -1 : 0);
            if (self.FacingY > 0) return or.Top >= sr.Bottom ? 1 : (or.Bottom <= sr.Top ? -1 : 0);
            if (self.FacingX < 0) return or.Right <= sr.Left ? 1 : (or.Left >= sr.Right ? -1 : 0);
            if (self.FacingX > 0) return or.Left >= sr.Right ? 1 : (or.Right <= sr.Left ? -1 : 0);
            return 0;
        }

        private static int NodeOf(int part, string network, int callerPart, IReadOnlyList<TopoNode> nodes,
                                  IReadOnlyList<TopoRect> parts, Dictionary<int, List<int>> byPart)
        {
            if (!byPart.TryGetValue(part, out var onPart)) return -1;
            int fallback = -1;
            foreach (int cand in onPart)
            {
                TopoNode c = nodes[cand];
                if (c.Network != network) continue;
                if (c.FaceOnly)
                {
                    if (SideOf(c, parts[c.Part], parts[callerPart]) > 0) return cand;
                }
                else if (fallback < 0)
                {
                    fallback = cand;
                }
            }
            return fallback;
        }

        private static bool FacePermits(int self, int otherPart, IReadOnlyList<TopoNode> nodes,
                                        IReadOnlyList<TopoRect> parts, Dictionary<int, List<int>> byPart)
        {
            TopoNode s = nodes[self];
            if (s.FaceOnly) return SideOf(s, parts[s.Part], parts[otherPart]) > 0;
            foreach (int sib in byPart[s.Part])
            {
                if (sib == self) continue;
                TopoNode sn = nodes[sib];
                if (!sn.FaceOnly || sn.Network != s.Network) continue;
                if (SideOf(sn, parts[sn.Part], parts[otherPart]) > 0) return false;
            }
            return true;
        }

        private static bool PermitsOutflow(int self, int other, IReadOnlyList<TopoNode> nodes, IReadOnlyList<TopoRect> parts)
        {
            TopoNode s = nodes[self];
            if (!s.Directional) return true;
            return SideOf(s, parts[s.Part], parts[nodes[other].Part]) > 0;
        }

        private static bool PermitsInflow(int self, int other, IReadOnlyList<TopoNode> nodes, IReadOnlyList<TopoRect> parts)
        {
            TopoNode s = nodes[self];
            if (!s.Directional) return true;
            if (s.Role == NodeRole.PressureSource) return false;
            return SideOf(s, parts[s.Part], parts[nodes[other].Part]) < 0;
        }

        private static void TryConnect(BloodGraph graph, HashSet<long> seen, int from, int to,
                                       IReadOnlyList<TopoNode> nodes, IReadOnlyList<TopoRect> parts, TopologyStats stats)
        {
            if (!seen.Add(((long)from << 32) | (uint)to)) return;
            stats.Considered++;
            if (!PermitsOutflow(from, to, nodes, parts) || !PermitsInflow(to, from, nodes, parts))
            {
                stats.Blocked++;
                return;
            }
            graph.Connect(nodes[from].Id, nodes[to].Id);
            stats.DirectedEdges++;
            if (from < to) stats.Edges++;
        }

        private static void TryPump(BloodGraph graph, HashSet<long> seen, int from, int to,
                                    IReadOnlyList<TopoNode> nodes, IReadOnlyList<TopoRect> parts, TopologyStats stats)
        {
            if (from == to) return;
            if (!PermitsOutflow(from, to, nodes, parts)) return;
            if (!PermitsInflow(to, from, nodes, parts)) return;
            if (!seen.Add(((long)from << 32) | (uint)to)) return;
            graph.AddPump(nodes[from].Id, nodes[to].Id);
            stats.Pumps++;
        }
    }
}
