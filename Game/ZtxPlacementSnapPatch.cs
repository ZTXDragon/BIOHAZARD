using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Ships.Parts;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(PartBrushState), "UpdateAddedParts")]
    internal static class ZtxPlacementSnapPatch
    {
        private static void Prefix(PartBrushState __instance)
        {
            PartRules part = __instance.Part;
            if (part == null || !part.IsFlippable) return;
            foreach (PartComponentRules c in part.Components)
            {
                if (!(c is ZtxPlacementSnapRules snap)) continue;
                __instance._partFlipX = PlacementSnap.MirrorAt(snap.ArteryDrawnRight, __instance._partRot);
                return;
            }
        }
    }
}
