using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.BioCirculation.Game
{
    // Rules for ZtxDigester — a stomach that accepts ANY resource in ANY cell of a single
    // FlexResourceGrid and converts each one by its own recipe.
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxDigester")]
    internal class ZtxDigesterRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public ID<PartComponentRules> Grid;

        [Serialize]
        [PortableHash]
        public ID<PartComponentRules> Output;

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<ResourceRules> OutputResource;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool EjectUndigestible = true;

        [Serialize(Optional = true, ReadContentOnly = true)]
        [PortableHash]
        public Dictionary<ID<ResourceRules>, Recipe> Recipes = new Dictionary<ID<ResourceRules>, Recipe>();

        // One resource's conversion terms.
        [ReflectiveSerialization(Explicit = true)]
        [ReflectivePortableHashing(Explicit = true)]
        public class Recipe
        {
            [Serialize(Optional = true)]
            [PortableHash]
            public ModifiableInt In = 1f;

            [Serialize(Optional = true)]
            [PortableHash]
            public ModifiableInt Out = 2f;

            [Serialize(Optional = true)]
            [PortableHash]
            public ModifiableTime Interval;
        }

        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules> Toggle;

        public override PartComponent CreateComponent()
        {
            return new ZtxDigester(this);
        }
    }
}
