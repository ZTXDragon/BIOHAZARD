using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Rendering;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Simulation.MediaEffects;
using Vector2 = Halfling.Geometry.Vector2;

namespace ZTX.BioCirculation.Game
{
    // One arm of a tentacle part: its anchor, its joint chain, and its media nodes.
    internal sealed class TentacleArm
    {
        public Vector2 Anchor;

        public Vector2 Outward;

        public Vector2[] Joints;

        public Vector2 TipGoal;

        public Vector2[] PrevJoints;

        public ManagedShipQuad[] Quads;


        public float Phase;
    }

    // Lifecycle owner for a part's animated tentacle arms.
    internal class ZtxTentacle : PartComponent
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Part, ZtxTentacle> s_byPart =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Part, ZtxTentacle>();

        public static ZtxTentacle GetFor(Part p)
        {
            if (p == null) return null;
            s_byPart.TryGetValue(p, out ZtxTentacle t);
            return t;
        }

        private ZtxTentacleManager _manager;
        private IComponentToggleProvider _toggle;
        private ZtxHauler _hauler;

        public TentacleArm[] Arms;

        public new ZtxTentacleRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxTentacleRules>(base.Rules); }
        }

        public ZtxTentacle(ZtxTentacleRules rules) : base(rules)
        {
        }

        public ZtxHauler Hauler => _hauler;

        public bool IsUsable
        {
            get
            {
                Part p = base.Part;
                if (p == null) return false;
                if (p.IsUnderConstruction) return false;
                return _toggle == null || _toggle.IsToggleOn;
            }
        }

        public bool TryGetRestTipLocal(int armIndex, out Vector2 restLocal)
        {
            restLocal = default(Vector2);
            Part p = base.Part;
            if (p == null) return false;

            ZtxTentacleRules r = Rules;
            Vector2 sizeRules = new Vector2(p.Rules.Size.X, p.Rules.Size.Y);
            Vector2 centerShip = p.TransformPointFromRules(sizeRules * 0.5f);

            Vector2[] anchorsRules = r.AnchorOffsets;
            int count = anchorsRules != null && anchorsRules.Length > 0
                ? anchorsRules.Length
                : (r.TentacleCount > 0 ? r.TentacleCount : 1);
            int i = armIndex % count;
            if (i < 0) i += count;

            Vector2 anchorRules = anchorsRules != null && anchorsRules.Length > 0
                ? anchorsRules[i]
                : ZtxTentacleManager.PerimeterAnchor(sizeRules, i, count);

            Vector2 anchor = p.TransformPointFromRules(anchorRules);
            Vector2 outward = anchor - centerShip;
            if (outward.LengthSquared > 1e-6f)
            {
                outward = outward.Normalize();
            }
            else
            {
                outward = p.TransformPointFromRules(sizeRules * 0.5f + new Vector2(0f, -1f)) - centerShip;
                outward = outward.LengthSquared > 1e-6f ? outward.Normalize() : new Vector2(0f, -1f);
            }

            int segs = r.SegmentCount > 0 ? r.SegmentCount : 1;
            float len = r.SegmentLength > 0f ? r.SegmentLength : 0.5f;
            restLocal = anchor + outward * (segs * len * r.IdleExtension);
            return true;
        }

        public void ReleaseNodes(bool immediate)
        {
            if (Arms == null) return;
            for (int a = 0; a < Arms.Length; a++)
            {
                ManagedShipQuad[] quads = Arms[a]?.Quads;
                if (quads == null) continue;
                for (int i = 0; i < quads.Length; i++)
                {
                    try { if (quads[i] != null) quads[i].Sprite = null; }
                    catch (Exception ex) { Log.Exception("ZtxTentacle.ReleaseNodes", ex); }
                    quads[i] = null;
                }
                Arms[a].Quads = null;
            }
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();

            if (Rules.Hauler != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<ZtxHauler>(Rules.Hauler, out _hauler);
                if (_hauler == null)
                    Log.Error("ZtxTentacle on " + base.Part + ": Hauler=" + Rules.Hauler +
                              " is not a ZtxHauler; arms will idle only.");
            }
            if (Rules.Toggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                    Log.Error("ZtxTentacle on " + base.Part + ": Toggle=" + Rules.Toggle +
                              " is not a toggle; arms will run ungated.");
            }
            if (Rules.SegmentSprite == null)
                Log.Error("ZtxTentacle on " + base.Part + ": SegmentSprite is null — nothing will render.");

            s_byPart.Remove(base.Part);
            s_byPart.Add(base.Part, this);

            _manager = ZtxTentacleManager.GetOrCreate(base.Ship);
            _manager?.Register(this);

            // Per part, so verbose only: every log line is a ~3 ms disk flush (see Log.Verbose).
            Log.Verbose("tentacle attached on " + base.Part + " at " + base.Part.Location +
                        " sprite=" + (Rules.SegmentSprite != null ? "ok" : "NULL") +
                        " manager=" + (_manager != null ? "ok" : "NULL"));
        }

        public override void OnPartDetaching()
        {
            if (base.Part != null) s_byPart.Remove(base.Part);
            ReleaseNodes(true);
            Arms = null;
            _manager?.Deregister(this);
            _manager = null;
            _toggle = null;
            _hauler = null;
            base.OnPartDetaching();
        }
    }
}
