using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public sealed class BloodGraph
    {
        public readonly List<BloodNode> Nodes = new();
        // Directed adjacency: Downstream[a] = nodes blood may flow INTO from a.
        public readonly Dictionary<int, List<int>> Downstream = new();
        // Sinks: low-pressure anchors (the network's drain side).
        public readonly HashSet<int> Sinks = new();
        // Active valve pumps: each is (inputNeighbourId -> outputNeighbourId). A valve moves
        // blood across itself against the gradient (input side low, output side high P_max).
        public readonly System.Collections.Generic.List<(int from, int to)> Pumps = new();
        // The ids that are the SOURCE of a pump. FlowStep asks "does this node pump onward?"
        // once per pump edge; scanning the pump list for that answer was O(pumps) per ask.
        public readonly HashSet<int> PumpSources = new();

        private readonly Dictionary<int, BloodNode> _byId = new();

        private bool _sorted = true;

        public readonly Dictionary<int, int> DeltaScratch = new();

        private bool _diffusionKnown;
        private bool _hasDiffusion;

        public bool HasDiffusionNodes
        {
            get
            {
                if (_diffusionKnown) return _hasDiffusion;
                _hasDiffusion = false;
                foreach (var n in Nodes)
                {
                    if (n.Diffusion) { _hasDiffusion = true; break; }
                }
                _diffusionKnown = true;
                return _hasDiffusion;
            }
        }

        public BloodNode Add(BloodNode n)
        {
            if (Nodes.Count > 0 && Nodes[Nodes.Count - 1].Id > n.Id) _sorted = false;
            Nodes.Add(n);
            _byId[n.Id] = n;
            _diffusionKnown = false;
            if (!Downstream.ContainsKey(n.Id)) Downstream[n.Id] = new List<int>();
            return n;
        }

        public void AddPump(int from, int to)
        {
            Pumps.Add((from, to));
            PumpSources.Add(from);
        }

        public void Connect(int from, int to) // directed: blood may flow from->to
        {
            if (!Downstream.ContainsKey(from)) Downstream[from] = new List<int>();
            Downstream[from].Add(to);
        }
        public void ConnectBoth(int a, int b) { Connect(a, b); Connect(b, a); }

        public BloodNode NodeById(int id) => _byId.TryGetValue(id, out var n) ? n : null;

        public void EnsureSorted()
        {
            if (_sorted) return;
            Nodes.Sort((x, y) => x.Id.CompareTo(y.Id));
            _sorted = true;
        }
    }
}
