using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Scene2D;
using Halfling.Timing;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Splits each biomass network evenly between the parts that burn it (2026-09-25).
    internal class ZtxBiomassShareManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, ZtxBiomassShareManager> s_managers =
            new ConditionalWeakTable<Ship, ZtxBiomassShareManager>();

        private readonly List<ZtxBiomassIntake> _intakes = new List<ZtxBiomassIntake>();
        private bool _orderDirty = true;

        private ZtxBiomassIntake[] _snapshot = new ZtxBiomassIntake[0];

        private readonly List<object> _networkOrder = new List<object>();
        private readonly Dictionary<object, List<ZtxBiomassIntake>> _byNetwork =
            new Dictionary<object, List<ZtxBiomassIntake>>();
        private int[] _wants = new int[0];
        private int[] _shares = new int[0];

        // Diagnostics over a ~10 s window: what each consumer got, and how often the network
        // could not cover every request. That is the line to read to see the split working.
        private int _windowTicks;
        private int _windowCycles;
        private int _shortCycles;
        private readonly Dictionary<ZtxBiomassIntake, int> _delivered = new Dictionary<ZtxBiomassIntake, int>();
        private bool _reportedShortPull;
        private bool _reportedRefusal;

        public ZtxBiomassShareManager()
        {
            // A DECLARED constant - FixedUpdateBuckets.GetBucketName throws on any undeclared
            // bucket int, and it is reachable from the multiplayer out-of-sync handler.
            base.FixedUpdateBucket = FixedUpdateBuckets.Timers;
            base.FixedUpdatingEnabled = true;
        }

        public static ZtxBiomassShareManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out ZtxBiomassShareManager manager)) return manager;

            manager = new ZtxBiomassShareManager();
            ZtxBiomassShareManager captured = manager;
            Ship capturedShip = ship;

            // Same attach rule as every manager here: a ship's Sim is null while its parts attach
            // from a save, and deferring in that window silently drops the add.
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

        public void Register(ZtxBiomassIntake intake)
        {
            if (intake == null) return;
            _intakes.Add(intake);
            _orderDirty = true;
        }

        public void Deregister(ZtxBiomassIntake intake)
        {
            if (intake == null) return;
            if (_intakes.Remove(intake))
            {
                _orderDirty = true;
                _delivered.Remove(intake);
            }
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            if (!Config.Enabled || _intakes.Count == 0) return;
            var sim = base.Sim;
            if (sim == null) return;

            int ticksPerSecond = (int)Math.Round(GameApp.Rules.Simulation.PhysicsUpdatesPerSecond);
            int cycleTicks = (int)Math.Round(Config.BiomassShareSeconds * ticksPerSecond);
            if (cycleTicks < 1) cycleTicks = 1;

            int tick = sim.Tick;
            if (tick % cycleTicks == 0)
            {
                long start = ZtxPerfProbe.Mark();
                ShareCycle(tick / cycleTicks);
                ZtxPerfProbe.Section("biomass share", start);
            }

            if (++_windowTicks >= 300)
            {
                LogWindow();
                _windowTicks = 0;
            }
        }

        private void ShareCycle(int cycle)
        {
            if (_orderDirty)
            {
                // Grid location, the engine's own deterministic part key; registration order is
                // attach order and shifts whenever a part is destroyed and rebuilt.
                _intakes.Sort(CompareByLocation);
                _orderDirty = false;
            }

            int count = _intakes.Count;
            if (_snapshot.Length < count) _snapshot = new ZtxBiomassIntake[count * 2];
            _intakes.CopyTo(0, _snapshot, 0, count);

            // Group ready intakes by network, keeping first-seen (location) order so the groups
            // are always served in the same sequence.
            _networkOrder.Clear();
            foreach (List<ZtxBiomassIntake> list in _byNetwork.Values) list.Clear();
            for (int i = 0; i < count; i++)
            {
                ZtxBiomassIntake intake = _snapshot[i];
                if (intake == null || !intake.IsReady) continue;
                Part p = intake.Part;
                if (p == null || !ReferenceEquals(p.Ship, base.Ship)) continue;
                object key = intake.NetworkKey;
                if (key == null) continue;
                if (!_byNetwork.TryGetValue(key, out List<ZtxBiomassIntake> group))
                    _byNetwork[key] = group = new List<ZtxBiomassIntake>();
                if (group.Count == 0) _networkOrder.Add(key);
                group.Add(intake);
            }

            _windowCycles++;
            bool shortThisCycle = false;
            for (int g = 0; g < _networkOrder.Count; g++)
            {
                if (ServeNetwork(_byNetwork[_networkOrder[g]], cycle)) shortThisCycle = true;
            }
            if (shortThisCycle) _shortCycles++;

            // Networks that vanished (a split, a severed pipe) must not keep their lists forever.
            if (_byNetwork.Count > _networkOrder.Count * 2 + 8)
            {
                var stale = new List<object>();
                foreach (KeyValuePair<object, List<ZtxBiomassIntake>> kv in _byNetwork)
                    if (kv.Value.Count == 0) stale.Add(kv.Key);
                foreach (object k in stale) _byNetwork.Remove(k);
            }
        }

        private bool ServeNetwork(List<ZtxBiomassIntake> group, int cycle)
        {
            int n = group.Count;
            if (n == 0) return false;
            if (_wants.Length < n)
            {
                _wants = new int[n * 2];
                _shares = new int[n * 2];
            }

            int wanted = 0;
            for (int i = 0; i < n; i++)
            {
                _wants[i] = group[i].Want;
                wanted += _wants[i];
            }
            if (wanted <= 0) return false;

            // Every intake in the group reads the same network, so any one of them can report
            // what it holds and pull from it.
            int available = group[0].Available;
            int planned = FairShare.Allocate(available, new ArraySegment<int>(_wants, 0, n),
                                             cycle & 0x7FFFFFFF, _shares);
            if (planned <= 0) return available < wanted;

            int got = group[0].Pull(planned);
            if (got < planned && !_reportedShortPull)
            {
                _reportedShortPull = true;
                Log.Info("biomass share: the network gave " + got + " of " + planned +
                         " planned units; delivering only what arrived (reported once)");
            }

            int left = got;
            for (int i = 0; i < n && left > 0; i++)
            {
                int give = _shares[i] < left ? _shares[i] : left;
                if (give <= 0) continue;
                int accepted = group[i].Deliver(give);
                left -= accepted;
                Record(group[i], accepted);
            }

            for (int i = 0; i < n && left > 0; i++)
            {
                int room = group[i].Want;
                if (room <= 0) continue;
                int accepted = group[i].Deliver(room < left ? room : left);
                left -= accepted;
                Record(group[i], accepted);
            }
            if (left > 0 && !_reportedRefusal)
            {
                _reportedRefusal = true;
                Log.Error("biomass share: " + left + " unit(s) pulled from the network found no tank " +
                          "with room and were lost (reported once) - a Want/Deliver mismatch");
            }

            return available < wanted;
        }

        private void Record(ZtxBiomassIntake intake, int units)
        {
            if (units <= 0) return;
            _delivered.TryGetValue(intake, out int v);
            _delivered[intake] = v + units;
        }

        private void LogWindow()
        {
            if (_delivered.Count > 0)
            {
                var sb = new StringBuilder("biomass share on ship ");
                sb.Append(base.Ship != null ? base.Ship.UniqueID.ToString() : "?")
                  .Append(": short on ").Append(_shortCycles).Append('/').Append(_windowCycles)
                  .Append(" cycles; delivered");
                for (int i = 0; i < _intakes.Count; i++)
                {
                    ZtxBiomassIntake intake = _intakes[i];
                    Part p = intake?.Part;
                    if (p == null) continue;
                    _delivered.TryGetValue(intake, out int units);
                    sb.Append(' ').Append(p.Rules.ID.ToString().Replace("ztx.biohazard.", ""))
                      .Append(p.Location).Append('=').Append(units);
                }
                Log.Info(sb.ToString());
            }
            _delivered.Clear();
            _windowCycles = 0;
            _shortCycles = 0;
        }

        private static int CompareByLocation(ZtxBiomassIntake a, ZtxBiomassIntake b)
        {
            Part pa = a?.Part, pb = b?.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }
    }
}
