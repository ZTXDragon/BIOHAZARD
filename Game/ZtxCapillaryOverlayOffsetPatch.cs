using System;
using Cosmoteer.Data;
using Cosmoteer.Game.Gui;
using Cosmoteer.Ships.Blueprints.Graphics;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    // Slide capillary port overlays SIDEWAYS along their face (v0.49.x) so the artery and vein
    // markers of a dual-network face sit beside each other instead of stacking on the face midpoint
    [HarmonyPatch(typeof(NetworkPortBlueprintOverlayRenderer), "GetPortDrawData")]
    internal static class ZtxCapillaryOverlayOffsetPatch
    {
        private const float Offset = 0.19f;

        private static PartNetworkGuiRules _cachedRules;
        private static PartNetworkOverlayIcon _arteryIcon;
        private static PartNetworkOverlayIcon _veinIcon;

        private static void Postfix(PartNetworkOverlayIcon icon,
                                    ref NetworkPortBlueprintOverlayRenderer.PortDrawData __result)
        {
            PartNetworkGuiRules rules = NetworkPortBlueprintOverlayRenderer.Rules;
            if (!ReferenceEquals(rules, _cachedRules))
            {
                _cachedRules = rules;
                rules.OverlayIcons.TryGetValue(new ID<PartNetworkOverlayIcon>("ArteryPortCap"), out _arteryIcon);
                rules.OverlayIcons.TryGetValue(new ID<PartNetworkOverlayIcon>("VeinPortCap"), out _veinIcon);
            }

            float side;
            if (ReferenceEquals(icon, _arteryIcon) && _arteryIcon != null) side = -1f;
            else if (ReferenceEquals(icon, _veinIcon) && _veinIcon != null) side = 1f;
            else return;

            // |sin| > 0.5 = facing Up or Down (Halfling angles: Up = -pi/2,
            // Down = +pi/2, y-down) = horizontal face = lateral axis is X.
            bool facesUpDown = MathF.Abs(MathF.Sin(__result.Direction.ToRadians())) > 0.5f;
            Vector2 shift = facesUpDown
                ? new Vector2(side * Offset, 0f)
                : new Vector2(0f, side * Offset);

            __result = new NetworkPortBlueprintOverlayRenderer.PortDrawData(
                __result.Midpoint + shift,
                __result.Direction,
                __result.Icon,
                __result.ConnectedEdgeIcon,
                __result.IsConnected,
                __result.IsRoutableFromPrimary);
        }
    }
}
