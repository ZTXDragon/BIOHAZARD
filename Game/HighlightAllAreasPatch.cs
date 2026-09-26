using System.Collections.Generic;
using Cosmoteer.Game.Gui;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Blueprints.Graphics;
using Cosmoteer.Ships.Parts;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(HighlightBlueprintOverlayRenderer),
                  nameof(HighlightBlueprintOverlayRenderer.RefreshData))]
    internal static class HighlightAllAreasPatch
    {
        private static void Postfix(HighlightBlueprintOverlayRenderer __instance,
                                    Ship ship,
                                    IBlueprintOverlaySettingsProvider settings)
        {
            if (ship?.BlueprintParts == null) return;
            if (settings != null && !settings.ShowBuffs) return;

            foreach (BlueprintPart bp in ship.BlueprintParts)
            {
                var rules = bp.Rules;
                if (rules?.Components == null) continue;
                foreach (var comp in rules.Components)
                {
                    if (comp is IHighlightOverlayAreaProvider prov &&
                        prov.HighlightID.HasValue)
                    {
                        __instance.AddCellsFromProvider(prov, new PartInfo(bp));
                    }
                }
            }
        }
    }
}
