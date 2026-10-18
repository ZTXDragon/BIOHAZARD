using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Buffs;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Buffs;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Source.Ships.Blueprints;
using Halfling.Geometry;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    internal sealed class BlueprintFlowModel
    {
        internal sealed class NodeInfo
        {
            public BlueprintPart Part;
            public int PartIndex;
            public ZtxFlowNodeRules Rules;
            public int PressurePercent = 100;
            public int FlowPercent = 100;
            public bool IsValve;
        }

        internal const string ArteryNetwork = "ztx_artery";
        internal const string VeinNetwork = "ztx_vein";

        public readonly BloodGraph Graph = new BloodGraph();
        public readonly List<NodeInfo> Nodes = new List<NodeInfo>();
        public readonly List<BlueprintPart> Parts = new List<BlueprintPart>();
        public List<FlowArrow> Arrows = new List<FlowArrow>();
        public readonly Dictionary<string, float> HeartByNetwork = new Dictionary<string, float>();
        public int BuiltAtPartCount;

        public static BlueprintFlowModel Build(Ship ship)
        {
            var m = new BlueprintFlowModel();
            m.BuiltAtPartCount = ship.BlueprintParts.Count;
            var partIndex = new Dictionary<BlueprintPart, int>();
            var rects = new List<TopoRect>();
            var all = new List<(BlueprintPart Part, ZtxFlowNodeRules Rules, bool On)>();

            foreach (BlueprintPart bp in ship.BlueprintParts)
            {
                foreach (PartComponentRules cr in bp.Rules.Components)
                {
                    if (!(cr is ZtxFlowNodeRules fr)) continue;
                    if (!partIndex.ContainsKey(bp))
                    {
                        partIndex[bp] = rects.Count;
                        IntRect r = bp.PhysicalRect;
                        rects.Add(new TopoRect(r.Left, r.Top, r.Right, r.Bottom));
                        m.Parts.Add(bp);
                    }
                    all.Add((bp, fr, IsOn(ship, bp, fr)));
                }
            }

            var heart = HeartScales(ship, all, partIndex);

            var topo = new List<TopoNode>();
            var keep = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].On) continue;
                (BlueprintPart bp, ZtxFlowNodeRules fr, _) = all[i];
                int id = m.Nodes.Count;
                IntVector2 f = fr.Facing.GetShipRelative(bp.Rotation, bp.FlipX).ToVector();
                var info = new NodeInfo
                {
                    Part = bp, PartIndex = partIndex[bp], Rules = fr, IsValve = fr.Role == FlowRole.Valve,
                    PressurePercent = Mode(ship, bp, fr.PressureToggle),
                    FlowPercent = Mode(ship, bp, fr.FlowToggle),
                };
                m.Nodes.Add(info);
                int decay = fr.PressureDecay < 1 ? 1 : fr.PressureDecay;
                float scale = fr.PMaxBuff == default(ID<BuffType>) ? 1f : heart[i];
                if (fr.PMaxBuff != default(ID<BuffType>))
                    m.HeartByNetwork[fr.Network] = Math.Max(scale, m.HeartByNetwork.TryGetValue(fr.Network, out float h) ? h : 0f);
                m.Graph.Add(new BloodNode(id, ShipGraphAdapter.ToCoreRole(fr.Role), fr.Capacity, decay,
                                          PumpStrength.FlowRate(fr.FlowRate, scale, info.FlowPercent))
                {
                    PMax = PumpStrength.PMax(fr.PMax, scale, info.PressurePercent),
                    PumpDriven = fr.Role == FlowRole.Valve || fr.Directional,
                    PropagatePressure = fr.PropagatePressure,
                    Diffusion = fr.Diffusion,
                    PinPressureZero = fr.AlwaysZeroPressure,
                });
                topo.Add(new TopoNode
                {
                    Id = id, Part = partIndex[bp], Network = fr.Network, Role = ShipGraphAdapter.ToCoreRole(fr.Role),
                    Directional = fr.Directional, FaceOnly = fr.FaceOnly, StackPump = fr.StackPump,
                    FacingX = f.X, FacingY = f.Y,
                });
                keep.Add(i);
            }

            var links = new List<(int, int)>();
            for (int n = 0; n < keep.Count; n++)
            {
                (BlueprintPart bp, ZtxFlowNodeRules fr, _) = all[keep[n]];
                foreach (int other in LinkedParts(bp, fr.Network, partIndex)) links.Add((n, other));
            }
            TopologyBuilder.Wire(m.Graph, topo, rects, links, null);
            FieldBuilder.Build(m.Graph);
            m.Arrows = FlowDirection.Of(m.Graph);
            m.BuildDraws();
            return m;
        }

        internal readonly struct ArrowDraw
        {
            public readonly Vector2 Point;
            public readonly Vector2 Dir;
            public readonly bool Artery;
            public readonly FlowKind Kind;

            public ArrowDraw(Vector2 point, Vector2 dir, bool artery, FlowKind kind)
            {
                Point = point; Dir = dir; Artery = artery; Kind = kind;
            }
        }

        internal const float LaneOffset = 0.19f;
        public readonly List<ArrowDraw> Draws = new List<ArrowDraw>();

        private void BuildDraws()
        {
            var raw = new List<(Vector2 Point, Vector2 Dir, bool Artery, FlowKind Kind)>();
            foreach (FlowArrow a in Arrows)
            {
                NodeInfo from = Nodes[a.From], to = Nodes[a.To];
                if (ReferenceEquals(from.Part, to.Part)) continue;
                bool artery = from.Rules.Network == ArteryNetwork;
                int before = raw.Count;
                foreach ((Vector2 p, Vector2 d) in Joints(from.Part, to.Part, from.Rules.Network)) raw.Add((p, d, artery, a.Kind));
                if (raw.Count == before)
                    foreach ((Vector2 p, Vector2 d) in Joints(to.Part, from.Part, from.Rules.Network)) raw.Add((p, -d, artery, a.Kind));
                if (raw.Count == before && SharedFace(from.Part.PhysicalRect, to.Part.PhysicalRect, out Vector2 sp, out Vector2 sd))
                    raw.Add((sp, sd, artery, a.Kind));
            }
            var points = new List<(float X, float Y, bool Artery)>(raw.Count);
            foreach (var r in raw) points.Add((r.Point.X, r.Point.Y, r.Artery));
            bool[] lane = ArrowLanes.NeedsLane(points);
            for (int i = 0; i < raw.Count; i++)
            {
                Vector2 p = raw[i].Point;
                if (lane[i])
                {
                    CapillaryOverlay.Lateral(Math.Abs(raw[i].Dir.Y) > 0.5f, raw[i].Artery, LaneOffset, out float dx, out float dy);
                    p += new Vector2(dx, dy);
                }
                Draws.Add(new ArrowDraw(p, raw[i].Dir, raw[i].Artery, raw[i].Kind));
            }
        }

        private static List<(Vector2 Point, Vector2 Dir)> Joints(BlueprintPart from, BlueprintPart to, string network)
        {
            var result = new List<(Vector2, Vector2)>();
            IntRect target = to.PhysicalRect;
            foreach (BlueprintPartComponent c in from.Components)
            {
                if (!(c is BaseBlueprintPartNetworkPort port)) continue;
                if (!ShipGraphAdapter.PortCarriesNetwork(port.Rules, network)) continue;
                foreach (PortRectPair pr in port.GetPortRects())
                {
                    if (!pr.Search.HasValue || !pr.Search.Value.IntersectsWith(target)) continue;
                    Vector2 rc = Center(pr.Receiver), sc = Center(pr.Search.Value);
                    Vector2 point = (rc + sc) * 0.5f;
                    bool seen = false;
                    foreach ((Vector2 q, Vector2 _) in result)
                        if (Math.Abs(q.X - point.X) < ArrowLanes.SamePoint && Math.Abs(q.Y - point.Y) < ArrowLanes.SamePoint) seen = true;
                    if (!seen) result.Add((point, sc - rc));
                }
            }
            return result;
        }

        private static Vector2 Center(IntRect r) => new Vector2((r.Left + r.Right) * 0.5f, (r.Top + r.Bottom) * 0.5f);

        private static bool SharedFace(IntRect a, IntRect b, out Vector2 point, out Vector2 normal)
        {
            point = default; normal = default;
            if (a.Right == b.Left || b.Right == a.Left)
            {
                int top = Math.Max(a.Top, b.Top), bottom = Math.Min(a.Bottom, b.Bottom);
                if (bottom <= top) return false;
                float x = a.Right == b.Left ? a.Right : a.Left;
                point = new Vector2(x, (top + bottom) * 0.5f);
                normal = new Vector2(a.Right == b.Left ? 1f : -1f, 0f);
                return true;
            }
            if (a.Bottom == b.Top || b.Bottom == a.Top)
            {
                int left = Math.Max(a.Left, b.Left), right = Math.Min(a.Right, b.Right);
                if (right <= left) return false;
                float y = a.Bottom == b.Top ? a.Bottom : a.Top;
                point = new Vector2((left + right) * 0.5f, y);
                normal = new Vector2(0f, a.Bottom == b.Top ? 1f : -1f);
                return true;
            }
            return false;
        }

        private static IEnumerable<int> LinkedParts(BlueprintPart bp, string network, Dictionary<BlueprintPart, int> partIndex)
        {
            foreach (BlueprintPartComponent c in bp.Components)
            {
                if (!(c is BaseBlueprintPartNetworkPort port)) continue;
                if (!ShipGraphAdapter.PortCarriesNetwork(port.Rules, network)) continue;
                BaseBlueprintPartNetworkPort other = port.GetConnectedPort(default, default);
                if (other?.Part == null || ReferenceEquals(other.Part, bp)) continue;
                if (!ShipGraphAdapter.PortCarriesNetwork(other.Rules, network)) continue;
                if (partIndex.TryGetValue(other.Part, out int i)) yield return i;
            }
        }

        private static Func<ID<PartToggleGuiRules>, int?> ToggleModes(Ship ship, BlueprintPart bp)
        {
            return id => ship.Metadata.PartUIToggleStates.Get((new PartSaveData(bp), id), blueprintMode: true);
        }

        private static bool IsOn(Ship ship, BlueprintPart bp, ZtxFlowNodeRules fr)
        {
            if (fr.Toggle == default(ID<PartComponentRules>)) return true;
            return BlueprintToggle.IsOn(ToggleState(ship, bp, fr.Toggle, 0));
        }

        private const int MaxToggleDepth = 8;

        private static bool? ToggleState(Ship ship, BlueprintPart bp, ID<PartComponentRules> id, int depth)
        {
            if (depth > MaxToggleDepth) return null;
            if (!bp.Rules.ComponentsByID.TryGetValue(id, out PartComponentRules tr)) return null;
            if (tr is PartMultiToggleRules multi)
            {
                var subs = new List<bool?>();
                if (multi.Toggles != null)
                {
                    foreach (PartMultiToggleRules.ToggleRules sub in multi.Toggles)
                    {
                        bool? s = sub.Toggle == null ? null : ToggleState(ship, bp, sub.Toggle.ID, depth + 1);
                        subs.Add(s.HasValue ? s.Value != sub.Invert : (bool?)null);
                    }
                }
                return BlueprintToggle.Combine((MultiMode)(int)multi.Mode, subs, multi.Invert);
            }
            if (tr is IBlueprintComponentToggle answerable)
            {
                try
                {
                    return answerable.IsBlueprintToggleOn(bp.Rules, ToggleModes(ship, bp));
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
            return null;
        }

        private static int Mode(Ship ship, BlueprintPart bp, ID<PartComponentRules> id)
        {
            if (id == default(ID<PartComponentRules>)) return 100;
            if (!bp.Rules.ComponentsByID.TryGetValue(id, out PartComponentRules r)) return 100;
            return r is IBlueprintComponentMode mode ? mode.GetBlueprintMode(bp.Rules, ToggleModes(ship, bp)) : 100;
        }

        private static float[] HeartScales(Ship ship, List<(BlueprintPart Part, ZtxFlowNodeRules Rules, bool On)> all,
                                           Dictionary<BlueprintPart, int> partIndex)
        {
            var result = new float[all.Count];
            var groups = new Dictionary<(string Network, ID<PartNetworkCategory> Category), List<int>>();
            for (int i = 0; i < all.Count; i++)
            {
                (BlueprintPart bp, ZtxFlowNodeRules fr, _) = all[i];
                if (fr.PMaxBuff == default(ID<BuffType>)) continue;
                foreach (ID<PartNetworkCategory> cat in ReceiverCategories(bp, fr.PMaxBuff))
                {
                    var key = (fr.Network, cat);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
                    list.Add(i);
                }
            }
            foreach (var kv in groups)
            {
                string network = kv.Key.Network;
                var values = new float[partIndex.Count];
                foreach (var pair in partIndex) values[pair.Value] = HubValue(pair.Key, kv.Key.Category);
                var links = new List<(int, int)>();
                foreach (var pair in partIndex)
                    foreach (int other in LinkedParts(pair.Key, network, partIndex)) links.Add((pair.Value, other));
                float[] sums = HeartNetwork.ComponentSums(partIndex.Count, links, values);
                foreach (int i in kv.Value) result[i] = Math.Max(result[i], sums[partIndex[all[i].Part]]);
            }
            return result;
        }

        private static IEnumerable<ID<PartNetworkCategory>> ReceiverCategories(BlueprintPart bp, ID<BuffType> buff)
        {
            foreach (PartComponentRules cr in bp.Rules.Components)
            {
                if (!(cr is BasePartBuffProviderRules provider) || provider.BuffType?.BuffID != buff) continue;
                if (provider.BuffMultiplier == null) continue;
                if (!bp.Rules.ComponentsByID.TryGetValue(provider.BuffMultiplier.Value, out PartComponentRules mr)) continue;
                if (!(mr is PartNetworkValueReceiverRules receiver)) continue;
                var cats = receiver.NetworkFilter?.Categories;
                if (cats == null) continue;
                foreach (var c in cats) yield return c;
            }
        }

        private static float HubValue(BlueprintPart bp, ID<PartNetworkCategory> category)
        {
            float sum = 0f;
            foreach (PartComponentRules cr in bp.Rules.Components)
            {
                if (!(cr is PartNetworkValueRules nv) || nv.Categories == null) continue;
                if (Array.IndexOf(nv.Categories, category) < 0) continue;
                if (bp.Rules.ComponentsByID.TryGetValue(nv.ValueSource, out PartComponentRules vs) &&
                    vs is PartBuffableValueRules bv)
                    sum += bv.Value.GetValue(bp, 1f);
            }
            return sum;
        }
    }
}
