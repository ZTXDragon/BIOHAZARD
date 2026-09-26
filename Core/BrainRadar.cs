using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    // How far a ship's brains reach on radar (2026-09-26, ZTX: "add a radar with scaling to brain
    // parts where a single big brain has a large radar but multiple small brains do not stack").
    public static class BrainRadar
    {
        public static float ClusterRadius(int tiles, IReadOnlyList<float> curveTiles, IReadOnlyList<float> curveRadar)
        {
            if (tiles <= 0 || curveTiles == null || curveRadar == null) return 0f;
            int n = curveTiles.Count < curveRadar.Count ? curveTiles.Count : curveRadar.Count;
            if (n == 0) return 0f;
            if (n == 1 || tiles <= curveTiles[0]) return curveRadar[0] > 0f ? curveRadar[0] : 0f;
            if (tiles >= curveTiles[n - 1]) return curveRadar[n - 1] > 0f ? curveRadar[n - 1] : 0f;

            // The segment that holds `tiles`; the checks above guarantee one exists.
            int seg = n - 2;
            for (int i = 1; i < n; i++)
            {
                if (tiles <= curveTiles[i]) { seg = i - 1; break; }
            }
            float x0 = curveTiles[seg], x1 = curveTiles[seg + 1];
            float y0 = curveRadar[seg], y1 = curveRadar[seg + 1];
            float r = x1 > x0 ? y0 + (y1 - y0) * (tiles - x0) / (x1 - x0) : y1;
            return r > 0f ? r : 0f;
        }

        public static float ShipRadius(IReadOnlyList<int> clusterTiles, IReadOnlyList<float> curveTiles,
                                       IReadOnlyList<float> curveRadar)
        {
            float best = 0f;
            for (int i = 0; i < clusterTiles.Count; i++)
            {
                float r = ClusterRadius(clusterTiles[i], curveTiles, curveRadar);
                if (r > best) best = r;
            }
            return best;
        }
    }
}
