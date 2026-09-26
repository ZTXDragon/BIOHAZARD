namespace ZTX.BioCirculation.Core
{
    // A run of open rays inside a swept arc: where the eye can actually see.
    public readonly struct ArcSpan
    {
        public readonly int Start;

        public readonly int Length;

        public ArcSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public float CentreOffset(int rayCount, float arcDegrees)
        {
            if (rayCount <= 0) return 0f;
            float step = arcDegrees / rayCount;
            return -arcDegrees * 0.5f + (Start + Length * 0.5f) * step;
        }

        public float Width(int rayCount, float arcDegrees)
        {
            if (rayCount <= 0) return 0f;
            return Length * (arcDegrees / rayCount);
        }
    }

    // Turns a swept arc of blocked/open rays into the one opening the eye actually looks through.
    public static class ArcScan
    {
        public static ArcSpan LargestOpenSpan(bool[] open)
        {
            if (open == null || open.Length == 0) return new ArcSpan(0, 0);

            int bestStart = 0, bestLen = 0;
            int runStart = 0, runLen = 0;
            for (int i = 0; i < open.Length; i++)
            {
                if (open[i])
                {
                    if (runLen == 0) runStart = i;
                    runLen++;
                    if (runLen > bestLen)
                    {
                        bestLen = runLen;
                        bestStart = runStart;
                    }
                }
                else
                {
                    runLen = 0;
                }
            }
            return new ArcSpan(bestStart, bestLen);
        }
    }
}
