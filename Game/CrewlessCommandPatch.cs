using Cosmoteer.Game.Gui;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(CommandManager), nameof(CommandManager.CanExecuteCrewCommandsFrom))]
    internal static class CrewlessCommandPatch
    {
        private static void Postfix(CommandManager __instance, int playerIndex, ref bool __result)
        {
            if (__result) return;

            Ship ship = __instance.Ship;
            if (ship == null || !ZtxHaulManager.HasHaulers(ship)) return;

            __result = __instance.IsCommandableBy(playerIndex);
        }
    }

    [HarmonyPatch(typeof(ShipsCard), "UpdateCommandButtons")]
    internal static class CrewlessSalvageButtonPatch
    {
        private static void Prefix(ShipsCard __instance, ref int crewCommandableShipCount)
        {
            if (crewCommandableShipCount > 0) return;

            foreach (Ship s in __instance.Sim.PlayerInput.SelectedShips)
            {
                if (s.Commands.CanExecuteCrewCommandsFromLocalPlayer)
                {
                    crewCommandableShipCount++;
                }
            }
        }
    }
}
