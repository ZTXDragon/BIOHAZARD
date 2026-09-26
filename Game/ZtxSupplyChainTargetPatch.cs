using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    // Makes the mod's typed storages valid SUPPLY-CHAIN TARGETS (2026-09-12, user report: "unable
    // to link the transfer chain into the stomach" / between storages).
    [HarmonyPatch(typeof(PartComponentRules), "get_CanBeResourceSupplierTargetForTypes")]
    internal static class ZtxSupplyChainTargetPatch
    {
        private static void Postfix(PartComponentRules __instance, ref IEnumerable<ID<ResourceRules>> __result)
        {
            if (__instance is TypedResourceGridRules grid
                && grid.AllowExternalPickupAndDelivery
                && grid.ResourceType.ToString().StartsWith("ztx."))
            {
                __result = __result.Concat(new[] { grid.ResourceType });
            }
        }
    }
}
