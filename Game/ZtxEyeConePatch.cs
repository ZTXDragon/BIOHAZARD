using System;
using System.Collections.Generic;
using System.Reflection;
using Cosmoteer.Modes;
using Cosmoteer.Ships;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
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

    [HarmonyPatch]
    internal static class ZtxEyeConePatch
    {
        [ThreadStatic]
        private static int s_depth;

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var found = new List<MethodBase>();
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(SimModeManager).Assembly))
            {
                if (type != typeof(SimModeManager) && !type.IsSubclassOf(typeof(SimModeManager))) continue;
                MethodInfo m = type.GetMethod("GetCustomSightCircles",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (m != null && !m.IsAbstract) found.Add(m);
            }
            Log.Info("eye sight hook installed on " + found.Count + " method(s)");
            return found;
        }

        private static void Prefix() { s_depth++; }

        private static void Finalizer() { s_depth--; }

        private static void Postfix(SimModeManager __instance, int team,
                                    ICollection<(Circle Circle, object Source)> sightCircles)
        {
            if (s_depth != 1) return;
            if (!Config.Enabled || sightCircles == null) return;
            if (ZtxEyeConeRadarPatch.InRadarPass) return;

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
                    Log.Exception("ZtxEyeCone.AppendSightCircles", ex);
                }
            }
        }
    }
}
