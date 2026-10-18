using System;

namespace ZTX.BioCirculation.Core
{
    public struct TraceHash
    {
        private const uint Offset = 2166136261u;
        private const uint Prime = 16777619u;

        public uint Value { get; private set; }

        public string Hex => Value.ToString("x8");

        public static TraceHash Begin()
        {
            return new TraceHash { Value = Offset };
        }

        private TraceHash Byte(int b)
        {
            Value = (Value ^ (uint)(b & 0xff)) * Prime;
            return this;
        }

        public TraceHash Add(int v)
        {
            Byte(v); Byte(v >> 8); Byte(v >> 16); return Byte(v >> 24);
        }

        public TraceHash Add(uint v) => Add(unchecked((int)v));

        public TraceHash Add(long v)
        {
            Add(unchecked((int)v));
            return Add(unchecked((int)(v >> 32)));
        }

        public TraceHash Add(float v) => Add(BitConverter.SingleToInt32Bits(v));

        public TraceHash Add(bool v) => Byte(v ? 1 : 0);

        public TraceHash Add(string s)
        {
            if (s == null) return Add(-1);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                Byte(c); Byte(c >> 8);
            }
            return this;
        }
    }
}
