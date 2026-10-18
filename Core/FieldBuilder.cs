using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public static class FieldBuilder
    {
        public static void Build(BloodGraph g)
        {
            foreach (var n in g.Nodes) n.Pressure = 0;
            foreach (var p in g.Nodes)
            {
                if (!p.IsResetPoint || p.PMax <= 0) continue;

                if (p.PropagatePressure)
                {
                    PaintFrom(g, p);
                }
                else if (p.PMax > p.Pressure)
                {
                    p.Pressure = p.PMax;
                }
            }

            foreach (var n in g.Nodes)
            {
                if (n.PinPressureZero) n.Pressure = 0;
            }
        }

        private static void PaintFrom(BloodGraph g, BloodNode source)
        {
            foreach (var kv in ReachOf(g, source.Id))
            {
                var node = g.NodeById(kv.Key);
                if (node != null && kv.Value > node.Pressure) node.Pressure = kv.Value;
            }
        }

        public static Dictionary<int, int> ReachOf(BloodGraph g, int sourceId)
        {
            var result = new Dictionary<int, int>();
            var source = g.NodeById(sourceId);
            if (source == null || !source.IsResetPoint || source.PMax <= 0) return result;
            if (!source.PropagatePressure)
            {
                result[sourceId] = source.PMax;
                return result;
            }
            var dist = new Dictionary<int, int> { [sourceId] = 0 };
            var pq = new SortedSet<(int d, int id)> { (0, sourceId) };
            while (pq.Count > 0)
            {
                var cur = pq.Min; pq.Remove(cur);
                int d = cur.d, id = cur.id;
                if (d > dist[id]) continue;
                int contribution = source.PMax - d;
                if (contribution <= 0) continue;
                if (!result.TryGetValue(id, out int had) || contribution > had) result[id] = contribution;
                if (!g.Downstream.TryGetValue(id, out var outs)) continue;
                foreach (var nbId in outs)
                {
                    var nb = g.NodeById(nbId);
                    if (nb == null) continue;
                    int nd = d + nb.Decay;
                    if (!dist.TryGetValue(nbId, out var old) || nd < old)
                    {
                        dist[nbId] = nd;
                        pq.Add((nd, nbId));
                    }
                }
            }
            return result;
        }

        public static int ReachPipes(int pmax, int decay)
        {
            if (pmax <= 0 || decay <= 0) return 0;
            return (pmax - 1) / decay;
        }
    }
}
