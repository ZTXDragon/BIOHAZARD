using Cosmoteer.Resources;
using Cosmoteer.Ships.Crew.Jobs;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    // Defensive guard for nugget-sourced transfer jobs (2026-09-12 crash fix).
    [HarmonyPatch(typeof(ResourceTransferJob), "IsSourceValid")]
    internal static class ZtxNuggetJobGuardPatch
    {
        private static bool Prefix(ResourceTransferJob __instance, ref bool __result)
        {
            if (__instance.Source is Nugget n && n.Sim == null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
