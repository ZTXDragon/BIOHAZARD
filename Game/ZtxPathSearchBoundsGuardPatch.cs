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
            if (!OutsideGrid(__instance, searchOrigin)) return true;
            __result = Array.Empty<(IntVector2, float)>();
            return false;
        }

        internal static bool OutsideGrid(PathManager paths, IntRect searchOrigin)
        {
            if (paths._pathfinders == null)
            {
                return false;
            }

            IntRect b = paths._bounds;

            if (searchOrigin.Left >= b.Left && searchOrigin.Top >= b.Top
                && searchOrigin.Right <= b.Right && searchOrigin.Bottom <= b.Bottom)
            {
                return false;
            }

            int n = Interlocked.Increment(ref _skipped);
            if (n <= 3 || n % 500 == 0)
            {
                Log.Info("path search skipped (outside grid): origin ["
                    + searchOrigin.Left + "," + searchOrigin.Top + "," + searchOrigin.Right + "," + searchOrigin.Bottom
                    + "] grid [" + b.Left + "," + b.Top + "," + b.Right + "," + b.Bottom
                    + "] count=" + n);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PathManager), nameof(PathManager.SearchPartsFrom),
                  new[] { typeof(IntRect), typeof(bool), typeof(int) })]
    internal static class ZtxPathSearchPartsBoundsGuardPatch
    {
        private static bool Prefix(PathManager __instance, IntRect searchOrigin,
                                   ref IEnumerable<(Cosmoteer.Ships.Parts.Part Part, float Dist, IntVector2 ClosestCell)> __result)
        {
            if (!ZtxPathSearchBoundsGuardPatch.OutsideGrid(__instance, searchOrigin)) return true;
            __result = Array.Empty<(Cosmoteer.Ships.Parts.Part, float, IntVector2)>();
            return false;
        }
    }

    [HarmonyPatch(typeof(PathManager), nameof(PathManager.SearchCrewFrom),
                  new[] { typeof(IntRect), typeof(bool), typeof(int) })]
    internal static class ZtxPathSearchCrewBoundsGuardPatch
    {
        private static bool Prefix(PathManager __instance, IntRect searchOrigin,
                                   ref IEnumerable<(Cosmoteer.Ships.Crew.ShipCrew crew, float Dist)> __result)
        {
            if (!ZtxPathSearchBoundsGuardPatch.OutsideGrid(__instance, searchOrigin)) return true;
            __result = Array.Empty<(Cosmoteer.Ships.Crew.ShipCrew, float)>();
            return false;
        }
    }
}
