using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Serialization.Generic;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // A stomach that digests whatever is in its maw, each resource by its own recipe.
    internal class ZtxDigester : PartComponent
    {
        private FlexResourceGrid _grid;
        private ISimpleResourceStorage _output;

        // Scratch buffers for the plan-then-commit take. Reused every tick so a digester does
        // not allocate three lists per recipe per tick; PlanTake clears what it fills.
        private readonly List<FlexResourceGrid.ResourceTile> _planTiles =
            new List<FlexResourceGrid.ResourceTile>();
        private readonly List<int> _planQty = new List<int>();
        private readonly List<int> _planTakes = new List<int>();

        private readonly HashSet<ID<ResourceRules>> _blockedLogged = new HashSet<ID<ResourceRules>>();
        private ZtxDigestManager _manager;

        private IComponentToggleProvider _toggle;

        private ID<ResourceRules>[] _keys = Array.Empty<ID<ResourceRules>>();
        private ZtxDigesterRules.Recipe[] _recipes = Array.Empty<ZtxDigesterRules.Recipe>();
        private float[] _cooldowns = Array.Empty<float>();

        public new ZtxDigesterRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxDigesterRules>(base.Rules); }
        }

        public ZtxDigester(ZtxDigesterRules rules) : base(rules)
        {
        }

        public bool HasRecipeFor(ID<ResourceRules> resource)
        {
            return Rules.Recipes != null && Rules.Recipes.ContainsKey(resource);
        }

        public FlexResourceGrid Grid => _grid;

        public int Tick(float seconds)
        {
            if (_grid == null || _output == null) return 0;
            if (_toggle != null && !_toggle.IsToggleOn) return 0;   // brain gate, v0.69.0

            int produced = 0;
            for (int i = 0; i < _keys.Length; i++)
            {
                _cooldowns[i] -= seconds;
                if (_cooldowns[i] > 0f) continue;

                ZtxDigesterRules.Recipe r = _recipes[i];
                // GetValue(part, scale) applies any buffs on this part, so a future
                // digestive-enzyme buff scales recipes with no code change.
                float interval = (float)r.Interval.GetValue(base.Part, 1f).Seconds;
                _cooldowns[i] += interval > 0f ? interval : 1f;
                if (_cooldowns[i] < 0f) _cooldowns[i] = 0f;   // never bank a burst after a stall

                produced += ConvertOne(_keys[i], r);
            }
            return produced;
        }

        private int ConvertOne(ID<ResourceRules> resource, ZtxDigesterRules.Recipe r)
        {
            int need = r.In.GetValue(base.Part, 1f);
            int make = r.Out.GetValue(base.Part, 1f);
            if (need <= 0 || make <= 0) return 0;

            if (_output.RemainingCapacity < make)
            {
                // Verbose (2026-09-26): when the output fills, every waiting resource logged at
                // once - ~20 flushed lines, a 185 ms stall in the digest tick.
                if (_blockedLogged.Add(resource))
                    Log.Verbose("digester: " + resource + " needs room for " + make +
                                " biomass but output is " + OutputFill + " - waiting");
                return 0;
            }
            _blockedLogged.Remove(resource);

            _planTiles.Clear();
            _planQty.Clear();
            foreach (FlexResourceGrid.ResourceTile tile in _grid)
            {
                if (tile.Resources <= 0) continue;
                if (tile.ResourceType != resource) continue;
                _planTiles.Add(tile);
                _planQty.Add(tile.Resources);
            }

            if (!DigestPlan.PlanTake(_planQty, need, _planTakes)) return 0;

            for (int i = 0; i < _planTiles.Count; i++)
            {
                int take = _planTakes[i];
                if (take <= 0) continue;
                // SetResources is the only sanctioned mutator. Writing tile.Resources directly
                // (Publicizer exposes it) would bypass the grid's total-quantity bookkeeping.
                FlexResourceGrid.ResourceTile t = _planTiles[i];
                t.SetResources(t.Resources - take, resource);
            }

            int before = _output.Resources;
            _output.AddResources(make);
            return _output.Resources - before;
        }

        public void EjectUndigestible()
        {
            if (_grid == null || !Rules.EjectUndigestible) return;
            Part p = base.Part;
            if (p?.Ship?.Sim == null) return;

            foreach (FlexResourceGrid.ResourceTile tile in _grid)
            {
                if (tile.Resources <= 0) continue;
                ID<ResourceRules>? type = tile.ResourceType;
                if (!type.HasValue || HasRecipeFor(type.Value)) continue;

                int qty = tile.Resources;
                tile.ClearResources();
                p.Ship.Sim.Nuggets.DropNuggets(type.Value, qty, p.Ship.DetTransformShapeToWorld(tile.Rect));
                Log.Info("digester ejected " + qty + " " + type.Value + " (no recipe)");
            }
        }

        public string OutputFill => _output == null ? "?" : _output.Resources + "/" + _output.MaxResources;

        public override string GetDebuggerInlineInfo()
        {
            if (_grid == null) return "[UNBOUND grid]";
            var sb = new System.Text.StringBuilder();
            sb.Append(_keys.Length).Append(" recipe(s), out=")
              .Append(_output != null ? _output.Resources + "/" + _output.MaxResources : "?");
            foreach (FlexResourceGrid.ResourceTile tile in _grid)
                if (tile.Resources > 0) sb.Append(' ').Append(tile.ResourceType).Append('x').Append(tile.Resources);
            return sb.ToString();
        }

        public override bool HasSaveState => true;

        public override void WriteTo(GenericSerialWriter writer)
        {
            base.WriteTo(writer);
            for (int i = 0; i < _keys.Length; i++)
                writer.WriteToPath("Cooldowns/" + _keys[i], _cooldowns[i]);
        }

        public override void ReadFrom(GenericSerialReader reader)
        {
            base.ReadFrom(reader);
            BuildRecipeArrays();
            for (int i = 0; i < _keys.Length; i++)
                _cooldowns[i] = reader.ReadOptionalFromPath("Cooldowns/" + _keys[i], 0f);
        }

        private void BuildRecipeArrays()
        {
            var keys = new List<ID<ResourceRules>>();
            if (Rules.Recipes != null)
                foreach (KeyValuePair<ID<ResourceRules>, ZtxDigesterRules.Recipe> kv in Rules.Recipes)
                    keys.Add(kv.Key);
            keys.Sort((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));

            _keys = keys.ToArray();
            _recipes = new ZtxDigesterRules.Recipe[_keys.Length];
            if (_cooldowns.Length != _keys.Length) _cooldowns = new float[_keys.Length];
            for (int i = 0; i < _keys.Length; i++) _recipes[i] = Rules.Recipes[_keys[i]];
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();

            base.Part.TryGetComponent<FlexResourceGrid>(Rules.Grid, out _grid);
            base.Part.TryGetComponent<ISimpleResourceStorage>(Rules.Output, out _output);
            if (_grid == null)
                Log.Error("ZtxDigester on " + base.Part + ": Grid=" + Rules.Grid + " is not a FlexResourceGrid.");
            if (_output == null)
                Log.Error("ZtxDigester on " + base.Part + ": Output=" + Rules.Output + " is not an ISimpleResourceStorage.");

            if (_keys.Length == 0) BuildRecipeArrays();
            if (_keys.Length == 0)
                Log.Error("ZtxDigester on " + base.Part + " has NO recipes — it will refuse everything.");

            if (Rules.Toggle != default(ID<PartComponentRules>))
            {
                base.Part.TryGetComponent<IComponentToggleProvider>(Rules.Toggle, out _toggle);
                if (_toggle == null)
                {
                    Log.Error("ZtxDigester on " + base.Part + ": Toggle=" + Rules.Toggle +
                              " is not a toggle on this part; it will digest ungated.");
                }
            }

            ZtxDigesterSinkPatch.Register(this);
            _manager = ZtxDigestManager.GetOrCreate(base.Ship);
            _manager?.Register(this);
        }

        public override void OnPartDetaching()
        {
            _manager?.Deregister(this);
            _manager = null;
            ZtxDigesterSinkPatch.Deregister(this);
            _grid = null;
            _output = null;
            base.OnPartDetaching();
        }
    }
}
