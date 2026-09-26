using Cosmoteer.Modes;
using Cosmoteer.Ships;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // A crewless ship with a brain is SIGNIFICANT - a live ship, not debris (2026-09-24).
    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.IsShipSignificant))]
    internal static class ZtxCrewlessSignificancePatch
    {
        private static void Postfix(Ship ship, ref bool __result)
        {
            // Runs for every ship every frame; the common case leaves on the first test.
            if (__result || !Config.Enabled || ship?.Crew == null) return;

            __result = SignificanceRule.IsSignificant(
                vanillaResult: false,
                // Vanilla defers to the provider only when it returns a value (flag.HasValue).
                hasSpecialProvider: ship.SpecialSignificanceProvider?.IsSignificant != null,
                hasCommandParts: ship.Commands.HasSignificantCommandParts,
                crewOnShip: ship.Crew.OnShip.Count,
                crewCapacity: ship.Crew.TotalCrewCapacityWithConstruction);
        }
    }
}
