using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    // One damaged part a distributor could feed, in ship grid coordinates.
    public readonly struct HealCandidate
    {
        public readonly int X;
        public readonly int Y;

        public readonly int Deficit;

        public readonly int Urgency;

        public HealCandidate(int x, int y, int deficit, int urgency = 0)
        {
            X = x; Y = y; Deficit = deficit; Urgency = urgency;
        }
    }

    // Shares a distributor's banked healing energy across the damaged bio parts around it.
    public static class HealSpread
    {
        public static int Allocate(int originX, int originY, int radius, int available,
                                   IReadOnlyList<HealCandidate> candidates, int[] amounts)
        {
            if (candidates == null || amounts == null) return 0;

            int n = candidates.Count;
            if (amounts.Length < n) return 0;
            for (int i = 0; i < n; i++) amounts[i] = 0;
            if (available <= 0) return 0;

            // Square reach (Chebyshev), matching the square area the highlight draws. A
            // circular reach would look wrong against the overlay the player is aiming with.
            var want = new int[n];
            int wanting = 0;
            for (int i = 0; i < n; i++)
            {
                HealCandidate c = candidates[i];
                int dx = c.X - originX; if (dx < 0) dx = -dx;
                int dy = c.Y - originY; if (dy < 0) dy = -dy;
                if (dx > radius || dy > radius || c.Deficit <= 0) continue;
                want[i] = c.Deficit;
                wanting++;
            }
            if (wanting == 0) return 0;

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = 1; i < n; i++)
            {
                int cur = order[i];
                int u = candidates[cur].Urgency;
                int j = i - 1;
                while (j >= 0 && candidates[order[j]].Urgency > u)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = cur;
            }

            int total = 0;
            while (available > 0 && wanting > 0)
            {
                int share = available / wanting;
                if (share == 0)
                {
                    for (int k = 0; k < n && available > 0; k++)
                    {
                        int i = order[k];
                        if (want[i] <= 0) continue;
                        amounts[i]++; want[i]--; available--; total++;
                    }
                    break;
                }

                wanting = 0;
                for (int k = 0; k < n; k++)
                {
                    int i = order[k];
                    if (want[i] <= 0) continue;
                    int give = share < want[i] ? share : want[i];
                    amounts[i] += give;
                    want[i] -= give;
                    available -= give;
                    total += give;
                    if (want[i] > 0) wanting++;   // still hungry: eligible for the next pass
                }
            }
            return total;
        }
    }
}
