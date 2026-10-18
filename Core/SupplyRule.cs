namespace ZTX.BioCirculation.Core
{
    public static class SupplyRule
    {
        public static bool Covers(float hx, float hy, float range,
                                  float sx, float sy, float tx, float ty)
        {
            if (range <= 0f) return false;
            float r2 = range * range;
            float dx = sx - hx, dy = sy - hy;
            if (dx * dx + dy * dy > r2) return false;
            dx = tx - hx;
            dy = ty - hy;
            return dx * dx + dy * dy <= r2;
        }

        public static int Compare(float distSqA, int availableA, float distSqB, int availableB)
        {
            int c = distSqA.CompareTo(distSqB);
            return c != 0 ? c : availableB.CompareTo(availableA);
        }

        public static int Available(int stock, int outstanding)
        {
            int left = stock - outstanding;
            return left > 0 ? left : 0;
        }
    }
}
