using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Serialization.Generic;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    internal class ZtxFlowNode : PartComponent, IPartComponentValue
    {
        private BloodFlowManager _manager;

        private IComponentToggleProvider _toggle;

        private EventHandler<EventArgs> _onToggleChanged;

        private bool _joined;

        private IPartComponentMode _pressureMode;
        private IPartComponentMode _flowMode;

        public new ZtxFlowNodeRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxFlowNodeRules>(base.Rules); }
        }

        private ISimpleResourceStorage _storage;

        private int _localVolume;

        public int Volume
        {
            get { return _storage != null ? _storage.Resources : _localVolume; }
            set
            {
                if (_storage == null) { _localVolume = value; return; }
                int delta = value - _storage.Resources;
                if (delta != 0) _storage.AddResources(delta);
            }
        }

        public float BuffScale
        {
            get
            {
                if (Rules.PMaxBuff == default(Cosmoteer.Data.ID<Cosmoteer.Ships.Buffs.BuffType>)) return 1f;
                var buffs = base.Part?.Buffs;
                if (buffs == null) return 0f;
                foreach (var kv in buffs)
                    if (kv.Key.BuffID == Rules.PMaxBuff) return kv.Value;
                return 0f;
            }
        }

        public int PressurePercent => _pressureMode != null ? _pressureMode.Mode : 100;

        public int FlowPercent => _flowMode != null ? _flowMode.Mode : 100;

        public int EffectivePMax => PumpStrength.PMax(Rules.PMax, BuffScale, PressurePercent);

        public int EffectiveFlowRate => PumpStrength.FlowRate(Rules.FlowRate, BuffScale, FlowPercent);

        public bool HasBackingStorage => _storage != null;

        public int EffectiveCapacity => _storage != null ? _storage.MaxResources : Rules.Capacity;

        private int _pressure;

        public int Pressure
        {
            get { return _pressure; }
            set
            {
                if (value == _pressure) return;
                _pressure = value;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        float IComponentValueProvider.Value =>
            Config.PressureSpriteFullScale > 0f ? _pressure / Config.PressureSpriteFullScale : 0f;

        public event EventHandler<EventArgs> ValueChanged;

        public int RemainingCapacity => EffectiveCapacity - Volume;

        public ZtxFlowNode(ZtxFlowNodeRules rules) : base(rules)
        {
        }

        public override bool HasSaveState => true;

        public override void WriteTo(GenericSerialWriter writer)
        {
            base.WriteTo(writer);
            writer.WriteToPath("Volume", _localVolume);
        }

        public override void ReadFrom(GenericSerialReader reader)
        {
            base.ReadFrom(reader);
            _localVolume = reader.ReadFromPath<int>("Volume");
        }

        public override string GetDebuggerInlineInfo()
        {
            return Rules.Role + (Rules.Diffusion ? " [diffusion]" : " P=" + Pressure) +
                   " vol=" + Volume + "/" + EffectiveCapacity +
                   (_storage != null ? " [live]" : " [private]") +
                   " net=" + Rules.Network + " decay=" + Rules.PressureDecay +
                   (Rules.PMax > 0 ? " PMax=" + Rules.PMax : "") +
                   (_pressureMode != null ? " pressure=" + PressurePercent + "%" : "") +
                   (_flowMode != null ? " flow=" + FlowPercent + "%" : "");
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            if (Rules.Storage != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<ISimpleResourceStorage>(Rules.Storage, out _storage);
                if (_storage == null)
                    Log.Error("FlowNode on " + base.Part + " declares Storage=" + Rules.Storage +
                              " but no such ISimpleResourceStorage exists on the part.");
            }

            if (Rules.Toggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                {
                    Log.Error("FlowNode on " + base.Part + " declares Toggle=" + Rules.Toggle +
                              " but no such toggle exists on the part; it will flow ungated.");
                }
                else
                {
                    _onToggleChanged = delegate { SyncMembership(); };
                    _toggle.ToggleChanged += _onToggleChanged;
                }
            }

            _pressureMode = ResolveMode(Rules.PressureToggle, "PressureToggle");
            _flowMode = ResolveMode(Rules.FlowToggle, "FlowToggle");

            _manager = BloodFlowManager.GetOrCreate(base.Ship);
            SyncMembership();
        }

        private IPartComponentMode ResolveMode(Cosmoteer.Data.ID<PartComponentRules> id, string field)
        {
            if (id == default(Cosmoteer.Data.ID<PartComponentRules>)) return null;
            base.Part.TryGetComponent<IPartComponentMode>(id, out IPartComponentMode mode);
            if (mode == null)
                Log.Error("FlowNode on " + base.Part + " declares " + field + "=" + id +
                          " but no such IPartComponentMode exists on the part; it runs at 100%.");
            return mode;
        }

        private void SyncMembership()
        {
            bool want = _toggle == null || _toggle.IsToggleOn;
            if (want == _joined) return;
            if (want) _manager?.Register(this);
            else _manager?.Deregister(this);
            _joined = want;
        }

        public override void OnPartDetaching()
        {
            if (_toggle != null && _onToggleChanged != null)
            {
                _toggle.ToggleChanged -= _onToggleChanged;
            }
            _onToggleChanged = null;
            _toggle = null;
            _pressureMode = null;
            _flowMode = null;
            if (_joined) _manager?.Deregister(this);
            _joined = false;
            _manager = null;
            _storage = null;
            base.OnPartDetaching();
        }
    }
}
