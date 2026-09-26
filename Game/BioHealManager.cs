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
    // Drives self-repair for one ship: every ZtxHealNode that is damaged and has energy banked
    // spends it on its own health each cycle.
    internal class BioHealManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, BioHealManager> s_managers =
            new ConditionalWeakTable<Ship, BioHealManager>();

        private readonly List<ZtxHealNode> _receivers = new List<ZtxHealNode>();
        private readonly List<ZtxHealDistributor> _distributors = new List<ZtxHealDistributor>();

        private readonly List<HealCandidate> _candidates = new List<HealCandidate>();
        private readonly List<ZtxHealNode> _targets = new List<ZtxHealNode>();
        private int[] _amounts = new int[0];
        private int _movedThisWindow;

        private ZtxHealNode[] _snapshot = new ZtxHealNode[0];
        private int _snapshotCount;

        private bool _orderDirty = true;
        private int _tickCounter;
        private int _hpThisWindow;
        private int _unitsThisWindow;

        public BioHealManager()
        {
            base.FixedUpdateBucket = FixedUpdateBuckets.Oscillators;
            base.FixedUpdatingEnabled = true;
        }

        public static BioHealManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out BioHealManager manager)) return manager;

            manager = new BioHealManager();
            BioHealManager captured = manager;
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

        public void Register(ZtxHealNode node)
        {
            if (node == null) return;
            _receivers.Add(node);
            _orderDirty = true;
        }

        public void Deregister(ZtxHealNode node)
        {
            if (node == null) return;
            if (_receivers.Remove(node)) _orderDirty = true;
        }

        public void RegisterDistributor(ZtxHealDistributor d)
        {
            if (d == null) return;
            _distributors.Add(d);
            _orderDirty = true;
        }

        public void DeregisterDistributor(ZtxHealDistributor d)
        {
            if (d == null) return;
            if (_distributors.Remove(d)) _orderDirty = true;
        }

        private void Distribute(float seconds)
        {
            for (int d = 0; d < _distributors.Count; d++)
            {
                ZtxHealDistributor dist = _distributors[d];
                if (dist == null) continue;
                Part dp = dist.Part;
                if (dp == null || !ReferenceEquals(dp.Ship, base.Ship)) continue;
                if (!dist.ReadyToServe(seconds)) continue;

                int available = dist.Available();
                if (available <= 0) continue;

                _candidates.Clear();
                _targets.Clear();
                // Every ready distributor walks EVERY heal node on the ship, in range or not.
                ZtxPerfProbe.Tally("heal distributor scans", 1);
                ZtxPerfProbe.Tally("heal nodes checked by scans", _snapshotCount);
                for (int i = 0; i < _snapshotCount; i++)
                {
                    ZtxHealNode node = _snapshot[i];
                    if (node == null || !node.IsUsable) continue;
                    Part np = node.Part;
                    if (np == null || !ReferenceEquals(np.Ship, base.Ship)) continue;

                    int room = node.BankRoom;
                    if (room <= 0) continue;
                    int missing = node.Deficit;
                    if (missing <= 0) continue;
                    int hpPerUnit = node.Rules.HpPerUnit;
                    int wanted = hpPerUnit > 0 ? (missing + hpPerUnit - 1) / hpPerUnit : room;
                    if (wanted > room) wanted = room;
                    if (wanted <= 0) continue;

                    int maxHp = np.Rules.MaxHealth;
                    int healthPerMille = maxHp > 0 ? (int)(np.Health * 1000L / maxHp) : 0;
                    int urgency = healthPerMille * 16 + node.Bank;

                    _candidates.Add(new HealCandidate(np.Location.X, np.Location.Y, wanted,
                                                      urgency));
                    _targets.Add(node);
                }
                if (_candidates.Count == 0) continue;

                if (_amounts.Length < _candidates.Count)
                    _amounts = new int[_candidates.Count * 2];

                int allocated = HealSpread.Allocate(dp.Location.X, dp.Location.Y, dist.Radius,
                                                    available, _candidates, _amounts);
                if (allocated <= 0) continue;

                // Withdraw first, deliver only what actually left the bank. The other order
                // would mint energy whenever the storage returned less than was asked of it.
                int drawn = dist.Withdraw(allocated);
                int delivered = 0;
                for (int i = 0; i < _targets.Count && delivered < drawn; i++)
                {
                    int give = _amounts[i];
                    if (give <= 0) continue;
                    if (delivered + give > drawn) give = drawn - delivered;
                    delivered += _targets[i].Receive(give);
                }

                // Anything a receiver refused goes back, so a bank that filled between the scan
                // and the hand-off cannot quietly destroy units.
                if (delivered < drawn) dist.Refund(drawn - delivered);
                _movedThisWindow += delivered;
            }
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            if (!Config.Enabled || _receivers.Count == 0) return;

            if (_orderDirty)
            {
                // Distributors are ordered the same way and for the same reason as the
                // receivers below: who serves first must not depend on part attach order.
                _distributors.Sort(CompareDistributorsByLocation);
                _receivers.Sort(CompareByLocation);
                _orderDirty = false;
            }

            if (_snapshot.Length < _receivers.Count) _snapshot = new ZtxHealNode[_receivers.Count * 2];
            _snapshotCount = _receivers.Count;
            _receivers.CopyTo(0, _snapshot, 0, _snapshotCount);

            float seconds = (float)fixedUpdater.Interval.Seconds;

            long distributeStart = ZtxPerfProbe.Mark();
            Distribute(seconds);
            ZtxPerfProbe.Section("heal distribute", distributeStart);

            long healStart = ZtxPerfProbe.Mark();
            ZtxPerfProbe.Tally("heal nodes ticked", _snapshotCount);
            for (int i = 0; i < _snapshotCount; i++)
            {
                ZtxHealNode node = _snapshot[i];
                if (node == null || !node.IsUsable) continue;

                Part p = node.Part;
                // A ship split reuses the SAME Part instances across both halves, so an
                // ExistsInSim check alone would pass for a part that now belongs to the other ship.
                if (p == null || !ReferenceEquals(p.Ship, base.Ship)) continue;

                int before = node.Bank;
                int hp = node.TickHeal(seconds);
                if (hp > 0)
                {
                    _hpThisWindow += hp;
                    _unitsThisWindow += before - node.Bank;
                }
            }
            ZtxPerfProbe.Section("heal tick", healStart);

            if (++_tickCounter >= 300)
            {
                _tickCounter = 0;
                if (_hpThisWindow > 0)
                {
                    Log.Info("heal: " + _hpThisWindow + " HP restored using " + _unitsThisWindow +
                             " energy across " + _snapshotCount + " node(s); " +
                             _movedThisWindow + " units distributed by " +
                             _distributors.Count + " distributor(s)");
                }
                _hpThisWindow = 0;
                _unitsThisWindow = 0;
                _movedThisWindow = 0;
            }
        }

        private static int CompareDistributorsByLocation(ZtxHealDistributor a, ZtxHealDistributor b)
        {
            Part pa = a?.Part, pb = b?.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }

        private static int CompareByLocation(ZtxHealNode a, ZtxHealNode b)
        {
            Part pa = a?.Part, pb = b?.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }
    }
}
