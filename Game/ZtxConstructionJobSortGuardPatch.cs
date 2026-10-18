using System;
using System.Collections.Generic;
using System.Reflection;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships.Construction;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Resources;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    internal static class ZtxConstructionJobSortGuardPatch
    {
        private static MethodBase RefreshReadyOf(Type type)
        {
            InterfaceMapping map = type.GetInterfaceMap(typeof(ConstructionManager.IQueuedConstructionMembers));
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].Name == "RefreshReady") return map.TargetMethods[i];
            }
            Log.Error("construction sort guard: no RefreshReady on " + type.Name +
                      " - the guard is NOT installed and a cancelled build can crash the game.");
            return null;
        }

        private static void CancelUnorderedJobs<TSink>(IReadOnlyList<TSink> sinks, string what)
            where TSink : IResourceSink
        {
            if (sinks == null) return;
            int cancelled = 0;
            var jobs = new List<ResourceTransferJob>();
            for (int s = 0; s < sinks.Count; s++)
            {
                jobs.Clear();
                jobs.AddRange(ResourceTransferJob.GetTransferJobsFor(sinks[s], MPValueType.Confirmed));
                for (int j = 0; j < jobs.Count; j++)
                {
                    if (jobs[j].IsManual) continue;
                    if (ZtxModJobs.IsMine(jobs[j])) ZtxModJobs.Retire(jobs[j]);
                    else jobs[j].Cancel();
                    cancelled++;
                }
            }
            if (cancelled > 0)
                Log.Info("construction sort guard: cancelled " + cancelled +
                         " unordered job(s) on a " + what + " that stopped being ready");
        }

        [HarmonyPatch]
        internal static class PartConstruction
        {
            [HarmonyPrepare]
            internal static bool Prepare() => TargetMethod() != null;

            [HarmonyTargetMethod]
            internal static MethodBase TargetMethod() => RefreshReadyOf(typeof(QueuedPartConstruction));

            [HarmonyPrefix]
            internal static void Prefix(QueuedPartConstruction __instance, bool? forceReady)
            {
                if (!__instance.IsReady) return;
                if (forceReady ?? __instance.GetIsReady()) return;
                CancelUnorderedJobs(__instance.ResourceSinks, "build site");
            }
        }

        [HarmonyPatch]
        internal static class PartRepair
        {
            [HarmonyPrepare]
            internal static bool Prepare() => TargetMethod() != null;

            [HarmonyTargetMethod]
            internal static MethodBase TargetMethod() => RefreshReadyOf(typeof(QueuedPartRepair));

            [HarmonyPrefix]
            internal static void Prefix(QueuedPartRepair __instance, bool? forceReady)
            {
                if (!__instance.IsReady) return;
                if (forceReady ?? __instance.GetIsReady()) return;
                CancelUnorderedJobs(__instance.ResourceSinks, "repair");
            }
        }
    }
}
