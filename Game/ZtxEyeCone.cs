using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Halfling.Geometry;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Directional sight: the eye reveals a CONE along its own facing instead of inflating the
    // ship's sight circle.
    internal class ZtxEyeCone : PartComponent
    {
        private static readonly ID<PartCategory> CartilageCategory =
            new ID<PartCategory>("bio_cartilage");

        private static readonly List<ZtxEyeCone> s_all = new List<ZtxEyeCone>();

        public static IReadOnlyList<ZtxEyeCone> All => s_all;

        private IComponentToggleProvider _toggle;

        private object[] _keys;

        private ArcSpan _span;
        private int _scannedAtPartCount = -1;

        public new ZtxEyeConeRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxEyeConeRules>(base.Rules); }
        }

        public ZtxEyeCone(ZtxEyeConeRules rules) : base(rules)
        {
        }

        public override Direction RulesRotation
        {
            get
            {
                switch (Rules.Facing)
                {
                    case OrthogonalRotation.Up: return Direction.Up;
                    case OrthogonalRotation.Down: return Direction.Down;
                    case OrthogonalRotation.Left: return Direction.Left;
                    default: return Direction.Right;
                }
            }
        }

        public bool IsActive
        {
            get
            {
                Part p = base.Part;
                if (p == null || p.Ship == null || p.Health <= 0) return false;
                return _toggle == null || _toggle.IsToggleOn;
            }
        }

        public float OpenArcDegrees => _span.Width(Rules.ScanRays, (float)Rules.Arc * 180f / (float)Math.PI);

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            if (Rules.Toggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                {
                    Log.Error("ZtxEyeCone on " + base.Part + " declares Toggle=" + Rules.Toggle +
                              " but no such toggle exists on the part; it will see ungated.");
                }
            }
            int n = Rules.MaxDiscs > 0 ? Rules.MaxDiscs : 1;
            _keys = new object[n];
            for (int i = 0; i < n; i++) _keys[i] = new object();
            _scannedAtPartCount = -1;
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        public override void OnPartDetaching()
        {
            s_all.Remove(this);
            _toggle = null;
            base.OnPartDetaching();
        }

        private static bool SeesThrough(Part hit)
        {
            return Config.EyesSeeThroughCartilage
                && hit.Rules?.TypeCategories != null
                && hit.Rules.TypeCategories.Contains(CartilageCategory);
        }

        private void Rescan(Ship ship)
        {
            int rays = Rules.ScanRays > 0 ? Rules.ScanRays : 1;
            int reach = Rules.ScanDistance > 0 ? Rules.ScanDistance : 1;
            float arc = (float)Rules.Arc;
            float baseAngle = (float)base.ShipDirection;
            Vector2 from = base.Part.LocalCenter;

            var open = new bool[rays];
            for (int i = 0; i < rays; i++)
            {
                // the ray through the middle of slice i
                float a = baseAngle - arc * 0.5f + arc * (i + 0.5f) / rays;
                float dx = (float)Math.Cos(a);
                float dy = (float)Math.Sin(a);
                bool blocked = false;
                for (int step = 1; step <= reach && !blocked; step++)
                {
                    var cell = new IntVector2((int)Math.Floor(from.X + dx * step),
                                              (int)Math.Floor(from.Y + dy * step));
                    Part hit = ship.Parts[cell];
                    if (hit != null && !ReferenceEquals(hit, base.Part) && !SeesThrough(hit))
                    {
                        blocked = true;
                    }
                }
                open[i] = !blocked;
            }
            _span = ArcScan.LargestOpenSpan(open);
            _scannedAtPartCount = ship.Parts.Count;
        }

        public void AppendSightCircles(ICollection<(Circle Circle, object Source)> sink)
        {
            Part p = base.Part;
            Ship ship = p?.Ship;
            if (ship == null || _keys == null) return;

            if (_scannedAtPartCount != ship.Parts.Count) Rescan(ship);
            if (_span.Length <= 0) return;                  // walled in: sees nothing

            int rays = Rules.ScanRays > 0 ? Rules.ScanRays : 1;
            float arcDeg = (float)Rules.Arc * 180f / (float)Math.PI;
            float widthDeg = _span.Width(rays, arcDeg);
            float centreDeg = _span.CentreOffset(rays, arcDeg);
            if (widthDeg <= 0f) return;

            const float ToRad = (float)Math.PI / 180f;
            float dir = (float)base.DetWorldDirection + centreDeg * ToRad;
            float half = widthDeg * 0.5f * ToRad;

            Vector2 origin = ship.DetTransformPointToWorld(p.LocalCenter);
            List<Disc> discs = ConeGeometry.Build(origin.X, origin.Y, dir, half,
                                                  Rules.Range, _keys.Length, Rules.StartDistance);
            int n = discs.Count < _keys.Length ? discs.Count : _keys.Length;
            for (int i = 0; i < n; i++)
            {
                Disc d = discs[i];
                sink.Add((new Circle(new Vector2(d.X, d.Y), d.Radius), _keys[i]));
            }
        }

        public override string GetDebuggerInlineInfo()
        {
            if (!IsActive) return "off";
            float arcDeg = (float)Rules.Arc * 180f / (float)Math.PI;
            return _span.Length <= 0
                ? "WALLED IN (no cone)"
                : string.Format("{0:0}deg of {1:0}deg, offset {2:0}deg, range {3:0}",
                                _span.Width(Rules.ScanRays, arcDeg), arcDeg,
                                _span.CentreOffset(Rules.ScanRays, arcDeg), Rules.Range);
        }
    }
}
