using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships.Resources;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.HasConstructionTransferJobs))]
    internal static class ZtxConstructionCancelGuardPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ResourceManager __instance, MPValueType valueType, bool pendingPickupOnly,
                                     ref bool __result)
        {
            if (__result || !Config.Enabled || __instance?.Ship == null) return;
            ZtxHaulManager haul = ZtxHaulManager.Find(__instance.Ship);
            if (haul != null && haul.HasLiveConstructionSupply(valueType, pendingPickupOnly)) __result = true;
        }
    }
}
