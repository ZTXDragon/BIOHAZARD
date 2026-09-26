using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // ON while the part sits at the authored number of clockwise quarter turns.
    internal class ZtxPartRotationToggle : PartComponent, IPartComponentToggle, IComponentToggleProvider
    {
        private bool _on;

        public new ZtxPartRotationToggleRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxPartRotationToggleRules>(base.Rules); }
        }

        public bool IsToggleOn => _on;

        public bool IsToggleOnAnticipated => _on;

        public event EventHandler<EventArgs> ToggleChanged;

        public ZtxPartRotationToggle(ZtxPartRotationToggleRules rules) : base(rules)
        {
        }

        private bool Evaluate()
        {
            var part = base.Part;
            if (part == null) return false;
            int bits = part.Rotation | (part.FlipX ? 4 : 0);
            return PartFacing.Matches(bits, Rules.Rotation);
        }

        public override void OnAddedToPart(Part part)
        {
            base.OnAddedToPart(part);
            _on = Evaluate();
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            _on = Evaluate();
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }

        public override string GetDebuggerInlineInfo()
        {
            var part = base.Part;
            return (_on ? "ON" : "off") + " wants=" + PartFacing.Normalize(Rules.Rotation) +
                   (part != null ? " part.Rotation=" + part.Rotation + (part.FlipX ? " flipX" : "") : "");
        }
    }
}
