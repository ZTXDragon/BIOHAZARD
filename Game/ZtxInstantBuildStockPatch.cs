using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Resources;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    internal static class ZtxInstantBuildStockPatch
    {
        private static readonly ConditionalWeakTable<BaseResourceStorage, object> s_digesterOutputs =
            new ConditionalWeakTable<BaseResourceStorage, object>();

        internal static void RegisterDigesterOutput(ISimpleResourceStorage output)
        {
            if (output is BaseResourceStorage s)
            {
                s_digesterOutputs.Remove(s);
                s_digesterOutputs.Add(s, null);
            }
        }

        internal static void DeregisterDigesterOutput(ISimpleResourceStorage output)
        {
            if (output is BaseResourceStorage s) s_digesterOutputs.Remove(s);
        }

        [HarmonyPatch]
        internal static class DigesterOutputIsStock
        {
            [HarmonyPrepare]
            internal static bool Prepare() => TargetMethod() != null;

            [HarmonyTargetMethod]
            internal static MethodBase TargetMethod()
            {
                InterfaceMapping map = typeof(BaseResourceStorage).GetInterfaceMap(typeof(IResourceSource));
                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    if (map.InterfaceMethods[i].Name == "get_IsValidManualResourceSource")
                        return map.TargetMethods[i];
                }
                Log.Error("instant-build stock: no IsValidManualResourceSource getter on BaseResourceStorage" +
                          " - stomach output will not pay for instant builds.");
                return null;
            }

            [HarmonyPostfix]
            internal static void Postfix(BaseResourceStorage __instance, ref bool __result)
            {
                if (!__result && __instance.Sim != null && __instance.HasResources &&
                    s_digesterOutputs.TryGetValue(__instance, out _))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.GetUsableConstructionResourceTotal))]
        internal static class AffordOnlyWhatCanBePaid
        {
            [HarmonyPostfix]
            internal static void Postfix(ResourceManager __instance, ID<ResourceRules> resourceType,
                                         bool includeAnticipated, MPValueType valueType, ref int __result)
            {
                if (__instance?.Ship == null || __instance.Sim == null) return;
                if (includeAnticipated) return;

                var ships = new List<Ship>();
                __instance.GetUsableConstructionResourceShips(ships);
                var sources = new List<(IResourceSource Source, int AnticipatedResources)>();
                for (int i = 0; i < ships.Count; i++)
                {
                    Ship ship = ships[i];
                    int stored = ship.Resources.GetResourceTotal(resourceType, includeStored: true,
                        includeCarried: false, includeOffShipCarried: false,
                        includeAnticipatedDeliveries: false, includeAnticipatedPickups: false, valueType);
                    if (stored <= 0) continue;

                    sources.Clear();
                    ship.Resources.GetAllAvailableResourceSources(resourceType, valueType, sources,
                                                                  includeAnticipatedPickups: false);
                    int spendable = 0;
                    for (int j = 0; j < sources.Count; j++) spendable += sources[j].AnticipatedResources;

                    __result -= ConstructionStock.Unreachable(stored, spendable);
                }
            }
        }
    }
}
