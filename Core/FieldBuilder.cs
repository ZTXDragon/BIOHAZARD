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
            var dist = new Dictionary<int, int> { [source.Id] = 0 };
            var pq = new SortedSet<(int d, int id)> { (0, source.Id) };
            while (pq.Count > 0)
            {
                var cur = pq.Min; pq.Remove(cur);
                int d = cur.d, id = cur.id;
                if (d > dist[id]) continue;          // stale entry
                int contribution = source.PMax - d;
                if (contribution <= 0) continue;     // beyond reach: prune
                var node = g.NodeById(id);
                if (contribution > node.Pressure) node.Pressure = contribution;
                if (!g.Downstream.TryGetValue(id, out var outs)) continue;
                foreach (var nbId in outs)
                {
                    var nb = g.NodeById(nbId);
                    if (nb == null) continue;
                    int nd = d + nb.Decay;           // cost to ENTER nb
                    if (!dist.TryGetValue(nbId, out var old) || nd < old)
                    {
                        dist[nbId] = nd;
                        pq.Add((nd, nbId));
                    }
                }
            }
        }
    }
}
