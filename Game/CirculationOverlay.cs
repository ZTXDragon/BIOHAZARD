using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Halfling.Gui;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    internal static class CirculationOverlay
    {
        internal static bool Enabled;

        private static Ship s_ship;
        private static BlueprintFlowModel s_model;
        private static bool s_dirty = true;
        private static bool s_failed;

        internal static void MarkDirty() { s_dirty = true; }

        internal static BlueprintFlowModel ModelFor(Ship ship)
        {
            if (!Enabled || ship == null || s_failed) return null;
            if (!ReferenceEquals(ship, s_ship)) Attach(ship);
            if (ship.BlueprintParts.IsDoingTempValidityCheckUpdate) return s_model;
            if (s_model != null && s_model.BuiltAtPartCount != ship.BlueprintParts.Count) s_dirty = true;
            if (s_dirty || s_model == null)
            {
                try
                {
                    s_model = BlueprintFlowModel.Build(ship);
                    s_dirty = false;
                    if (Config.VerboseLog)
                    {
                        Log.Info("circulation model: " + s_model.Parts.Count + " part(s), " + s_model.Nodes.Count +
                                 " node(s), " + s_model.Arrows.Count + " arrow(s), " + ship.BlueprintParts.Count + " blueprint part(s)");
                        LogParity(ship, s_model);
                    }
                }
                catch (Exception ex)
                {
                    s_failed = true;
                    s_model = null;
                    Log.Exception("CirculationOverlay.Build", ex);
                }
            }
            return s_model;
        }

        private static void Attach(Ship ship)
        {
            if (s_ship != null)
            {
                s_ship.BlueprintPartNetworks.NetworkChanged -= OnNetworkChanged;
                s_ship.BlueprintParts.PartsChanged -= OnPartsChanged;
            }
            s_ship = ship;
            s_model = null;
            s_dirty = true;
            ship.BlueprintPartNetworks.NetworkChanged += OnNetworkChanged;
            ship.BlueprintParts.PartsChanged += OnPartsChanged;
        }

        private static void OnNetworkChanged(object sender, EventArgs e)
        {
            if (s_ship != null && !s_ship.BlueprintParts.IsDoingTempValidityCheckUpdate) s_dirty = true;
        }

        private static void OnPartsChanged(object sender, EventArgs e)
        {
            if (s_ship != null && !s_ship.BlueprintParts.IsDoingTempValidityCheckUpdate) s_dirty = true;
        }

        private static void LogParity(Ship ship, BlueprintFlowModel m)
        {
            int compared = 0, mismatches = 0;
            for (int i = 0; i < m.Nodes.Count; i++)
            {
                Part live = m.Nodes[i].Part.MatchingPhysicalPart;
                if (live == null) continue;
                foreach (ZtxFlowNode fn in LiveNodes(live, m.Nodes[i].Rules.Network))
                {
                    if (!ReferenceEquals(fn.Rules, m.Nodes[i].Rules)) continue;
                    compared++;
                    if (fn.Pressure != m.Graph.NodeById(i).Pressure) mismatches++;
                    break;
                }
            }
            Log.Info("circulation parity: " + compared + " node(s) compared, mismatches=" + mismatches);
        }

        private static IEnumerable<ZtxFlowNode> LiveNodes(Part part, string network)
        {
            foreach (PartComponent c in part.Components)
                if (c is ZtxFlowNode fn && fn.Rules.Network == network) yield return fn;
        }

        [HarmonyPatch(typeof(BuildToolbox), "AddMenus")]
        internal static class MenuPatch
        {
            private static void Postfix(BuildToolbox __instance)
            {
                try
                {
                    MenuToggleItem item = __instance._menu.AddToggleItem(ToggleButton.Style.Check,
                        Strings.KeyString("ZtxOverlay/ShowCirculation"), Enabled);
                    item.ToolTips.TextProvider = Strings.KeyString("ZtxOverlay/ShowCirculationTip");
                    item.Activated += delegate { item.IsSelected = Enabled; };
                    item.SelectionChanged += delegate
                    {
                        Enabled = item.IsSelected;
                        s_dirty = true;
                        s_failed = false;
                        CirculationOverlayRenderer.ResetFailure();
                        Log.Info("circulation overlay " + (Enabled ? "ON" : "OFF"));
                    };
                }
                catch (Exception ex)
                {
                    Log.Exception("CirculationOverlay.MenuPatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(BlueprintPart), nameof(BlueprintPart.OnPartUIToggleStatesChanged))]
        internal static class ToggleChangedPatch
        {
            private static void Postfix() { s_dirty = true; }
        }
    }
}
