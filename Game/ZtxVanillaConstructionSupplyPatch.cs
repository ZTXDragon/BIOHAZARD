using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Resources;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.TransferConstructionResourcesTo))]
    internal static class ZtxVanillaConstructionSupplyPatch
    {
        private static readonly ConditionalWeakTable<Ship, object> s_reported =
            new ConditionalWeakTable<Ship, object>();

        private static bool Prefix(ResourceManager __instance)
        {
            Ship ship = __instance.Ship;
            if (!ZtxHaulManager.ServesConstruction(ship)) return true;
            if (!s_reported.TryGetValue(ship, out _))
            {
                s_reported.Add(ship, null);
                Log.Info("vanilla construction supply skipped on ship " + ship.UniqueID +
                         ": tentacles feed its build sites");
            }
            return false;
        }
    }
}
