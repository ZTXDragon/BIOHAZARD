using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling;
using Halfling.Graphics;
using Cosmoteer.Ships.Rendering;
using Cosmoteer.Simulation.MediaEffects;
using Halfling.Geometry;
using Halfling.Scene2D;
using ZTX.BioCirculation.Core;
using Vector2 = Halfling.Geometry.Vector2;

namespace ZTX.BioCirculation.Game
{
    internal class ZtxTentacleManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, ZtxTentacleManager> s_managers =
            new ConditionalWeakTable<Ship, ZtxTentacleManager>();

        private readonly List<ZtxTentacle> _tentacles = new List<ZtxTentacle>();

        private struct TargetInfo
        {
            public Vector2 Point;
            public bool Pinned;
        }

        private readonly List<TargetInfo> _targets = new List<TargetInfo>();

        private double _lastTime = double.NaN;
        private bool _wasVisible;
        private bool _loggedAlive;
        private int _frameCounter;

        public ZtxTentacleManager()
        {
            base.UpdateBucket = 7;
            base.UpdatingEnabled = true;
        }

        public static ZtxTentacleManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out ZtxTentacleManager manager)) return manager;

            manager = new ZtxTentacleManager();
            ZtxTentacleManager captured = manager;
            Ship capturedShip = ship;

            if (ship.Sim != null)
            {
                ship.Sim.EnqueueDeterministic(ship.UniqueID, delegate
                {
                    capturedShip.Components.Add(captured);
                });
            }
            else
            {
                ship.Components.Add(manager);
            }

            s_managers.Add(ship, manager);
            return manager;
        }

        private Vector2[] _constraintScratch = new Vector2[32];

        public void Register(ZtxTentacle t)
        {
            if (t != null) _tentacles.Add(t);
        }

        public void Deregister(ZtxTentacle t)
        {
            if (t != null) _tentacles.Remove(t);
        }

        public override void Update(SceneRoot root)
        {
            if (!_loggedAlive)
            {
                _loggedAlive = true;
                Log.Info("tentacle manager frame update ALIVE, " + _tentacles.Count + " part(s) registered");
            }

            if (!Config.Enabled || _tentacles.Count == 0) return;
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            bool visible = ship.IsWithinSightOfLocalPlayer;
            if (!visible)
            {
                if (_wasVisible)
                    for (int i = 0; i < _tentacles.Count; i++) _tentacles[i]?.ReleaseNodes(false);
                _wasVisible = false;
                _lastTime = double.NaN;
                return;
            }
            _wasVisible = true;

            double now = ship.Sim.Clock.Time.Seconds;
            float dt = double.IsNaN(_lastTime) ? 0f : (float)(now - _lastTime);
            if (dt < 0f) dt = 0f;
            if (dt > 0.1f) dt = 0.1f;
            _lastTime = now;
            float time = (float)now;

            for (int i = 0; i < _tentacles.Count; i++)
            {
                ZtxTentacle t = _tentacles[i];
                Part p = t?.Part;
                if (p == null) continue;
                if (!ReferenceEquals(p.Ship, ship)) continue;

                if (!t.IsUsable)
                {
                    t.ReleaseNodes(false);
                    continue;
                }

                try
                {
                    if (t.Arms == null)
                    {
                        BuildArms(t, p);
                        Log.Info("tentacle arms built on " + p + ": " + t.Arms.Length +
                                 " arm(s) x " + (t.Arms[0].Joints.Length - 1) + " segment(s), anchor0=" +
                                 t.Arms[0].Anchor + " outward0=" + t.Arms[0].Outward);
                    }
                    ResolveTargets(t, ship);
                    AnimatePart(t, p, ship, dt, time);
                }
                catch (Exception ex)
                {
                    Log.Exception("ZtxTentacleManager.Update", ex);
                    t.ReleaseNodes(true);
                    t.Arms = null;
                }
            }

            if (++_frameCounter >= 600 && Config.VerboseLog)
            {
                _frameCounter = 0;
                int arms = 0, nodes = 0;
                for (int i = 0; i < _tentacles.Count; i++)
                {
                    TentacleArm[] a = _tentacles[i]?.Arms;
                    if (a == null) continue;
                    arms += a.Length;
                    for (int j = 0; j < a.Length; j++) nodes += a[j]?.Quads?.Length ?? 0;
                }
                Log.Verbose("tentacle heartbeat: " + _tentacles.Count + " part(s), " + arms +
                            " arm(s), " + nodes + " live node(s), visible=" + _wasVisible);
            }
        }

        private static void BuildArms(ZtxTentacle t, Part p)
        {
            ZtxTentacleRules r = t.Rules;
            int segs = r.SegmentCount > 0 ? r.SegmentCount : 1;
            float len = r.SegmentLength > 0f ? r.SegmentLength : 0.5f;
            Ship ship = p.Ship;

            Vector2 sizeRules = new Vector2(p.Rules.Size.X, p.Rules.Size.Y);
            Vector2 centerShip = p.TransformPointFromRules(sizeRules * 0.5f);

            Vector2[] anchorsRules = r.AnchorOffsets;
            if (anchorsRules == null || anchorsRules.Length == 0)
                anchorsRules = PerimeterAnchors(sizeRules, r.TentacleCount > 0 ? r.TentacleCount : 1);

            var arms = new TentacleArm[anchorsRules.Length];
            for (int a = 0; a < arms.Length; a++)
            {
                Vector2 anchor = p.TransformPointFromRules(anchorsRules[a]);
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

                Vector2 anchorW = ship.TransformPointToWorld(anchor);
                Vector2 outwardW = ship.TransformPointToWorld(anchor + outward) - anchorW;
                outwardW = outwardW.LengthSquared > 1e-6f ? outwardW.Normalize() : outwardW;

                var joints = new Vector2[segs + 1];
                var prev = new Vector2[segs + 1];
                for (int j = 0; j <= segs; j++) { joints[j] = anchorW + outwardW * (len * j); prev[j] = joints[j]; }

                arms[a] = new TentacleArm
                {
                    Anchor = anchor,
                    Outward = outward,
                    Joints = joints,
                    PrevJoints = prev,
                    TipGoal = joints[segs],
                    Phase = (((p.Location.X * 73856093) ^ (p.Location.Y * 19349663) ^ (a * 83492791))
                             & 0x3FF) * (6.2831853f / 1024f),
                };
            }
            t.Arms = arms;
        }

        private static Vector2[] PerimeterAnchors(Vector2 size, int count)
        {
            var result = new Vector2[count];
            for (int i = 0; i < count; i++) result[i] = PerimeterAnchor(size, i, count);
            return result;
        }

        internal static Vector2 PerimeterAnchor(Vector2 size, int i, int count)
        {
            float w = size.X, h = size.Y;
            float perim = 2f * (w + h);
            float d = (i + 0.5f) / count * perim;
            if (d < w) return new Vector2(d, 0f);
            if (d < w + h) return new Vector2(w, d - w);
            if (d < w + h + w) return new Vector2(w - (d - w - h), h);
            return new Vector2(0f, h - (d - w - h - w));
        }

        private void ResolveTargets(ZtxTentacle t, Ship ship)
        {
            _targets.Clear();
            ZtxHauler h = t.Hauler;
            if (h == null) return;

            var salvage = h.SalvageClaim;
            Part sp = salvage?.SalvagePart;
            if (sp != null && !sp.IsDestroyed && sp.Ship != null && sp.ExistsInSim(ship.Sim) &&
                MiningRule.AcceptsTarget(ReferenceEquals(sp.Ship, ship), h.CanMine))
                _targets.Add(new TargetInfo { Point = sp.Ship.TransformPointToWorld(sp.LocalCenter) });

            Part bp = h.BuildClaim;
            if (bp != null && bp.IsUnderConstruction && ReferenceEquals(bp.Ship, ship))
                _targets.Add(new TargetInfo { Point = ship.TransformPointToWorld(bp.LocalCenter) });

            var haul = h.Claim;
            Part hp = haul?.Source?.Part;
            if (hp != null && ReferenceEquals(hp.Ship, ship))
                _targets.Add(new TargetInfo { Point = ship.TransformPointToWorld(hp.LocalCenter) });
        }

        private static void ClampBend(Vector2[] joints, int segs, float maxBend)
        {
            for (int j = 1; j < segs; j++)
            {
                Vector2 prev = joints[j] - joints[j - 1];
                float plen = prev.Length;
                if (plen <= 1e-5f) continue;
                Vector2 cur = joints[j + 1] - joints[j];
                float clen = cur.Length;
                if (clen <= 1e-5f) continue;

                float inv = 1f / plen;
                float pdx = prev.X * inv, pdy = prev.Y * inv;
                float cdx = cur.X / clen, cdy = cur.Y / clen;
                float ang = (float)Math.Atan2(pdx * cdy - pdy * cdx, pdx * cdx + pdy * cdy);
                if (ang <= maxBend && ang >= -maxBend) continue;

                if (ang > maxBend) ang = maxBend; else ang = -maxBend;
                float s = (float)Math.Sin(ang), c = (float)Math.Cos(ang);
                joints[j + 1] = new Vector2(joints[j].X + (pdx * c - pdy * s) * clen,
                                            joints[j].Y + (pdx * s + pdy * c) * clen);
            }
        }

        private void AnimatePart(ZtxTentacle t, Part p, Ship ship, float dt, float time)
        {
            ZtxTentacleRules r = t.Rules;
            float len = r.SegmentLength > 0f ? r.SegmentLength : 0.5f;
            float omega = r.SwaySpeed * 6.2831853f;
            float maxBend = r.MaxBendAngle * 0.017453292f;
            bool clampBend = maxBend > 0f && maxBend < 3.14159f;

            Ship shipW = p.Ship;

            ZtxHauler hs = t.Hauler;
            ArmTipSlot[] slots = (hs != null && hs.CanCarry) ? hs.Slots : null;
            float tval = ship.Sim.FixedUpdater.TValue;
            int misc = 0;

            bool limp = !ZtxBrainManager.IsConscious(p);

            for (int a = 0; a < t.Arms.Length; a++)
            {
                TentacleArm arm = t.Arms[a];
                Vector2[] joints = arm.Joints;
                int segs = joints.Length - 1;
                float reach = segs * len;

                Vector2 anchorW = shipW.TransformPointToWorld(arm.Anchor);
                Vector2 outwardW = shipW.TransformPointToWorld(arm.Anchor + arm.Outward) - anchorW;
                float owLen = outwardW.Length;
                if (owLen > 1e-5f) outwardW *= 1f / owLen; else outwardW = new Vector2(0f, -1f);

                if (limp)
                {
                    Vector2[] prevJ = arm.PrevJoints;
                    float damp = r.LimpDamping;
                    if (damp < 0f) damp = 0f;
                    if (damp > 1f) damp = 1f;
                    float dragK = t.Rules.RagdollDrag;
                    for (int j = 1; j <= segs; j++)
                    {
                        Vector2 cur = joints[j];
                        Vector2 vel = (cur - prevJ[j]) * damp;
                        if (dragK > 0f)
                            vel *= 1f / (1f + dragK * ((float)j / segs) * vel.Length);
                        joints[j] = cur + vel;
                        prevJ[j] = cur;
                    }
                    joints[0] = anchorW;
                    prevJ[0] = anchorW;

                    if (_constraintScratch.Length < segs + 1) _constraintScratch = new Vector2[segs + 1];
                    for (int j = 0; j <= segs; j++) _constraintScratch[j] = joints[j];
                    for (int iter = 0; iter < 3; iter++)
                    {
                        joints[0] = anchorW;
                        for (int j = 0; j < segs; j++)
                        {
                            Vector2 dl = joints[j + 1] - joints[j];
                            float distl = dl.Length;
                            if (distl <= 1e-5f)
                            {
                                joints[j + 1] = joints[j] + outwardW * len;
                                continue;
                            }
                            float err = (distl - len) / distl;
                            if (j == 0)
                            {
                                joints[1] -= dl * err;
                            }
                            else
                            {
                                Vector2 half = dl * (0.5f * err);
                                joints[j] += half;
                                joints[j + 1] -= half;
                            }
                        }
                        if (clampBend) ClampBend(joints, segs, maxBend);
                    }
                    joints[0] = anchorW;
                    for (int j = 0; j < segs; j++)
                    {
                        Vector2 dx = joints[j + 1] - joints[j];
                        float distx = dx.Length;
                        joints[j + 1] = distx > 1e-5f ? joints[j] + dx * (len / distx)
                                                      : joints[j] + outwardW * len;
                    }
                    if (clampBend) ClampBend(joints, segs, maxBend);
                    for (int j = 1; j <= segs; j++) prevJ[j] += joints[j] - _constraintScratch[j];
                    arm.TipGoal = joints[segs];
                    RenderArm(t, arm, ship, r, segs);
                    continue;
                }

                Vector2 target;
                bool pinned = false;
                bool working = false;
                ArmTipSlot slot = (slots != null && a < slots.Length) ? slots[a] : null;
                if (slot != null && TentacleFollow.OnSimTip(slot.Initialized, slot.Job != null, slot.Homing, slot.Holding))
                {
                    target = Vector2.Lerp(slot.PrevTip, slot.Tip, tval);
                    pinned = true;
                }
                else if (misc < _targets.Count)
                {
                    target = _targets[misc++].Point;
                    working = true;
                }
                else
                {
                    float halfFan = r.IdleSweepDegrees * 0.5f * 0.017453292f;
                    float roam = (float)Math.Sin(time * omega * 0.35f + arm.Phase * 1.7f) * halfFan;
                    float ca = (float)Math.Cos(roam), sa = (float)Math.Sin(roam);
                    Vector2 dir = new Vector2(outwardW.X * ca - outwardW.Y * sa,
                                              outwardW.X * sa + outwardW.Y * ca);
                    Vector2 perp = new Vector2(-dir.Y, dir.X);
                    float wander = (float)Math.Sin(time * omega * 0.5f + arm.Phase)
                                   * r.SwayAmplitude * 1.5f;
                    float breathe = 0.85f + 0.15f * (float)Math.Sin(time * omega * 0.23f + arm.Phase);
                    float idle = ArmReach.IdleTiles(hs != null ? hs.RangeTiles : 0f, reach, r.IdleExtension);
                    target = anchorW + dir * (idle * breathe)
                             + perp * wander;
                }

                if (!pinned)
                {
                    Vector2[] prevJ = arm.PrevJoints;
                    float pdamp = r.LimpDamping;
                    if (pdamp < 0f) pdamp = 0f;
                    if (pdamp > 1f) pdamp = 1f;
                    float dt2 = dt * dt;

                    float anchorSpeed = 0f;
                    var shipBody = shipW.Physics != null ? shipW.Physics.Body : null;
                    if (shipBody != null)
                        anchorSpeed = ((Vector2)shipBody.GetLinearVelocityFromWorldPoint(anchorW)).Length;
                    float motionScale = anchorSpeed <= 1.5f ? 1f
                                      : (anchorSpeed >= 4f ? 0f : (4f - anchorSpeed) / 2.5f);

                    float overStretch = (target - anchorW).Length - reach;
                    float stretchScale = overStretch <= 0f ? 1f
                                       : (overStretch >= 1f ? 0f : 1f - overStretch);

                    float steerScale = motionScale < stretchScale ? motionScale : stretchScale;

                    float wave = r.SwayAmplitude * 55f * steerScale;

                    Vector2 tipSteer = (target - joints[segs])
                                       * ((r.TipPull > 0f ? r.TipPull : 30f) * steerScale);

                    for (int j = 1; j <= segs; j++)
                    {
                        Vector2 cur = joints[j];

                        Vector2 seg = cur - joints[j - 1];
                        float slen = seg.Length;
                        Vector2 perp = slen > 1e-5f
                            ? new Vector2(-seg.Y / slen, seg.X / slen)
                            : new Vector2(-outwardW.Y, outwardW.X);

                        float ramp = (float)j / segs;
                        float waveRamp = r.BaseWaveFraction + (1f - r.BaseWaveFraction) * ramp;
                        Vector2 accel = perp * ((float)Math.Sin(time * omega + arm.Phase - j * 0.7f)
                                                * wave * waveRamp);
                        if (j == segs) accel += tipSteer;

                        Vector2 vel = (cur - prevJ[j]) * pdamp;
                        float dragK2 = r.RagdollDrag * (1f - steerScale);
                        if (dragK2 > 0f)
                            vel *= 1f / (1f + dragK2 * ramp * vel.Length);
                        joints[j] = cur + vel + accel * dt2;
                        prevJ[j] = cur;
                    }
                    joints[0] = anchorW;
                    prevJ[0] = anchorW;

                    if (_constraintScratch.Length < segs + 1) _constraintScratch = new Vector2[segs + 1];
                    for (int j = 0; j <= segs; j++) _constraintScratch[j] = joints[j];
                    for (int iter = 0; iter < 3; iter++)
                    {
                        joints[0] = anchorW;
                        for (int j = 0; j < segs; j++)
                        {
                            Vector2 dl = joints[j + 1] - joints[j];
                            float distl = dl.Length;
                            if (distl <= 1e-5f)
                            {
                                joints[j + 1] = joints[j] + outwardW * len;
                                continue;
                            }
                            float err = (distl - len) / distl;
                            if (j == 0) joints[1] -= dl * err;
                            else
                            {
                                Vector2 half = dl * (0.5f * err);
                                joints[j] += half;
                                joints[j + 1] -= half;
                            }
                        }
                        if (clampBend && !working) ClampBend(joints, segs, maxBend);
                    }
                    joints[0] = anchorW;
                    for (int j = 0; j < segs; j++)
                    {
                        Vector2 dx = joints[j + 1] - joints[j];
                        float distx = dx.Length;
                        joints[j + 1] = distx > 1e-5f ? joints[j] + dx * (len / distx)
                                                      : joints[j] + outwardW * len;
                    }
                    if (clampBend && !working) ClampBend(joints, segs, maxBend);
                    for (int j = 1; j <= segs; j++) prevJ[j] += joints[j] - _constraintScratch[j];
                    arm.TipGoal = joints[segs];
                    RenderArm(t, arm, ship, r, segs);
                    continue;
                }

                arm.TipGoal = target;

                joints[segs] = arm.TipGoal;
                for (int j = segs - 1; j >= 0; j--)
                {
                    Vector2 d = joints[j] - joints[j + 1];
                    float dist = d.Length;
                    joints[j] = dist > 1e-5f ? joints[j + 1] + d * (len / dist)
                                             : joints[j + 1] - outwardW * len;
                }
                joints[0] = anchorW;
                for (int j = 0; j < segs; j++)
                {
                    Vector2 d = joints[j + 1] - joints[j];
                    float dist = d.Length;
                    joints[j + 1] = dist > 1e-5f ? joints[j] + d * (len / dist)
                                                 : joints[j] + outwardW * len;
                }

                float pinAnchorSpeed = 0f;
                var pinBody = shipW.Physics != null ? shipW.Physics.Body : null;
                if (pinBody != null)
                    pinAnchorSpeed = ((Vector2)pinBody.GetLinearVelocityFromWorldPoint(anchorW)).Length;
                float pinMotionScale = pinAnchorSpeed <= 1.5f ? 1f
                                     : (pinAnchorSpeed >= 4f ? 0f : (4f - pinAnchorSpeed) / 2.5f);

                Vector2[] pj = arm.PrevJoints;
                for (int j = 0; j <= segs; j++) pj[j] = joints[j];

                if (_constraintScratch.Length < segs + 1) _constraintScratch = new Vector2[segs + 1];
                _constraintScratch[0] = Vector2.Zero;
                for (int j = 1; j <= segs; j++)
                {
                    Vector2 seg = joints[j] - joints[j - 1];
                    float dist = seg.Length;
                    Vector2 off = Vector2.Zero;
                    if (dist > 1e-5f)
                    {
                        Vector2 perp = new Vector2(-seg.Y / dist, seg.X / dist);
                        float amp = r.SwayAmplitude * pinMotionScale
                                    * (r.BaseWaveFraction + (1f - r.BaseWaveFraction) * ((float)j / segs));
                        off = perp * ((float)Math.Sin(time * omega + arm.Phase + j * 0.9f) * amp * 0.35f);
                    }
                    _constraintScratch[j] = off;
                    joints[j] += off;
                }

                RenderArm(t, arm, ship, r, segs);

                for (int j = 1; j <= segs; j++) joints[j] -= _constraintScratch[j];
            }
        }

        private static Color ArmColor(Ship ship)
        {
            if (!Config.TentacleBasePaintOnly)
            {
                return Color.White;
            }

            Color c = ship.Metadata.RoofBaseColor;
            return new Color(c.R, c.G, c.B, 1f);
        }

        private static void RenderArm(ZtxTentacle t, TentacleArm arm, Ship ship, ZtxTentacleRules r, int segs)
        {
            if (r.SegmentSprite == null) return;
            ShipRenderer renderer = ship.Renderer;
            if (renderer == null) return;

            Color armColor = ArmColor(ship);

            if (arm.Quads == null)
            {
                AtlasQuadManager layer = renderer.GetLayerQuads(r.Layer);
                if (layer == null) return;

                float scale = ship.WorldUniformScale;
                float inflate = ship.Rules.RenderLayers[r.Layer].Inflate;
                var clock = ship.Sim?.Clock;

                arm.Quads = new ManagedShipQuad[segs];
                for (int j = 0; j < segs; j++)
                {
                    AtlasSprite sprite = (j == segs - 1 && r.TipSprite != null)
                        ? r.TipSprite : r.SegmentSprite;
                    arm.Quads[j] = new ManagedShipQuad(
                        layer, sprite, 0, false,
                        ship.TransformPointFromWorld((arm.Joints[j] + arm.Joints[j + 1]) * 0.5f),
                        Vector2.Zero, Direction.Zero, scale, armColor, inflate, 1f, clock);
                }
            }

            for (int j = 0; j < segs; j++)
            {
                ManagedShipQuad q = arm.Quads[j];
                if (q == null) continue;

                q.Color = armColor;

                Vector2 la = ship.TransformPointFromWorld(arm.Joints[j]);
                Vector2 lb = ship.TransformPointFromWorld(arm.Joints[j + 1]);

                q.Location = (la + lb) * 0.5f;
                q.Rotation = la.DirectionTo(in lb);
            }
        }
    }
}
