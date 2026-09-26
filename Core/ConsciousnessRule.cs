using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    // One brain block's consciousness disc: the block's ship-local centre and its CLUSTER's radius.
    public readonly struct Disc
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Radius;

        public Disc(float x, float y, float radius)
        {
            X = x;
            Y = y;
            Radius = radius;
        }
    }

    // The geometric half of the brain gate, kept free of engine types so it can be tested.
    public static class ConsciousnessRule
    {
        public static bool IsConscious(float x, float y, IReadOnlyList<Disc> discs)
        {
            if (discs == null) return false;
            for (int i = 0; i < discs.Count; i++)
            {
                Disc d = discs[i];
                float dx = x - d.X;
                float dy = y - d.Y;
                if (dx * dx + dy * dy <= d.Radius * d.Radius) return true;
            }
            return false;
        }
    }
}
