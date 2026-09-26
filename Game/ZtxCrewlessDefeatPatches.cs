using System.Runtime.CompilerServices;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Career defeat for a CREWLESS fleet: the run ends when the last BRAIN dies, not when the first
    // part does.
    [HarmonyPatch(typeof(CareerSimModeManager), "GetAllegianceForReducedShip")]
    internal static class ZtxCrewlessAllegiancePatch
    {
        internal static bool IsCrewless(Ship ship)
        {
            return ship?.Crew != null
                && ship.Crew.OnShip.Count == 0
                && ship.Crew.TotalCrewCapacityWithConstruction == 0;
        }

        private static bool Prefix(Ship ship, ref int? updateCrewAllegiance, ref int __result)
        {
            if (!Config.Enabled || ship == null) return true;
            if (!ship.IsHumanPlayerShip || !IsCrewless(ship)) return true;

            bool junk = CareerDefeatRule.JunksReducedShip(
                ship.Crew.Assigned.Count,
                ship.Commands.HasSignificantCommandParts,
                ship.Crew.OnShip.Count,
                ship.Crew.TotalCrewCapacityWithConstruction);

            updateCrewAllegiance = null;
            __result = junk ? -3 : ship.Metadata.PlayerIndex;

            Log.Info("crewless allegiance: class=" + (ship.Rules != null ? ship.Rules.ID.ToString() : "?") +
                     " brains=" + ship.Commands.HasSignificantCommandParts +
                     " parts=" + ship.Parts.Count +
                     " -> " + (junk ? "junk" : "KEEP"));
            return false;       // the original's crew disjunct would junk this ship immediately
        }
    }

    // The other half: the defeat screen itself.
    [HarmonyPatch(typeof(CareerGameModeManager), "Update")]
    internal static class ZtxCareerDefeatSuppressionPatch
    {
        internal static GameOverGui Sentinel;

        private static bool _wasSuppressing;

        private static void Prefix(CareerGameModeManager __instance, out bool __state)
        {
            __state = false;
            if (!Config.Enabled || __instance == null) return;
            if (__instance._gameOverGui != null) return;    // a real screen is up; leave it alone

            if (__instance.Sim.Ships.IsDoingLocalJump || __instance.Game.IsDoingNodeJump) return;

            int crewed = 0, brained = 0, ships = 0;
            foreach (Ship s in __instance.Sim.Ships.HumanPlayerShips)
            {
                if (s == null) continue;
                ships++;
                if (s.Crew.Assigned.Count > 0) crewed++;
                if (s.Commands.HasSignificantCommandParts) brained++;
            }
            if (ships == 0) return;     // nothing of the player's is loaded; not ours to judge

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

    // AllowSaveGame is _gameOverGui == null (CareerGameModeManager.cs:336) - the single other
    // reader of the field the suppression borrows.
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
