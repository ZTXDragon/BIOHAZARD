using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Serialization.Generic;

namespace ZTX.BioCirculation.Game
{
    // The live blood-movement component on a part.
    internal class ZtxFlowNode : PartComponent, IPartComponentValue
    {
        private BloodFlowManager _manager;

        private IComponentToggleProvider _toggle;

        private EventHandler<EventArgs> _onToggleChanged;

        private bool _joined;

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
                return 0f;   // buff not present at all = no heart reaching this part
            }
        }

        public int EffectivePMax
        {
            get
            {
                if (Rules.PMax <= 0) return 0;
                return (int)System.Math.Round(Rules.PMax * BuffScale);
            }
        }

        public int EffectiveFlowRate
        {
            get
            {
                float scale = BuffScale;
                if (scale <= 0f) return 0;
                int r = (int)System.Math.Round(Rules.FlowRate * scale);
                return r < 1 ? 1 : r;
            }
        }

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
                   (Rules.PMax > 0 ? " PMax=" + Rules.PMax : "");
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            // Bind the backing storage before registering, so the first graph build already
            // reads real blood levels rather than zeroes.
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

            _manager = BloodFlowManager.GetOrCreate(base.Ship);
            SyncMembership();
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
                _toggle.ToggleChanged -= _onToggleChanged;   // the part outlives the event otherwise
            }
            _onToggleChanged = null;
            _toggle = null;
            if (_joined) _manager?.Deregister(this);
            _joined = false;
            _manager = null;
            _storage = null;
            base.OnPartDetaching();
        }
    }
}
