using Cosmoteer.Modes;
using Cosmoteer.Ships;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.IsShipSignificant))]
    internal static class ZtxCrewlessSignificancePatch
    {
        private static void Postfix(Ship ship, ref bool __result)
        {
            if (__result || !Config.Enabled || ship?.Crew == null) return;

            int onShip = ship.Crew.OnShip.Count;
            int capacity = ship.Crew.TotalCrewCapacityWithConstruction;
            __result = SignificanceRule.IsSignificant(
                vanillaResult: false,
                hasSpecialProvider: ship.SpecialSignificanceProvider?.IsSignificant != null,
                hasCommandParts: ship.Commands.HasSignificantCommandParts,
                crewOnShip: onShip,
                crewCapacity: capacity,
                hasBioBrain: onShip == 0 && capacity == 0 && ZtxBrainManager.HasBrainAboard(ship));
        }
    }
}
