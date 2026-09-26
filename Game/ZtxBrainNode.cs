using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;

namespace ZTX.BioCirculation.Game
{
    // The live component on one modular brain block.
    internal class ZtxBrainNode : PartComponent
    {
        private ZtxBrainManager _manager;

        public new ZtxBrainNodeRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxBrainNodeRules>(base.Rules); }
        }

        public ZtxBrainNode(ZtxBrainNodeRules rules) : base(rules)
        {
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            _manager = ZtxBrainManager.GetOrCreate(base.Ship);
            _manager?.Register(this);
        }

        public override void OnPartDetaching()
        {
            _manager?.Deregister(this);
            _manager = null;
            base.OnPartDetaching();
        }
    }
}
