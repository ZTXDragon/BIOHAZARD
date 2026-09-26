namespace ZTX.BioCirculation.Core
{
    // Pure logic behind the part-rotation toggle.
    public static class PartFacing
    {
        public static int QuarterTurns(int rotFlipBits)
        {
            return rotFlipBits & 3;
        }

        public static int Normalize(int quarterTurns)
        {
            return ((quarterTurns % 4) + 4) % 4;
        }

        public static bool Matches(int rotFlipBits, int wantedQuarterTurns)
        {
            return QuarterTurns(rotFlipBits) == Normalize(wantedQuarterTurns);
        }
    }
}
