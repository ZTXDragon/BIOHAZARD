using System.Linq;
using System.Reflection;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui.Resources;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Resources;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui.Dialogs;
using Halfling.Pooling;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Vanilla's Eject controls, made to work on a crewless bio ship (2026-09-24).
    internal static class ZtxEject
    {
        private static readonly ID<PartCategory> BioCategory = new ID<PartCategory>("ztx_bio");

        internal static bool CanVent(IResourceSource source)
        {
            if (!Config.Enabled || source == null || source is Nugget) return false;
            if (!(source is IResourceSink)) return false;
            Part part = source.Part;
            PartRules rules = part?.Rules;
            return part?.Ship != null
                && rules?.TypeCategories != null
                && rules.TypeCategories.Contains(BioCategory);
        }

        internal static void EnqueueVent(GameRoot game, IResourceSource source, int amount)
        {
            if (game == null || amount <= 0) return;
            game.NetManager.EnqueueInput(new MPMoveResourcesInput(
                source, (IResourceSink)source, amount, source.Ship.Jobs.AllocLocalJobID()));
        }

        internal static void Vent(GameRoot game, IResourceSource source, int requested,
                                  int playerIndex, bool force)
        {
            Ship ship = source?.Ship;
            if (ship == null || game?.Sim == null || !ship.ExistsInSim(game.Sim)) return;
            if (!force && !ship.Commands.IsCommandableBy(playerIndex)) return;
            if (!CanVent(source)) return;

            int amount = EjectRule.VentAmount(requested, source.Resources,
                ship.Resources.GetAnticipatedPickUp(source, MPValueType.Confirmed));
            if (amount <= 0) return;

            ID<ResourceRules> type = source.ResourceType;
            // Read back what actually left: SubtractResources clamps, and its setter notifies
            // subscribers synchronously, so the level can move underneath us.
            int taken = source.SubtractResources(amount);
            if (taken <= 0) return;

            Vector2 center = source.Center;
            Vector2 inherit = ship.Physics.Body != null
                ? (Vector2)ship.Physics.Body.GetLinearVelocityFromWorldPoint(
                    ship.DetTransformPointToWorld(center))
                : Vector2.Zero;
            game.Sim.Nuggets.DropNuggets(type, taken,
                ship.DetTransformShapeToWorld(Rect.CenteredOn(center, new Vector2(0.8f, 0.8f))),
                inherit, randomVelocity: true);
            Log.Info("eject: vented " + taken + " " + type + " from " + PartName(source));
        }

        private static string PartName(IResourceSource s)
        {
            Part p = s.Part;
            return p == null ? "?" : p.Rules.ID + "@" + p.Location;
        }
    }

    // Sim side: a vent request never becomes a transfer job.
    [HarmonyPatch(typeof(MPMoveResourcesInput), nameof(MPMoveResourcesInput.Execute))]
    internal static class ZtxEjectExecutePatch
    {
        private static bool Prefix(MPMoveResourcesInput __instance, GameRoot game, int playerIndex,
                                   bool force)
        {
            if (!EjectRule.IsVentRequest(__instance.Source, __instance.Sink)) return true;
            ZtxEject.Vent(game, __instance.Source, __instance.Amount, playerIndex, force);
            return false;
        }
    }

    // Local prediction: vanilla creates an unconfirmed job for instant feedback.
    [HarmonyPatch(typeof(MPMoveResourcesInput), nameof(MPMoveResourcesInput.ExecuteLocalImmediate))]
    internal static class ZtxEjectLocalPatch
    {
        private static bool Prefix(MPMoveResourcesInput __instance)
        {
            return !EjectRule.IsVentRequest(__instance.Source, __instance.Sink);
        }
    }

    // The resource bar's right-click menu (Eject Some / All / Excess).
    [HarmonyPatch(typeof(ResourcesToolbox.ResourceButton), "EjectResources")]
    internal static class ZtxEjectMenuPatch
    {
        private static bool Prefix(ResourcesToolbox.ResourceButton __instance, Ship ship, int amount)
        {
            if (!Config.Enabled || ship == null) return true;
            if (amount <= 0) return false;

            GameRoot game = __instance._toolbox.Game;
            int requested = amount, sent = 0;
            bool vented = false;
            using TempList<(IResourceSource, int)> sources = TempList<(IResourceSource, int)>.Alloc();
            ship.Resources.GetAllAvailableResourceSources(__instance._rr.ID, MPValueType.Displayed,
                sources, includeAnticipatedPickups: true, null, sortBestFirst: true);
            for (int i = 0; i < sources.Count && amount > 0; i++)
            {
                (IResourceSource item, int available) = sources[i];
                int num = Mathx.Min(available, amount);
                if (num <= 0) continue;

                IntRect? tileRect = item.TileRect;
                Airlock airlock = tileRect.HasValue
                    ? Airlock.GetNearestContiguousWith(item.Ship, tileRect.Value.Location)
                    : Airlock.GetNearestContiguousWith(item.Part);
                if (airlock != null)
                {
                    game.NetManager.EnqueueInput(new MPMoveResourcesInput(
                        item, airlock, num, ship.Jobs.AllocLocalJobID()));
                }
                else if (ZtxEject.CanVent(item))
                {
                    ZtxEject.EnqueueVent(game, item, num);
                    vented = true;
                }
                else
                {
                    continue;
                }
                amount -= num;
                sent += num;
            }

            bool fail = vented ? EjectRule.ShowFailure(requested, sent) : amount > 0;
            if (fail) OneButtonDialog.Show(Strings.GetText("ResourcesBox/EjectFailed"));
            return false;
        }
    }

    // The Eject button on a selected storage tile (and its mirrored tiles).
    [HarmonyPatch(typeof(MoveResourcesState), "OnEjectClicked")]
    internal static class ZtxEjectTilePatch
    {
        private static bool Prefix(MoveResourcesState __instance)
        {
            if (!Config.Enabled) return true;
            IResourceSource selected = __instance._selectedTile;
            if (selected == null) return false;

            EjectTile(__instance.Game, selected);
            foreach (var mirrored in BaseResourcesState.GetMirroredTiles(selected.Ship, selected,
                         typed: true, flex: true, ui: true, airlock: false))
            {
                IResourceSource item = mirrored.Tile;
                if (MoveResourcesState.IsValidPickUpTile(selected.Ship, item, selected))
                    EjectTile(__instance.Game, item);
            }
            return false;
        }

        private static void EjectTile(GameRoot game, IResourceSource tile)
        {
            if (tile.Resources <= 0) return;
            Airlock airlock = Airlock.GetNearestContiguousWith(tile.Ship, tile.TileRect.Value.Location);
            if (airlock != null)
            {
                game.NetManager.EnqueueInput(new MPMoveResourcesInput(
                    tile, airlock, tile.Resources, tile.Ship.Jobs.AllocLocalJobID()));
            }
            else if (ZtxEject.CanVent(tile))
            {
                ZtxEject.EnqueueVent(game, tile, tile.Resources);
            }
        }
    }

    // Un-greys the tile Eject button.
    [HarmonyPatch]
    internal static class ZtxEjectButtonPatch
    {
        internal static bool Prepare() => TargetMethod() != null;

        [HarmonyTargetMethod]
        internal static MethodBase TargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(MoveResourcesState))
                .FirstOrDefault(m => m.IsStatic && m.Name.Contains("_HasAirlockFor"));
        }

        private static void Postfix(IResourceSource __0, ref bool __result)
        {
            if (!__result && ZtxEject.CanVent(__0)) __result = true;
        }
    }
}
