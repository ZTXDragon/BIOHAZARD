using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Scene2D;
using Halfling.Timing;

using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    internal class ZtxDigestManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, ZtxDigestManager> s_managers =
            new ConditionalWeakTable<Ship, ZtxDigestManager>();

        internal static ZtxDigestManager Find(Ship ship)
        {
            return ship != null && s_managers.TryGetValue(ship, out ZtxDigestManager m) ? m : null;
        }

        internal void TraceState(ref TraceHash h)
        {
            h.Add(_digesters.Count).Add(_ejectCounter);
            for (int i = 0; i < _digesters.Count; i++)
            {
                ZtxDigester d = _digesters[i];
                if (d?.Part == null) { h.Add(-1); continue; }
                h.Add(d.Part.Location.X).Add(d.Part.Location.Y).Add(d.OutputFill);
            }
        }

        private readonly List<ZtxDigester> _digesters = new List<ZtxDigester>();
        private ZtxDigester[] _snapshot = new ZtxDigester[0];
        private int _snapshotCount;
        private bool _orderDirty = true;
        private int _tickCounter;
        private int _ejectCounter;
        private int _producedThisWindow;

        public ZtxDigestManager()
        {
            base.FixedUpdateBucket = FixedUpdateBuckets.Timers;
            base.FixedUpdatingEnabled = true;
        }

        public static ZtxDigestManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out ZtxDigestManager manager)) return manager;

            manager = new ZtxDigestManager();
            ZtxDigestManager captured = manager;
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

        public void Register(ZtxDigester d)
        {
            if (d == null) return;
            _digesters.Add(d);
            _orderDirty = true;
        }

        public void Deregister(ZtxDigester d)
        {
            if (d == null) return;
            if (_digesters.Remove(d)) _orderDirty = true;
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            if (!Config.Enabled || _digesters.Count == 0) return;

            if (_orderDirty)
            {
                _digesters.Sort(CompareByLocation);
                _orderDirty = false;
            }

            if (_snapshot.Length < _digesters.Count) _snapshot = new ZtxDigester[_digesters.Count * 2];
            _snapshotCount = _digesters.Count;
            _digesters.CopyTo(0, _snapshot, 0, _snapshotCount);

            float seconds = (float)fixedUpdater.Interval.Seconds;
            bool sweepEjects = ++_ejectCounter >= 60;
            if (sweepEjects) _ejectCounter = 0;

            for (int i = 0; i < _snapshotCount; i++)
            {
                ZtxDigester d = _snapshot[i];
                if (d?.Part == null) continue;
                if (!ReferenceEquals(d.Part.Ship, base.Ship)) continue;

                _producedThisWindow += d.Tick(seconds);
                if (sweepEjects) d.EjectUndigestible();
            }

            if (++_tickCounter >= 300)
            {
                _tickCounter = 0;
                if (_producedThisWindow > 0)
                {
                    var fill = new System.Text.StringBuilder();
                    for (int i = 0; i < _snapshotCount; i++)
                    {
                        ZtxDigester d = _snapshot[i];
                        if (d != null) fill.Append(' ').Append(d.OutputFill);
                    }
                    Log.Info("digest: " + _producedThisWindow + " unit(s) produced across " +
                             _snapshotCount + " digester(s), output" + fill);
                }
                _producedThisWindow = 0;
            }
        }

        private static int CompareByLocation(ZtxDigester a, ZtxDigester b)
        {
            Part pa = a?.Part, pb = b?.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }
    }
}
