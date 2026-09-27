using System;

namespace ZTX.BioCirculation.Core
{
    // How far a tentacle arm is drawn for the work its hauler accepts, and how far it idles.
    public static class ArmReach
    {
        public const float DropBand = 1.25f;

        public static float TilesNeeded(float rangeTiles, float anchorOffset)
        {
            if (rangeTiles <= 0f) return 0f;
            return DropBand * rangeTiles + (anchorOffset > 0f ? anchorOffset : 0f);
        }

        public static int SegmentsRequired(float rangeTiles, float anchorOffset, float segmentLength)
        {
            if (segmentLength <= 0f) return 0;
            float needed = TilesNeeded(rangeTiles, anchorOffset);
            if (needed <= 0f) return 0;
            // The epsilon keeps an exact multiple (1.25 x 4 = 5) from rounding up on float noise.
            return (int)Math.Ceiling(needed / segmentLength - 1e-4);
        }

        public static bool Covers(int segments, float segmentLength, float rangeTiles, float anchorOffset)
        {
            return segments >= SegmentsRequired(rangeTiles, anchorOffset, segmentLength);
        }

        public static float IdleTiles(float rangeTiles, float drawnReach, float fraction)
        {
            if (fraction <= 0f) return 0f;
            float basis = rangeTiles > 0f ? rangeTiles : drawnReach;
            if (basis <= 0f) return 0f;
            float tiles = fraction * basis;
            if (drawnReach > 0f && tiles > drawnReach) tiles = drawnReach;
            return tiles;
        }
    }
}
