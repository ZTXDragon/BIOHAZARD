using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
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

            for (int i = 0; i < 8; i++)
            {
                IntVector2 their = cell + Offsets[i];
                bool bioLink = ship.Parts.HasVirtualInternalCell(cell, their, IsBioPart);
                if (BioWallSide.Hide(NeighbourAt(ship, their), bioLink))
                    sitCode |= (byte)(1 << (7 - i));
            }
        }

        private static WallNeighbour NeighbourAt(Ship ship, IntVector2 cell)
        {
            Part part = ship.Parts[cell, PartRectType.Normal];
            if (part == null) return WallNeighbour.Empty;
            if (ZtxHideVanillaWalls.Applies(part.Rules)) return WallNeighbour.Bio;
            return part.IsUnderConstruction ? WallNeighbour.Empty : WallNeighbour.Other;
        }
    }
}
