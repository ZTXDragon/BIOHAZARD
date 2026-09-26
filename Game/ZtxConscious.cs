using System;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;

namespace ZTX.BioCirculation.Game
{
    // ON while a brain's consciousness radius reaches this part.
    internal class ZtxConscious : PartComponent, IPartComponentToggle, IComponentToggleProvider
    {
        private ZtxBrainManager _manager;
        private bool _on;

        public bool IsToggleOn => _on;

        public bool IsToggleOnAnticipated => _on;

        public event EventHandler<EventArgs> ToggleChanged;

        public ZtxConscious(ZtxConsciousRules rules) : base(rules)
        {
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            _manager = ZtxBrainManager.GetOrCreate(base.Ship);
            _manager?.RegisterGate(this);
        }

        public override void OnPartDetaching()
        {
            _manager?.DeregisterGate(this);
            _manager = null;
            base.OnPartDetaching();
        }

        internal void SetConscious(bool conscious)
        {
            if (conscious == _on) return;
            _on = conscious;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }

        public override string GetDebuggerInlineInfo()
        {
            return _on ? "awake" : "UNCONSCIOUS (no brain in range)";
        }
    }
}
