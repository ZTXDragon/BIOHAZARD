using System;
using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    // Builds a sight cone out of overlapping discs, because the engine has no wedge.
    public static class ConeGeometry
    {
        private const float SmoothFactor = 0.40f;

        public static List<Disc> Build(float ox, float oy, float dir, float halfAngle,
                                       float range, int maxDiscs, float start = 1f)
        {
            var list = new List<Disc>();
            if (maxDiscs <= 0 || halfAngle <= 0f || range <= 0f) return list;

            float s = (float)Math.Sin(halfAngle);
            if (s <= 0f) return list;

            float cos = (float)Math.Cos(dir);
            float sin = (float)Math.Sin(dir);

            // Hard limit: beyond this ratio neighbours separate and the cone breaks into beads.
            //   d2 - d1 <= s*(d1 + d2)  =>  d2/d1 <= (1+s)/(1-s)
            float kMax = s >= 1f ? float.MaxValue : (1f + s) / (1f - s);
            float kSmooth = 1f + SmoothFactor * s;
            if (kSmooth > kMax) kSmooth = kMax;

            float last = range / (1f + s);

            if (start < 0.01f) start = 0.01f;
            if (start > last) start = last;

            int discs = 1;
            if (start < last && kSmooth > 1f)
            {
                double needed = Math.Log(last / start) / Math.Log(kSmooth);
                discs = (int)Math.Ceiling(needed) + 1;
            }
            if (discs > maxDiscs) discs = maxDiscs;
            if (discs < 1) discs = 1;

            float first = last;
            float k = 1f;
            if (discs > 1)
            {
                // If the budget binds, pull the start outward so neighbours still overlap.
                float minFirst = last / (float)Math.Pow(kMax, discs - 1);
                first = start > minFirst ? start : minFirst;
                k = (float)Math.Pow(last / first, 1.0 / (discs - 1));
            }

            float d = first;
            for (int i = 0; i < discs; i++)
            {
                if (i == discs - 1) d = last;           // the nose lands exactly on range
                list.Add(new Disc(ox + cos * d, oy + sin * d, d * s));
                d *= k;
            }
            return list;
        }
    }
}
