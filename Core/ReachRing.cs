using System;

namespace ZTX.BioCirculation.Core
{
    // A hauling organ's reach, and the ring that shows it in build mode (2026-09-26, ZTX: "so
    // player can see in the build mode how far it can reach and what it can service", drawn "like
    public static class ReachRing
    {
        public const float SegmentTiles = 1f;

        public const int MinSegments = 24;

        public static float Tiles(int range)
        {
            return range > 0 ? range : 0f;
        }

        public static int SegmentCount(float radius)
        {
            if (radius <= 0f) return 0;
            int n = (int)Math.Ceiling(2.0 * Math.PI * radius / SegmentTiles);
            return n < MinSegments ? MinSegments : n;
        }

        public static void Vertex(float radius, int k, int n, out float x, out float y)
        {
            double a = n > 0 ? 2.0 * Math.PI * (k % n) / n : 0.0;
            x = (float)(Math.Cos(a) * radius);
            y = (float)(Math.Sin(a) * radius);
        }
    }
}
