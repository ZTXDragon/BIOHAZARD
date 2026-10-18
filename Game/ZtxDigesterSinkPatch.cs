using System.Reflection;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts.Resources;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    internal static class ZtxDigesterSinkPatch
    {
        private static readonly ConditionalWeakTable<FlexResourceGrid, ZtxDigester> s_owned =
            new ConditionalWeakTable<FlexResourceGrid, ZtxDigester>();

        public static void Register(ZtxDigester digester)
        {
            FlexResourceGrid g = digester?.Grid;
            if (g == null) return;
            s_owned.Remove(g);
            s_owned.Add(g, digester);
        }

        public static void Deregister(ZtxDigester digester)
        {
            FlexResourceGrid g = digester?.Grid;
            if (g != null) s_owned.Remove(g);
        }

        private static bool Refuses(FlexResourceGrid.ResourceTile tile, ID<ResourceRules> resource)
        {
            if (tile == null) return false;
            FlexResourceGrid grid = tile.Grid;
            if (grid == null) return false;
            if (!s_owned.TryGetValue(grid, out ZtxDigester digester)) return false;
            return !digester.HasRecipeFor(resource);
        }

        [HarmonyPatch]
        internal static class GetRemainingCapacityPatch
        {
            [HarmonyPrepare]
            internal static bool Prepare() => TargetMethod() != null;

            [HarmonyTargetMethod]
            internal static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(FlexResourceGrid.ResourceTile),
                    "Cosmoteer.Ships.Resources.IResourceSink.GetRemainingCapacity");
            }

            [HarmonyPostfix]
            internal static void Postfix(FlexResourceGrid.ResourceTile __instance,
                                         ID<ResourceRules> resourceType, ref int __result)
            {
                if (__result != 0 && Refuses(__instance, resourceType)) __result = 0;
            }
        }

        [HarmonyPatch]
        internal static class ReceiveResourcesPatch
        {
            [HarmonyPrepare]
            internal static bool Prepare() => TargetMethod() != null;

            [HarmonyTargetMethod]
            internal static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(FlexResourceGrid.ResourceTile),
                    "Cosmoteer.Ships.Resources.IResourceSink.ReceiveResources");
            }

            [HarmonyPrefix]
            internal static bool Prefix(FlexResourceGrid.ResourceTile __instance,
                                        ID<ResourceRules> resourceType, ref int __result)
            {
                if (!Refuses(__instance, resourceType)) return true;
                __result = 0;
                return false;
            }
        }
    }
}
