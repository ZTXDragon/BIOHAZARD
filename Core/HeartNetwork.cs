using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public static class HeartNetwork
    {
        public static float[] ComponentSums(int count, IEnumerable<(int A, int B)> links, IReadOnlyList<float> values)
        {
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            foreach ((int a, int b) in links)
            {
                if (a < 0 || b < 0 || a >= count || b >= count) continue;
                int ra = Find(parent, a), rb = Find(parent, b);
                if (ra != rb) parent[ra] = rb;
            }
            var total = new float[count];
            for (int i = 0; i < count; i++) total[Find(parent, i)] += values[i];
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = total[Find(parent, i)];
            return result;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }
    }
}
