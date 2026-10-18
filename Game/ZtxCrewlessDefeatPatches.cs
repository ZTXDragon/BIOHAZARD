using System.Runtime.CompilerServices;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(CareerSimModeManager), "GetAllegianceForReducedShip")]
    internal static class ZtxCrewlessAllegiancePatch
    {
        private static readonly ConditionalWeakTable<Ship, StrongBox<bool>> s_lastAnswer =
            new ConditionalWeakTable<Ship, StrongBox<bool>>();

        internal static bool IsCrewless(Ship ship)
        {
            return ship?.Crew != null
                && ship.Crew.OnShip.Count == 0
                && ship.Crew.TotalCrewCapacityWithConstruction == 0
                && ZtxBrainManager.HasBrainAboard(ship);
        }

        private static bool Prefix(Ship ship, ref int? updateCrewAllegiance, ref int __result)
        {
            if (!Config.Enabled || ship == null) return true;
            if (!ship.IsHumanPlayerShip || !IsCrewless(ship)) return true;

            bool junk = CareerDefeatRule.JunksReducedShip(
                ship.Crew.Assigned.Count,
                ship.Commands.HasSignificantCommandParts,
                ship.Crew.OnShip.Count,
                ship.Crew.TotalCrewCapacityWithConstruction,
                hasBioBrain: true);

            updateCrewAllegiance = null;
            __result = junk ? -3 : ship.Metadata.PlayerIndex;

            if (!s_lastAnswer.TryGetValue(ship, out StrongBox<bool> last) || last.Value != junk)
            {
                s_lastAnswer.AddOrUpdate(ship, new StrongBox<bool>(junk));
                Log.Info("crewless allegiance: class=" + (ship.Rules != null ? ship.Rules.ID.ToString() : "?") +
                         " brains=" + ship.Commands.HasSignificantCommandParts +
                         " parts=" + ship.Parts.Count +
                         " -> " + (junk ? "junk" : "KEEP"));
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(CareerGameModeManager), "Update")]
    internal static class ZtxCareerDefeatSuppressionPatch
    {
        internal static GameOverGui Sentinel;

        private static bool _wasSuppressing;

        private static void Prefix(CareerGameModeManager __instance, out bool __state)
        {
            __state = false;
            if (!Config.Enabled || __instance == null) return;
            if (__instance._gameOverGui != null) return;

            if (__instance.Sim.Ships.IsDoingLocalJump || __instance.Game.IsDoingNodeJump) return;

            int crewed = 0, brained = 0, ships = 0;
            foreach (Ship s in __instance.Sim.Ships.HumanPlayerShips)
            {
                if (s == null) continue;
                ships++;
                if (s.Crew.Assigned.Count > 0) crewed++;
                if (s.Commands.HasSignificantCommandParts && ZtxBrainManager.HasBrainAboard(s)) brained++;
            }
            if (ships == 0) return;

            if (!CareerDefeatRule.ShouldSuppressDefeat(false, crewed, brained))
            {
                if (_wasSuppressing)
                {
                    _wasSuppressing = false;
                    Log.Info("career defeat: no brain left in the fleet, the run is over");
                }
                return;
            }

            if (!_wasSuppressing)
            {
                _wasSuppressing = true;
                Log.Info("career defeat suppressed: crewless fleet, brains alive on " +
                         brained + " ship(s)");
            }

            Sentinel ??= (GameOverGui)RuntimeHelpers.GetUninitializedObject(typeof(GameOverGui));
            __instance._gameOverGui = Sentinel;
            __state = true;
        }

        private static void Postfix(CareerGameModeManager __instance, bool __state)
        {
            if (!__state || __instance == null) return;
            if (ReferenceEquals(__instance._gameOverGui, Sentinel)) __instance._gameOverGui = null;
        }
    }

    [HarmonyPatch(typeof(CareerGameModeManager), "AllowSaveGame", MethodType.Getter)]
    internal static class ZtxCareerAllowSaveGamePatch
    {
        private static void Postfix(CareerGameModeManager __instance, ref bool __result)
        {
            if (!Config.Enabled || __instance == null || __result) return;
            if (ZtxCareerDefeatSuppressionPatch.Sentinel != null
                && ReferenceEquals(__instance._gameOverGui, ZtxCareerDefeatSuppressionPatch.Sentinel))
                __result = true;
        }
    }
}
