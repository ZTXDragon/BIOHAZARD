using System;
using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    // The BIOMASS transport model: passive equalization, thermal-style.
    public static class DiffusionStep
    {
        public static void Tick(BloodGraph g)
        {
            g.EnsureSorted();

            var delta = g.DeltaScratch;
            delta.Clear();
            foreach (var a in g.Nodes)
            {
                if (!a.Diffusion || a.PumpDriven) continue;
                if (!g.Downstream.TryGetValue(a.Id, out var outs) || outs.Count == 0) continue;

                int budget = a.FlowRate;
                if (budget > a.Volume) budget = a.Volume;
                if (budget <= 0) continue;

                // Count eligible receivers first so the budget splits fairly between them.
                int eligible = 0;
                foreach (var bId in outs)
                {
                    var nb = g.NodeById(bId);
                    if (nb == null || !nb.Diffusion || nb.PumpDriven) continue;
                    if (WantedTransfer(a, nb) > 0) eligible++;
                }
                if (eligible == 0) continue;

                int count = outs.Count;
                int start = (a.SplitCursor & 0x7FFFFFFF) % count;
                a.SplitCursor++;

                for (int k = 0; k < count; k++)
                {
                    if (budget <= 0) break;
                    int idx = start + k;
                    if (idx >= count) idx -= count;
                    var b = g.NodeById(outs[idx]);
                    if (b == null || !b.Diffusion || b.PumpDriven) continue;

                    int want = WantedTransfer(a, b);
                    if (want <= 0) continue;

                    int share = budget / eligible;
                    if (share < 1) share = 1;
                    if (want > share) want = share;
                    if (want > budget) want = budget;

                    // Destination room accounts for what it is already scheduled to receive
                    // this tick, so concurrent senders cannot jointly overfill it.
                    delta.TryGetValue(b.Id, out int inbound);
                    int room = b.RemainingCapacity - inbound;
                    if (want > room) want = room;
                    if (want <= 0) { eligible--; if (eligible < 1) eligible = 1; continue; }

                    delta.TryGetValue(a.Id, out int aD); delta[a.Id] = aD - want;
                    delta.TryGetValue(b.Id, out int bD); delta[b.Id] = bD + want;
                    budget -= want;
                    eligible--;
                    if (eligible < 1) eligible = 1;
                }
            }

            foreach (var kv in delta)
            {
                var n = g.NodeById(kv.Key);
                if (n != null) n.Volume += kv.Value;
            }
        }

        private static int WantedTransfer(BloodNode a, BloodNode b)
        {
            int d = a.Volume - b.Volume;
            if (d <= 1) return 0;
            return d / 2;
        }
    }
}
