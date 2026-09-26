using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Sensors;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    // Brains give radar (2026-09-26).
    [HarmonyPatch(typeof(SensorManager), nameof(SensorManager.RadarRadius), MethodType.Getter)]
    internal static class ZtxBrainRadarPatch
    {
        private static void Postfix(SensorManager __instance, ref float __result)
        {
            if (!Config.Enabled) return;
            Ship ship = __instance.Ship;
            float radar = ZtxBrainManager.RadarOf(ship);
            if (radar <= 0f) return;
            float r = ship.WorldBoundingRadius + radar * ship.Nebulas.RadarRangeFactor;
            if (r > __result) __result = r;
        }
    }
}
