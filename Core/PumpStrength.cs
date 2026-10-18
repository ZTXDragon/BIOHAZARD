using System;

namespace ZTX.BioCirculation.Core
{
    public static class PumpStrength
    {
        public static int PMax(int authored, float buffScale, int percent)
        {
            if (authored <= 0) return 0;
            return (int)Math.Round(authored * buffScale * Fraction(percent));
        }

        public static int FlowRate(int authored, float buffScale, int percent)
        {
            float scale = buffScale * Fraction(percent);
            if (scale <= 0f) return 0;
            int r = (int)Math.Round(authored * scale);
            return r < 1 ? 1 : r;
        }

        private static float Fraction(int percent)
        {
            return percent > 0 ? percent / 100f : 0f;
        }
    }
}
