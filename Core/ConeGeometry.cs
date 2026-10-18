using System;
using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public static class ConeGeometry
    {
        private const float SmoothFactor = 0.40f;

        public static List<Disc> Build(float ox, float oy, float dir, float halfAngle,
                                       float range, int maxDiscs, ITrig trig, float start = 1f)
        {
            var list = new List<Disc>();
            if (maxDiscs <= 0 || halfAngle <= 0f || range <= 0f) return list;

            float s = (float)trig.Sin(halfAngle);
            if (s <= 0f) return list;

            float cos = (float)trig.Cos(dir);
            float sin = (float)trig.Sin(dir);

            float kMax = s >= 1f ? float.MaxValue : (1f + s) / (1f - s);
            float kSmooth = 1f + SmoothFactor * s;
            if (kSmooth > kMax) kSmooth = kMax;

            float last = range / (1f + s);

            if (start < 0.01f) start = 0.01f;
            if (start > last) start = last;

            int discs = 1;
            if (start < last && kSmooth > 1f)
            {
                double needed = trig.Log(last / start) / trig.Log(kSmooth);
                discs = (int)Math.Ceiling(needed) + 1;
            }
            if (discs > maxDiscs) discs = maxDiscs;
            if (discs < 1) discs = 1;

            float first = last;
            float k = 1f;
            if (discs > 1)
            {
                float minFirst = last / (float)trig.Pow(kMax, discs - 1);
                first = start > minFirst ? start : minFirst;
                k = (float)trig.Pow(last / first, 1.0 / (discs - 1));
            }

            float d = first;
            for (int i = 0; i < discs; i++)
            {
                if (i == discs - 1) d = last;
                list.Add(new Disc(ox + cos * d, oy + sin * d, d * s));
                d *= k;
            }
            return list;
        }
    }
}
