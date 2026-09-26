using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;

namespace ZTX.BioCirculation.Game
{
    // The live system-fullness toggle.
    internal class ZtxFlowGate : PartComponent, IPartComponentToggle, IComponentToggleProvider
    {
        private BloodFlowManager _manager;
        private IPartComponentMode _modeToggle;
        private bool _on = true;

        public new ZtxFlowGateRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxFlowGateRules>(base.Rules); }
        }

        public float EffectiveStopAbove
        {
            get
            {
                if (_modeToggle == null) return Rules.StopAbove;
                float stop = _modeToggle.Mode / 100f;
                if (stop < 0.05f) stop = 0.05f;
                if (stop > 1f) stop = 1f;
                return stop;
            }
        }

        public float EffectiveResumeBelow
        {
            get
            {
                if (_modeToggle == null) return Rules.ResumeBelow;
                float stop = EffectiveStopAbove;
                float a = stop - 0.15f;
                float b = stop * 0.5f;
                return a > b ? a : b;
            }
        }

        public bool IsToggleOn => _on;

        public bool IsToggleOnAnticipated => _on;

        public event EventHandler<EventArgs> ToggleChanged;

        public float LastFullness;
        public long LastVolume;
        public long LastCapacity;

        public ZtxFlowGate(ZtxFlowGateRules rules) : base(rules)
        {
        }

        public void SetOn(bool on)
        {
            if (on == _on) return;
            _on = on;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }

        public override string GetDebuggerInlineInfo()
        {
            return (_on ? "PRODUCING" : "PAUSED (system full)") +
                   " fullness=" + (LastFullness * 100f).ToString("0") + "%" +
                   " (" + LastVolume + "/" + LastCapacity + "u)" +
                   " stop>=" + EffectiveStopAbove.ToString("0.00") +
                   " resume<=" + EffectiveResumeBelow.ToString("0.00") +
                   (_modeToggle != null ? " [player mode " + _modeToggle.Mode + "%]" : "") +
                   (Rules.Network.Length > 0 ? " net=" + Rules.Network : " net=all");
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            if (Rules.ModeToggle != default(Cosmoteer.Data.ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IPartComponentMode>(Rules.ModeToggle, out _modeToggle);
                if (_modeToggle == null)
                    Log.Error("ZtxFlowGate on " + base.Part + " declares ModeToggle=" +
                              Rules.ModeToggle + " but no such IPartComponentMode exists.");
            }
            _manager = BloodFlowManager.GetOrCreate(base.Ship);
            _manager?.RegisterGate(this);
        }

        public override void OnPartDetaching()
        {
            _manager?.DeregisterGate(this);
            _manager = null;
            _modeToggle = null;
            base.OnPartDetaching();
        }
    }
}
