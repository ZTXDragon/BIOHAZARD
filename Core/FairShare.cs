using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    // Splits a limited amount evenly between consumers (2026-09-25, ZTX: "i need the biomass to be
    // spread evenly across all biomass parts that use it like factory, thrusters and bone
    public static class FairShare
    {
        public static int Allocate(int available, IReadOnlyList<int> wants, int start, int[] result)
        {
            int n = wants.Count;
            for (int i = 0; i < n; i++) result[i] = 0;
            if (n == 0 || available <= 0) return 0;

            int total = 0;
            while (available > 0)
            {
                int hungry = 0;
                for (int i = 0; i < n; i++)
                    if (wants[i] - result[i] > 0) hungry++;
                if (hungry == 0) break;

                int share = available / hungry;
                if (share == 0)
                {
                    // Fewer units than hungry consumers: one each from the rotation start.
                    int first = ((start % n) + n) % n;
                    for (int k = 0; k < n && available > 0; k++)
                    {
                        int i = (first + k) % n;
                        if (wants[i] - result[i] <= 0) continue;
                        result[i]++;
                        available--;
                        total++;
                    }
                    break;
                }

                for (int i = 0; i < n; i++)
                {
                    int room = wants[i] - result[i];
                    if (room <= 0) continue;
                    int give = room < share ? room : share;
                    result[i] += give;
                    available -= give;
                    total += give;
                }
            }
            return total;
        }
    }
}
