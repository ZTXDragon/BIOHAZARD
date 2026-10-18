using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Game.SelectionStates;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Buffs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Rendering;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(BuildToolbox), "OnDrawOverlays")]
    internal static class CirculationOverlayRenderer
    {
        private const string Artery = BlueprintFlowModel.ArteryNetwork;
        private const string Vein = BlueprintFlowModel.VeinNetwork;
        private static readonly Color ReachBorder = Rgba(255, 255, 255, 140);
        private static readonly Color ReachFill = Rgba(255, 255, 255, 25);
        private static readonly Color ArrowCore = Rgba(255, 255, 255, 255);
        private static readonly Color ArrowOutline = Rgba(15, 8, 8, 255);
        private const float OutlineScale = 1.35f;
        private static bool s_failed;

        internal static void ResetFailure() { s_failed = false; }

        private static void Postfix(BuildToolbox __instance)
        {
            if (!CirculationOverlay.Enabled || s_failed) return;
            Ship ship = __instance._ship;
            if (ship?.Sim == null) return;
            try
            {
                BlueprintFlowModel m = CirculationOverlay.ModelFor(ship);
                if (m == null) return;
                List<int> selectedValves = SelectedValves(m, __instance);
                using (App.Graphics.PushTransform(ship.Sim.Viewport.WorldToViewportTransform * ship.WorldTransform))
                {
                    CirculationNetworkDisplay.Draw(ship, m);
                    DrawArrows(ship, m);
                    foreach (int valve in selectedValves) DrawOneReach(m, valve);
                }
                foreach (int valve in selectedValves) DrawReachLabel(ship, m, valve);
                DrawHeldValve(m, __instance);
                DrawHover(__instance, ship, m);
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("CirculationOverlayRenderer", ex);
            }
        }

        private static Color Rgba(int r, int g, int b, int a) => new Color(r / 255f, g / 255f, b / 255f, a / 255f);

        private static GraphicsList<AtlasQuad> s_icons;
        private static bool s_iconsMissingLogged;
        private static readonly ID<PartNetworkOverlayIcon> ArrowIconId = new ID<PartNetworkOverlayIcon>("ZtxFlowArrow");
        private static readonly ID<PartNetworkOverlayIcon> DotIconId = new ID<PartNetworkOverlayIcon>("ZtxFlowDot");

        private static void DrawArrows(Ship ship, BlueprintFlowModel m)
        {
            PartNetworkGuiRules rules = GameApp.Rules.Game.GameGui.PartNetworks;
            if (rules?.OverlayIcons == null || rules.OverlayIconAtlas == null ||
                !rules.OverlayIcons.TryGetValue(ArrowIconId, out PartNetworkOverlayIcon arrow) ||
                !rules.OverlayIcons.TryGetValue(DotIconId, out PartNetworkOverlayIcon dot))
            {
                if (!s_iconsMissingLogged)
                {
                    s_iconsMissingLogged = true;
                    Log.Error("circulation overlay: the ZtxFlowArrow / ZtxFlowDot overlay icons are missing; no arrows drawn");
                }
                return;
            }
            if (s_icons == null) s_icons = new GraphicsList<AtlasQuad>(App.Graphics, PerformanceMode.DynamicStreaming);
            s_icons.Clear();
            float scale = ship.Rules.ShipScale;
            for (int pass = 0; pass < 2; pass++)
            {
                bool outline = pass == 0;
                Color c = outline ? ArrowOutline : ArrowCore;
                foreach (BlueprintFlowModel.ArrowDraw a in m.Draws)
                {
                    PartNetworkOverlayIcon icon = a.Kind == FlowKind.Still ? dot : arrow;
                    Vector2 size = icon.Sprite.Size * (outline ? OutlineScale : 1f);
                    Direction d = new Direction((float)Math.Atan2(a.Dir.Y, a.Dir.X));
                    s_icons.Add(new AtlasQuad(Rect.CenteredOn(a.Point, size), c, d, 0f, 1f, icon.Sprite, scale));
                }
            }
            App.Graphics.SetToMaterial(rules.OverlayIconMaterial);
            App.Graphics.Texture = rules.OverlayIconAtlas;
            App.Graphics.Draw(s_icons);
        }

        private static List<int> SelectedValves(BlueprintFlowModel m, BuildToolbox tb)
        {
            var result = new List<int>();
            if (!(tb.Game?.Gui?.GetSelectionState() is SelectPartsState sel)) return result;
            foreach (BlueprintPart p in sel._selectedParts)
                for (int i = 0; i < m.Nodes.Count; i++)
                    if (ReferenceEquals(m.Nodes[i].Part, p) && m.Nodes[i].IsValve) result.Add(i);
            return result;
        }

        private static void DrawReachLabel(Ship ship, BlueprintFlowModel m, int valve)
        {
            BloodNode node = m.Graph.NodeById(valve);
            IntRect r = m.Nodes[valve].Part.PhysicalRect;
            Vector2 local = new Vector2((r.Left + r.Right) * 0.5f, r.Top);
            Vector2 screen = ship.Sim.Viewport.TransformScreenPointFromWorld(ship.TransformPointToWorld(local));
            BaseSelectionState.DrawText(screen, string.Format(Strings.GetText("ZtxOverlay/Reach"),
                FieldBuilder.ReachPipes(node.PMax, node.Decay)), HAlignment.Center, VAlignment.Bottom, background: true);
        }

        private static void DrawHeldValve(BlueprintFlowModel m, BuildToolbox tb)
        {
            if (!(tb.Game?.Gui?.GetSelectionState() is PartBrushState brush) || brush.Part == null) return;
            foreach (PartComponentRules cr in brush.Part.Components)
            {
                if (!(cr is ZtxFlowNodeRules fr) || fr.Role != FlowRole.Valve) continue;
                int decay = fr.PressureDecay < 1 ? 1 : fr.PressureDecay;
                float heart = fr.PMaxBuff == default(ID<BuffType>) ? 1f
                    : (m.HeartByNetwork.TryGetValue(fr.Network, out float h) ? h : 0f);
                int pmax = PumpStrength.PMax(fr.PMax, heart, 100);
                BaseSelectionState.DrawTextUnderCursor(string.Format(Strings.GetText("ZtxOverlay/Reach"),
                    FieldBuilder.ReachPipes(pmax, decay)));
                return;
            }
        }

        private static void DrawOneReach(BlueprintFlowModel m, int valve)
        {
            var reach = FieldBuilder.ReachOf(m.Graph, valve);
            foreach (var kv in reach)
            {
                IntRect r = m.Nodes[kv.Key].Part.PhysicalRect;
                BaseSelectionState.DrawRect(new Rect(r.Left, r.Top, r.Width, r.Height), ReachBorder, ReachFill);
            }
        }

        private static void DrawHover(BuildToolbox tb, Ship ship, BlueprintFlowModel m)
        {
            if (tb.Game?.Gui?.Reacted == true) return;
            BlueprintPart hovered = ship.BlueprintParts.GetPartUnderWorldPoint(ship.Sim.WorldMouseLoc);
            if (hovered == null) return;
            var lines = new List<string>();
            string reach = null;
            for (int i = 0; i < m.Nodes.Count; i++)
            {
                var n = m.Nodes[i];
                if (!ReferenceEquals(n.Part, hovered)) continue;
                BloodNode node = m.Graph.NodeById(i);
                string label = (n.Rules.Network == Artery ? "artery" : n.Rules.Network == Vein ? "vein" : n.Rules.Network) +
                               (n.IsValve ? "" : n.Rules.AlwaysZeroPressure ? " in" : (n.Rules.PMax > 0 ? " out" : ""));
                lines.Add(string.Format(Strings.GetText("ZtxOverlay/Pressure"), label, node.Pressure));
                if (n.IsValve)
                {
                    lines.Add(string.Format(Strings.GetText("ZtxOverlay/Pump"), n.PressurePercent, n.FlowPercent));
                    reach = string.Format(Strings.GetText("ZtxOverlay/Reach"), FieldBuilder.ReachPipes(node.PMax, node.Decay));
                }
            }
            if (lines.Count == 0) return;
            if (reach != null) lines.Add(reach);
            BaseSelectionState.DrawTextUnderCursor(string.Join(", ", lines));
        }
    }
}
