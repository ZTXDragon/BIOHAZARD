using System;
using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public static class FlowStep
    {
        private static bool HasOutboundPump(BloodGraph g, int nodeId) => g.PumpSources.Contains(nodeId);

        public static void Tick(BloodGraph g, float gradientGain, int ticksPerSecond = 1)
        {
            g.EnsureSorted();

            if (ticksPerSecond < 1) ticksPerSecond = 1;
            foreach (var n in g.Nodes)
            {
                // Bank this tick's slice of the per-second rate, release whole units, carry
                // the remainder. Integer division only — see BloodNode.RateCredit.
                n.RateCredit += n.FlowRate;
                n.TickBudget  = n.RateCredit / ticksPerSecond;
                n.RateCredit -= n.TickBudget * ticksPerSecond;
            }

            // PASS 1 — valve pumps
            foreach (var (fromId, toId) in g.Pumps)
            {
                var to = g.NodeById(toId);
                if (to == null) continue;
                if (to.Role == NodeRole.Valve && !HasOutboundPump(g, toId)) continue;
                var from = g.NodeById(fromId);
                if (from == null) continue;
                var governor = to.Role == NodeRole.Valve ? to : from;
                int move = governor.TickBudget;
                // Retention floor: a pump can only take what the source holds ABOVE
                // its Retain — same rule as gradient emission below.
                int available = from.Volume - from.Retain;
                if (available < 0) available = 0;
                if (move > available) move = available;
                // FlowRoom, not RemainingCapacity: the destination's reserve is kept
                // free for its own converter (the distributor tap).
                if (move > to.FlowRoom) move = to.FlowRoom;
                if (move <= 0) continue;
                governor.TickBudget -= move;
                from.Volume -= move;
                to.Volume   += move;
            }

            var delta = g.DeltaScratch;
            delta.Clear();
            foreach (var a in g.Nodes)
            {
                if (a.PumpDriven || a.Diffusion) continue;
                if (!g.Downstream.TryGetValue(a.Id, out var outs) || outs.Count == 0) continue;

                int budget = a.TickBudget;
                int surplus = a.Volume - a.Retain;
                if (surplus < 0) surplus = 0;
                if (budget > surplus) budget = surplus;
                if (budget <= 0) continue;

                // Count downhill neighbours first so the budget can be split fairly between
                // them rather than served first-come.
                int downhill = 0;
                foreach (var bId in outs)
                {
                    var nb = g.NodeById(bId);
                    if (nb == null || nb.PumpDriven || nb.Diffusion) continue;
                    if (a.Pressure - nb.Pressure > 0) downhill++;
                }
                if (downhill == 0) continue;

                int count = outs.Count;
                int start = (a.SplitCursor & 0x7FFFFFFF) % count;
                a.SplitCursor++;

                for (int k = 0; k < count; k++)
                {
                    if (budget <= 0) break;
                    int idx = start + k;
                    if (idx >= count) idx -= count;
                    int bId = outs[idx];
                    var b = g.NodeById(bId);
                    if (b == null || b.PumpDriven || b.Diffusion) continue;

                    int drop = a.Pressure - b.Pressure;
                    if (drop <= 0) continue;

                    int want = (int)Math.Round(gradientGain * drop, MidpointRounding.AwayFromZero);
                    if (want < 1) want = 1;

                    int share = budget / downhill;
                    if (share < 1) share = 1;
                    if (want > share) want = share;
                    if (want > budget) want = budget;

                    delta.TryGetValue(b.Id, out int inbound);
                    // FlowRoom, not RemainingCapacity: see the pump pass above.
                    int room = b.FlowRoom - inbound;
                    if (want > room) want = room;
                    if (want <= 0) { downhill--; if (downhill < 1) downhill = 1; continue; }

                    delta.TryGetValue(a.Id, out int aD); delta[a.Id] = aD - want;
                    delta.TryGetValue(b.Id, out int bD); delta[b.Id] = bD + want;
                    budget -= want;
                    downhill--;
                    if (downhill < 1) downhill = 1;
                }

                a.TickBudget = budget;
            }

            foreach (var kv in delta)
            {
                var n = g.NodeById(kv.Key);
                if (n != null) n.Volume += kv.Value;
            }
        }
    }
}
