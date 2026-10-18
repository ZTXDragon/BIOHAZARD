using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public enum MultiMode { All, Any, One, None }

    public static class BlueprintToggle
    {
        public static bool? Combine(MultiMode mode, IReadOnlyList<bool?> subs, bool invert)
        {
            int on = 0, off = 0, unknown = 0;
            foreach (bool? s in subs)
            {
                if (!s.HasValue) unknown++;
                else if (s.Value) on++;
                else off++;
            }
            bool? flag;
            switch (mode)
            {
                case MultiMode.All:
                    flag = off > 0 ? false : unknown > 0 ? (bool?)null : true;
                    break;
                case MultiMode.Any:
                    flag = on > 0 ? true : unknown > 0 ? (bool?)null : false;
                    break;
                case MultiMode.One:
                    flag = on > 1 ? false : unknown > 0 ? (bool?)null : on == 1;
                    break;
                default:
                    flag = on > 0 ? false : unknown > 0 ? (bool?)null : true;
                    break;
            }
            if (!flag.HasValue) return null;
            return flag.Value != invert;
        }

        public static bool IsOn(bool? state)
        {
            return state ?? true;
        }
    }
}
