using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Blueprints.Graphics;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Source.Ships.Blueprints;

namespace ZTX.BioCirculation.Game
{
    internal static class CirculationNetworkDisplay
    {
        private sealed class OverlaySettings : IBlueprintOverlaySettingsProvider
        {
            public bool IsValid => true;

            public bool ShowBuffs => false;

            public int GetToggleMode(Ship ship, PartInfo part, ID<PartToggleGuiRules> id)
            {
                return ship.Metadata.PartUIToggleStates.Get((new PartSaveData(part), id), blueprintMode: true)
                       ?? part.Rules.UIToggleComponentsByID[id].Default;
            }
        }

        private static readonly OverlaySettings s_settings = new OverlaySettings();
        private static NetworkPortBlueprintOverlayRenderer s_renderer;
        private static BlueprintFlowModel s_shownModel;

        internal static void Draw(Ship ship, BlueprintFlowModel m)
        {
            if (s_renderer == null) s_renderer = new NetworkPortBlueprintOverlayRenderer();
            if (!ReferenceEquals(m, s_shownModel))
            {
                s_shownModel = m;
                Refresh(ship, m);
            }
            s_renderer.DrawLate(ship, s_settings);
        }

        private static void Refresh(Ship ship, BlueprintFlowModel m)
        {
            var parts = new List<PartOverlayRenderData>();
            foreach (BlueprintPart bp in m.Parts)
                if (HasBloodPort(bp)) parts.Add(new PartOverlayRenderData(new PartInfo(bp), bp));
            s_renderer.RefreshData(ship, parts, s_settings, BlueprintOverlayFlags.NoPlacementInfo);
        }

        private static bool HasBloodPort(BlueprintPart bp)
        {
            foreach (BlueprintPartComponent c in bp.Components)
            {
                if (!(c is BaseBlueprintPartNetworkPort port)) continue;
                if (ShipGraphAdapter.PortCarriesNetwork(port.Rules, BlueprintFlowModel.ArteryNetwork) ||
                    ShipGraphAdapter.PortCarriesNetwork(port.Rules, BlueprintFlowModel.VeinNetwork))
                    return true;
            }
            return false;
        }
    }
}
