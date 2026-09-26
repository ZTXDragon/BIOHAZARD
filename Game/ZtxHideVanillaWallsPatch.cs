using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    // Suppresses the VANILLA hull-wall skin on bio parts (2026-09-12, user request: the bio wall
    // art is not painted yet and the grey terran plating must not show meanwhile).
    internal static class ZtxHideVanillaWalls
    {
        private static readonly ID<PartCategory> BioCategory = new ID<PartCategory>("ztx_bio");

        internal static bool Applies(PartRules rules)
        {
            return Config.HideVanillaWalls
                && rules?.TypeCategories != null
                && rules.TypeCategories.Contains(BioCategory);
        }
    }

    // Clears the vanilla wall skin around bio parts, at the LAST possible moment.
    [HarmonyPatch(typeof(ExternalWallsManager), "UpdateCell", new[] { typeof(IntVector2), typeof(byte) })]
    internal static class ZtxHideExternalWallsPatch
    {
        private static readonly IntVector2[] Offsets =
        {
            new IntVector2(-1, -1), new IntVector2(0, -1), new IntVector2(1, -1),
            new IntVector2(1, 0),   new IntVector2(1, 1),  new IntVector2(0, 1),
            new IntVector2(-1, 1),  new IntVector2(-1, 0),
        };

        private static readonly System.Predicate<Part> IsBioPart =
            p => p != null && ZtxHideVanillaWalls.Applies(p.Rules);

        private static void Prefix(ExternalWallsManager __instance, IntVector2 cell, ref byte sitCode)
        {
            if (!Config.HideVanillaWalls || sitCode == byte.MaxValue) return;
            Ship ship = __instance?.Ship;
            if (ship == null) return;

            Part self = ship.Parts[cell, PartRectType.Normal];
            if (self != null && ZtxHideVanillaWalls.Applies(self.Rules))
            {
                sitCode = byte.MaxValue;
                return;
            }

            for (int i = 0; i < 8; i++)
            {
                IntVector2 their = cell + Offsets[i];
                Part n = ship.Parts[their, PartRectType.Normal];
                if (n != null && ZtxHideVanillaWalls.Applies(n.Rules))
                {
                    sitCode |= (byte)(1 << (7 - i));
                    continue;
                }
                if (ship.Parts.HasVirtualInternalCell(cell, their, IsBioPart))
                    sitCode |= (byte)(1 << (7 - i));
            }
        }
    }

    // Stops wall segments being drawn on a bio part's own cells.
    [HarmonyPatch(typeof(PartRules), "GetInternalWalls")]
    internal static class ZtxHideInternalWallsPatch
    {
        private static void Postfix(PartRules __instance, ref AdjacencyFlags __result)
        {
            if (ZtxHideVanillaWalls.Applies(__instance)) __result = AdjacencyFlags.None;
        }
    }
}
