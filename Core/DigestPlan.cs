using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public static class DigestPlan
    {
        public static bool PlanTake(IReadOnlyList<int> tileQuantities, int need, IList<int> takes)
        {
            if (takes == null) return false;
            takes.Clear();
            if (need <= 0 || tileQuantities == null) return false;

            int remaining = need;
            for (int i = 0; i < tileQuantities.Count; i++)
            {
                int have = tileQuantities[i];
                if (have <= 0 || remaining <= 0)
                {
                    takes.Add(0);
                    continue;
                }
                int take = have < remaining ? have : remaining;
                takes.Add(take);
                remaining -= take;
            }

            if (remaining > 0)
            {
                takes.Clear();
                return false;
            }
            return true;
        }
    }
}
