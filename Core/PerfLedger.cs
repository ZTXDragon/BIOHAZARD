using System;
using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    // Elapsed time per key, for the performance probe (2026-09-25).
    public sealed class PerfLedger<TKey>
    {
        // Everything recorded against one key since the last reset.
        public sealed class Entry
        {
            public TKey Key;
            public long Total;
            public long Max;
            public long Calls;
        }

        private readonly Dictionary<TKey, Entry> _entries;

        public PerfLedger(IEqualityComparer<TKey> comparer = null)
        {
            _entries = new Dictionary<TKey, Entry>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public void Add(TKey key, long elapsed)
        {
            if (!_entries.TryGetValue(key, out Entry e))
                _entries[key] = e = new Entry { Key = key };
            e.Total += elapsed;
            e.Calls++;
            if (elapsed > e.Max) e.Max = elapsed;
        }

        public long Sum()
        {
            long s = 0;
            foreach (Entry e in _entries.Values) s += e.Total;
            return s;
        }

        public List<Entry> Top(int n, Func<TKey, string> name)
        {
            var all = new List<Entry>(_entries.Values);
            all.Sort(delegate (Entry a, Entry b)
            {
                int c = b.Total.CompareTo(a.Total);
                return c != 0 ? c : string.CompareOrdinal(name(a.Key), name(b.Key));
            });
            if (all.Count > n) all.RemoveRange(n, all.Count - n);
            return all;
        }

        public void Reset() => _entries.Clear();
    }

    // Stopwatch-tick arithmetic for the probe's report.
    public static class PerfMath
    {
        public static double Ms(long elapsed, long frequency) =>
            frequency <= 0 ? 0.0 : elapsed * 1000.0 / frequency;

        public static double PerUnitMs(long elapsed, long units, long frequency) =>
            units <= 0 ? 0.0 : Ms(elapsed, frequency) / units;

        public static double Percent(long part, long whole) =>
            whole <= 0 ? 0.0 : 100.0 * part / whole;
    }
}
