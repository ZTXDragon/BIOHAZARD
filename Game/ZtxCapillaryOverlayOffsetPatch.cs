using System;
using Cosmoteer.Data;
using Cosmoteer.Game.Gui;
using Cosmoteer.Ships.Blueprints.Graphics;
using Halfling.Geometry;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
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

            bool artery;
            if (ReferenceEquals(icon, _arteryIcon) && _arteryIcon != null) artery = true;
            else if (ReferenceEquals(icon, _veinIcon) && _veinIcon != null) artery = false;
            else return;

            bool facesUpDown = MathF.Abs(MathF.Sin(__result.Direction.ToRadians())) > 0.5f;
            CapillaryOverlay.Lateral(facesUpDown, artery, Offset, out float dx, out float dy);
            Vector2 shift = new Vector2(dx, dy);

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
