using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;

namespace ZTX.BioCirculation.Game
{
    // One biomass consumer's claim on its network: the network output it reads, the tank it fills,
    // and how much it may take per cycle.
    internal class ZtxBiomassIntake : PartComponent
    {
        private IResourceStorage _source;
        private IResourceStorage _tank;
        private IComponentToggleProvider _toggle;
        private ZtxBiomassShareManager _manager;

        public new ZtxBiomassIntakeRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxBiomassIntakeRules>(base.Rules); }
        }

        public ZtxBiomassIntake(ZtxBiomassIntakeRules rules) : base(rules)
        {
        }

        public bool IsReady =>
            _source != null && _tank != null && base.Part != null && (_toggle == null || _toggle.IsToggleOn);

        public object NetworkKey => (_source as BasePartNetworkResourceStorage)?.NetworkNode?.Subnetwork;

        public int Available => _source != null ? _source.ResourcesReadyToUse : 0;

        public int Want
        {
            get
            {
                if (_tank == null) return 0;
                int room = _tank.MaxResources - _tank.Resources;
                int cap = Rules.UnitsPerCycle;
                int want = room < cap ? room : cap;
                return want > 0 ? want : 0;
            }
        }

        public int Pull(int units)
        {
            if (_source == null || units <= 0) return 0;
            int before = _source.Resources;
            _source.AddResources(-units);
            int got = before - _source.Resources;
            if (got < 0) return 0;
            return got > units ? units : got;
        }

        public int Deliver(int units)
        {
            if (_tank == null || units <= 0) return 0;
            int before = _tank.Resources;
            _tank.AddResources(units);
            int gained = _tank.Resources - before;
            return gained > 0 ? gained : 0;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();

            // TryGetComponent, never GetComponent: the latter throws on a missing or wrong-typed
            // component, and a rules typo should be a log line rather than a crash at part spawn.
            base.Part.TryGetComponent<IResourceStorage>(Rules.Source, out _source);
            if (_source == null)
                Log.Error("BiomassIntake on " + base.Part + " declares Source=" + Rules.Source +
                          " but no such resource storage exists on the part.");

            base.Part.TryGetComponent<IResourceStorage>(Rules.Tank, out _tank);
            if (_tank == null)
                Log.Error("BiomassIntake on " + base.Part + " declares Tank=" + Rules.Tank +
                          " but no such resource storage exists on the part.");

            if (Rules.Toggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                    Log.Error("BiomassIntake on " + base.Part + " declares Toggle=" + Rules.Toggle +
                              " but no such toggle exists on the part; it will take ungated.");
            }

            _manager = ZtxBiomassShareManager.GetOrCreate(base.Ship);
            _manager?.Register(this);
        }

        public override void OnPartDetaching()
        {
            _manager?.Deregister(this);
            _manager = null;
            _source = null;
            _tank = null;
            _toggle = null;
            base.OnPartDetaching();
        }

        public override string GetDebuggerInlineInfo()
        {
            return "net=" + Available + " want=" + Want + (IsReady ? "" : " [not ready]") +
                   (_source == null || _tank == null ? " [UNBOUND]" : "");
        }
    }
}
