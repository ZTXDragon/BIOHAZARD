using System;
using System.Collections.Generic;
using Cosmoteer.Modes;
using Cosmoteer.Ships;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    // Keeps the cone out of RADAR.
    [HarmonyPatch(typeof(SimModeManager), "GetCustomRadarCircles")]
    internal static class ZtxEyeConeRadarPatch
    {
        [ThreadStatic]
        internal static bool InRadarPass;

        private static void Prefix()
        {
            InRadarPass = true;
        }

        private static void Finalizer()
        {
            InRadarPass = false;
        }
    }

    // Feeds every live ZtxEyeCone into the team's sight list.
    [HarmonyPatch(typeof(SimModeManager), "GetCustomSightCircles")]
    internal static class ZtxEyeConePatch
    {
        private static void Postfix(SimModeManager __instance, int team,
                                    ICollection<(Circle Circle, object Source)> sightCircles)
        {
            if (!Config.Enabled || sightCircles == null) return;
            if (ZtxEyeConeRadarPatch.InRadarPass) return;      // sight only, never radar

            IReadOnlyList<ZtxEyeCone> all = ZtxEyeCone.All;
            for (int i = 0; i < all.Count; i++)
            {
                ZtxEyeCone eye = all[i];
                Ship ship = eye?.Part?.Ship;
                if (ship == null || ship.Sim != __instance.Sim) continue;
                if (ship.Metadata.Team != team) continue;
                if (!eye.IsActive) continue;
                try
                {
                    eye.AppendSightCircles(sightCircles);
                }
                catch (System.Exception ex)
                {
                    // A throw here would take the whole sight refresh with it, blinding the
                    // team and stopping the fog from updating at all.
                    Log.Exception("ZtxEyeCone.AppendSightCircles", ex);
                }
            }
        }
    }
}
