using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Serialization.Generic;

namespace ZTX.BioCirculation.Game
{
    // A part that repairs itself by spending banked healing energy.
    internal class ZtxHealNode : PartComponent
    {
        private ISimpleResourceStorage _storage;
        private BioHealManager _manager;

        private IComponentToggleProvider _toggle;

        private float _cooldown;

        private int _hpCredit;

        public new ZtxHealNodeRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxHealNodeRules>(base.Rules); }
        }

        public ZtxHealNode(ZtxHealNodeRules rules) : base(rules)
        {
        }

        public int Bank => _storage != null ? _storage.Resources : 0;

        public int BankRoom => _storage != null ? _storage.RemainingCapacity : 0;

        public int Deficit
        {
            get
            {
                Part p = base.Part;
                if (p == null) return 0;
                int d = p.Rules.MaxHealth - p.Health;
                return d < 0 ? 0 : d;
            }
        }

        public bool IsUsable => _storage != null && base.Part != null;

        public int Receive(int units)
        {
            if (_storage == null || units <= 0) return 0;
            int room = _storage.RemainingCapacity;
            if (units > room) units = room;
            if (units <= 0) return 0;

            int before = _storage.Resources;
            _storage.AddResources(units);
            int gained = _storage.Resources - before;
            return gained < 0 ? 0 : gained;
        }

        public int Spend(int units)
        {
            if (_storage == null || units <= 0) return 0;
            int have = _storage.Resources;
            if (units > have) units = have;
            if (units <= 0) return 0;

            int before = _storage.Resources;
            _storage.AddResources(-units);
            int spent = before - _storage.Resources;
            return spent < 0 ? 0 : spent;
        }

        public int TickHeal(float seconds)
        {
            if (_toggle != null && !_toggle.IsToggleOn) return 0;

            _cooldown -= seconds;
            if (_cooldown > 0f) return 0;
            _cooldown += Rules.HealInterval > 0f ? Rules.HealInterval : 1.0f;
            if (_cooldown < 0f) _cooldown = 0f;      // never bank a burst up after a long stall

            Part p = base.Part;
            if (p == null || _storage == null) return 0;

            if (p.Health <= 0) return 0;

            int deficit = Deficit;
            if (deficit <= 0) return 0;

            int wanted = Rules.MaxHpPerCycle > 0 ? Rules.MaxHpPerCycle : deficit;
            if (wanted > deficit) wanted = deficit;

            // Buy whole units only while the credit cannot cover this cycle. The leftover stays
            // as credit, so a 30 HP top-up no longer burns a whole 200 HP unit.
            int perUnit = Rules.HpPerUnit > 0 ? Rules.HpPerUnit : 1;
            while (_hpCredit < wanted)
            {
                if (Spend(1) <= 0) break;            // bank empty
                _hpCredit += perUnit;
            }

            int apply = wanted < _hpCredit ? wanted : _hpCredit;
            if (apply <= 0) return 0;

            _hpCredit -= apply;
            p.SetHealthAndSalvageDamage(p.Health + apply, p.SalvageDamage);
            return apply;
        }

        public override string GetDebuggerInlineInfo()
        {
            Part p = base.Part;
            return "bank=" + Bank + "/" + (_storage != null ? _storage.MaxResources : 0) +
                   " hp=" + (p != null ? p.Health + "/" + p.Rules.MaxHealth : "?") +
                   " credit=" + _hpCredit +
                   " cd=" + _cooldown.ToString("0.00") +
                   (_storage == null ? " [UNBOUND]" : "");
        }

        public override bool HasSaveState => true;

        public override void WriteTo(GenericSerialWriter writer)
        {
            base.WriteTo(writer);
            writer.WriteToPath("HpCredit", _hpCredit);
            writer.WriteToPath("Cooldown", _cooldown);
        }

        public override void ReadFrom(GenericSerialReader reader)
        {
            base.ReadFrom(reader);
            _hpCredit = reader.ReadFromPath<int>("HpCredit");
            _cooldown = reader.ReadFromPath<float>("Cooldown");
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();

            // TryGetComponent, never GetComponent: the latter throws on a missing or wrong-typed
            // component, and a rules typo should be a log line rather than a crash at part spawn.
            base.Part.TryGetComponent<ISimpleResourceStorage>(Rules.Storage, out _storage);
            if (_storage == null)
            {
                Log.Error("HealNode on " + base.Part + " declares Storage=" + Rules.Storage +
                          " but no such ISimpleResourceStorage exists on the part.");
            }

            if (Rules.Toggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                {
                    Log.Error("HealNode on " + base.Part + " declares Toggle=" + Rules.Toggle +
                              " but no such toggle exists on the part; it will heal ungated.");
                }
            }

            _manager = BioHealManager.GetOrCreate(base.Ship);
            _manager?.Register(this);
        }

        public override void OnPartDetaching()
        {
            _manager?.Deregister(this);
            _manager = null;
            _storage = null;
            base.OnPartDetaching();
        }
    }
}
