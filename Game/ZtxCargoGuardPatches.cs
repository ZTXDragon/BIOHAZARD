using Cosmoteer;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.HitEffects;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    // Two guards that keep the ENGINE's nugget machinery from re-routing or fighting over the haul
    // organs' in-flight cargo (2026-09-12, user report: "some tentacles move chain resources
    [HarmonyPatch(typeof(TurretWeapon), "TryAcquireCollectNuggetsTarget")]
    internal static class ZtxBeamCarryExclusionPatch
    {
        private static int s_vetoLogBudget = 40;

        private static void Postfix(ref bool __result, ref ITarget target)
        {
            if (__result && target is CollectNuggetTarget cnt &&
                ZtxHaulManager.IsBeingCarried(cnt.Nugget))
            {
                if (ZtxHaulDebug.Verbose && s_vetoLogBudget > 0)
                {
                    s_vetoLogBudget--;
                    Log.Info("guard: beam veto - nugget is carry-held (" +
                             cnt.Nugget.Quantity + " " + cnt.Nugget.ResourceType + ")" +
                             (s_vetoLogBudget == 0 ? " [last veto log]" : ""));
                }
                target = null;
                __result = false;
            }
        }
    }

    // Stops the engine's full-sink fallback from re-routing TRACKED cargo to a priority-picked
    // storage.
    [HarmonyPatch(typeof(CollectNuggetEffectRules), "RequeueRemainingResources")]
    internal static class ZtxCargoRequeueGuardPatch
    {
        private static bool Prefix(Nugget n)
        {
            if (!ZtxHaulManager.IsTrackedCargo(n)) return true;
            if (ZtxHaulDebug.Verbose)
                Log.Info("guard: BLOCKED engine requeue of tracked cargo (" +
                         n.Quantity + " " + n.ResourceType + ") - MaintainCargo will re-route");
            return false;
        }
    }
}
