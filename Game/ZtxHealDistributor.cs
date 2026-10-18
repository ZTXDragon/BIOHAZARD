using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;

namespace ZTX.BioCirculation.Game
{
    internal class ZtxHealDistributor : PartComponent
    {
        private ISimpleResourceStorage _storage;
        private BioHealManager _manager;
        private IComponentToggleProvider _toggle;

        private float _cooldown;

        public new ZtxHealDistributorRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxHealDistributorRules>(base.Rules); }
        }

        public ZtxHealDistributor(ZtxHealDistributorRules rules) : base(rules)
        {
        }

        public int Radius => Rules.Radius;

        public int Bank => _storage != null ? _storage.Resources : 0;

        public bool IsUsable =>
            _storage != null && base.Part != null && (_toggle == null || _toggle.IsToggleOn);

        public bool ReadyToServe(float seconds)
        {
            if (!IsUsable) return false;
            _cooldown -= seconds;
            if (_cooldown > 0f) return false;
            _cooldown = Rules.Interval > 0f ? Rules.Interval : 0.1f;
            return Bank > 0;
        }

        public int Available()
        {
            int have = Bank;
            int cap = Rules.UnitsPerCycle;
            return have < cap ? have : cap;
        }

        public int Withdraw(int units)
        {
            if (_storage == null || units <= 0) return 0;
            int before = _storage.Resources;
            if (units > before) units = before;
            _storage.AddResources(-units);
            return before - _storage.Resources;
        }

        public void Refund(int units)
        {
            if (_storage == null || units <= 0) return;
            _storage.AddResources(units);
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();

            base.Part.TryGetComponent<ISimpleResourceStorage>(Rules.Storage, out _storage);
            if (_storage == null)
            {
                Log.Error("HealDistributor on " + base.Part + " declares Storage=" + Rules.Storage +
                          " but no such ISimpleResourceStorage exists on the part.");
            }

            if (Rules.Toggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                {
                    Log.Error("HealDistributor on " + base.Part + " declares Toggle=" + Rules.Toggle +
                              " but no such toggle exists on the part; it will serve ungated.");
                }
            }

            _manager = BioHealManager.GetOrCreate(base.Ship);
            _manager?.RegisterDistributor(this);
        }

        public override void OnPartDetaching()
        {
            _manager?.DeregisterDistributor(this);
            _manager = null;
            _storage = null;
            _toggle = null;
            base.OnPartDetaching();
        }

        public override string GetDebuggerInlineInfo()
        {
            return "bank=" + Bank + " r=" + Radius + (_storage == null ? " [UNBOUND]" : "");
        }
    }
}
