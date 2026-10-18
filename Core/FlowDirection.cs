using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public enum FlowKind { Downhill, Pump, Still }

    public readonly struct FlowArrow
    {
        public readonly int From;
        public readonly int To;
        public readonly FlowKind Kind;

        public FlowArrow(int from, int to, FlowKind kind) { From = from; To = to; Kind = kind; }
    }

    public static class FlowDirection
    {
        public static List<FlowArrow> Of(BloodGraph g)
        {
            var arrows = new List<FlowArrow>();
            var done = new HashSet<long>();
            foreach ((int from, int to) in g.Pumps)
            {
                if (done.Add(Key(from, to))) arrows.Add(new FlowArrow(from, to, FlowKind.Pump));
            }
            foreach (var kv in g.Downstream)
            {
                foreach (int to in kv.Value)
                {
                    int from = kv.Key;
                    if (!done.Add(Key(from, to))) continue;
                    BloodNode a = g.NodeById(from), b = g.NodeById(to);
                    if (a == null || b == null) continue;
                    FlowArrow arrow = new FlowArrow(from < to ? from : to, from < to ? to : from, FlowKind.Still);
                    if (!a.PumpDriven && !b.PumpDriven && !a.Diffusion && !b.Diffusion)
                    {
                        if (a.Pressure > b.Pressure && Has(g, from, to)) arrow = new FlowArrow(from, to, FlowKind.Downhill);
                        else if (b.Pressure > a.Pressure && Has(g, to, from)) arrow = new FlowArrow(to, from, FlowKind.Downhill);
                    }
                    arrows.Add(arrow);
                }
            }
            return arrows;
        }

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static bool Has(BloodGraph g, int from, int to)
            => g.Downstream.TryGetValue(from, out var outs) && outs.Contains(to);
    }
}
