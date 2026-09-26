using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Resources;

namespace ZTX.BioCirculation.Game
{
    // Debug switch + formatters for the transfer pipeline (2026-09-12, user request: the chain
    // instability persisted after the rework — log every decision the pipeline makes so the next
    internal static class ZtxHaulDebug
    {
        internal const bool Verbose = true;

        internal static string Sink(IResourceSink s)
        {
            if (s == null) return "<null sink>";
            Part p = s.Part;
            string name = p != null ? p + "@" + p.Location : s.GetType().Name;
            return s.IsConstructionSink ? name + "[construction]" : name;
        }

        internal static string Src(IResourceSource s)
        {
            if (s == null) return "<null source>";
            if (s is Nugget n) return "nugget@" + n.DetWorldLocation;
            Part p = s.Part;
            return p != null ? p + "@" + p.Location : s.GetType().Name;
        }
    }
}
