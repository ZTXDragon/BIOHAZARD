using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Resources;

namespace ZTX.BioCirculation.Game
{
    internal static class ZtxHaulDebug
    {
        internal static bool Verbose => Config.VerboseLog;

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
