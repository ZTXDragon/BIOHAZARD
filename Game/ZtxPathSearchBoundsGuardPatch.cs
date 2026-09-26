using System;
using System.Collections.Generic;
using System.Threading;
using Cosmoteer.Ships.Crew.Pathing;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(PathManager), nameof(PathManager.SearchCellsFrom),
                  new[] { typeof(IntRect), typeof(bool), typeof(int) })]
    internal static class ZtxPathSearchBoundsGuardPatch
    {
        private static int _skipped;

        private static bool Prefix(PathManager __instance, IntRect searchOrigin,
                                   ref IEnumerable<(IntVector2 Cell, float Dist)> __result)
        {
            // Vanilla already short-circuits this case; leave it alone.
            if (__instance._pathfinders == null)
            {
                return true;
            }

            IntRect b = __instance._bounds;

            // The seed loop iterates [Top, Bottom) x [Left, Right) and indexes an array
            // sized to _bounds, so the origin must sit fully inside those half-open edges.
            if (searchOrigin.Left >= b.Left && searchOrigin.Top >= b.Top
                && searchOrigin.Right <= b.Right && searchOrigin.Bottom <= b.Bottom)
            {
                return true;
            }

            int n = Interlocked.Increment(ref _skipped);
            if (n <= 3 || n % 500 == 0)
            {
                Log.Info("path search skipped (outside grid): origin ["
                    + searchOrigin.Left + "," + searchOrigin.Top + "," + searchOrigin.Right + "," + searchOrigin.Bottom
                    + "] grid [" + b.Left + "," + b.Top + "," + b.Right + "," + b.Bottom
                    + "] count=" + n);
            }

            __result = Array.Empty<(IntVector2, float)>();
            return false;
        }
    }
}
