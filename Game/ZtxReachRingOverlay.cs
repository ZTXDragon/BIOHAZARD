using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Blueprints.Graphics;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.Overlays;
using Halfling.Geometry;
using Halfling.Graphics;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Build-mode ring showing how far a hauling organ reaches (2026-09-26).
    internal sealed class ZtxReachRingOverlay : IBlueprintOverlayRenderer
    {
        private struct Ring
        {
            public Vector2 Center;
            public float Radius;
            public int Segments;
            public CappedLine Line;
        }

        private readonly List<Ring> _rings = new List<Ring>();

        private readonly HashSet<IntRect> _seen = new HashSet<IntRect>();

        public void RefreshData(Ship ship, IReadOnlyList<PartOverlayRenderData> parts,
                                IBlueprintOverlaySettingsProvider settings, BlueprintOverlayFlags flags)
        {
            _rings.Clear();
            _seen.Clear();
            if (parts == null) return;
            if (settings != null && !settings.ShowBuffs) return;

            for (int i = 0; i < parts.Count; i++)
            {
                PartOverlayRenderData data = parts[i];
                PartRules rules = data.Info.Rules;
                if (rules == null) continue;

                List<PartComponentRules> components = rules.Components;
                for (int c = 0; c < components.Count; c++)
                {
                    if (!(components[c] is ZtxHaulerRules hauler) || hauler.ReachRingLine == null) continue;

                    // Blueprint buffs if the part is on the blueprint, the authored base if it is
                    // only a ghost at the cursor (GetValue is null-safe).
                    float radius = ReachRing.Tiles(hauler.Range.GetValue(data.Part, 1f));
                    int segments = ReachRing.SegmentCount(radius);
                    if (segments == 0 || !_seen.Add(data.Info.Rect)) continue;

                    _rings.Add(new Ring
                    {
                        Center = data.Info.LocalCenter,
                        Radius = radius,
                        Segments = segments,
                        Line = hauler.ReachRingLine,
                    });
                }
            }
        }

        public void DrawEarly(Ship ship, IBlueprintOverlaySettingsProvider settings)
        {
        }

        public void Draw(Ship ship, IBlueprintOverlaySettingsProvider settings)
        {
        }

        public void DrawLate(Ship ship, IBlueprintOverlaySettingsProvider settings)
        {
            if (settings != null && !settings.ShowBuffs) return;

            for (int i = 0; i < _rings.Count; i++)
            {
                Ring ring = _rings[i];
                ReachRing.Vertex(ring.Radius, 0, ring.Segments, out float px, out float py);
                for (int k = 1; k <= ring.Segments; k++)
                {
                    ReachRing.Vertex(ring.Radius, k, ring.Segments, out float x, out float y);
                    ring.Line.Draw(ring.Center + new Vector2(px, py), ring.Center + new Vector2(x, y), Color.White);
                    px = x;
                    py = y;
                }
            }
        }

        public void DrawLimited(Ship ship, IBlueprintOverlaySettingsProvider settings)
        {
        }

        public void Dispose()
        {
        }
    }
}
