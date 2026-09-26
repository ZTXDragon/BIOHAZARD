using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Simulation;
using HarmonyLib;
using Halfling.Geometry;
using Halfling.Scene2D;
using Halfling.Timing;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    // Performance probe (2026-09-25).
    internal static class ZtxPerfProbe
    {
        private static readonly object s_lock = new object();

        private static readonly PerfLedger<Type> s_tickObjects = new PerfLedger<Type>();
        private static readonly PerfLedger<Type> s_updateObjects = new PerfLedger<Type>();
        private static readonly PerfLedger<Type> s_drawObjects = new PerfLedger<Type>();
        private static readonly PerfLedger<int> s_tickBuckets = new PerfLedger<int>();
        private static readonly PerfLedger<string> s_sections = new PerfLedger<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> s_tallies = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<int, string> s_bucketNames = new Dictionary<int, string>();

        private static bool s_enabled;
        private static long s_windowStart;
        private static long s_ticks, s_tickTotal, s_tickMax;
        private static long s_frames, s_updateTotal, s_drawTotal;
        private static long s_lastFrameEnd, s_frameMax;

        private static int s_tickLoopHits, s_updateLoopHits, s_drawLoopHits;

        public static bool Enabled => s_enabled;

        public static long Mark() => s_enabled ? Stopwatch.GetTimestamp() : 0;

        public static void Section(string key, long start)
        {
            if (!s_enabled || start == 0) return;
            long elapsed = Stopwatch.GetTimestamp() - start;
            lock (s_lock) s_sections.Add(key, elapsed);
        }

        public static void Tally(string key, long amount)
        {
            if (!s_enabled) return;
            lock (s_lock)
            {
                s_tallies.TryGetValue(key, out long v);
                s_tallies[key] = v + amount;
            }
        }

        public static void Install(Harmony harmony)
        {
            int ok = 0;

            ok += Patch(harmony, "tick loop", AccessTools.Method(typeof(SceneRoot), "FixedUpdateForBucket"),
                        transpiler: nameof(TickLoopTranspiler));
            ok += Patch(harmony, "update loop", AccessTools.Method(typeof(SceneRoot), "UpdateForBucket"),
                        transpiler: nameof(UpdateLoopTranspiler));
            ok += Patch(harmony, "draw loop", AccessTools.Method(typeof(SceneRoot), "DrawForBucket"),
                        transpiler: nameof(DrawLoopTranspiler));
            ok += Patch(harmony, "tick buckets", AccessTools.Method(typeof(SimRoot), "FixedUpdateForBucket"),
                        prefix: nameof(StartPrefix), postfix: nameof(BucketPostfix));
            ok += Patch(harmony, "tick total", AccessTools.Method(typeof(SimRoot), "FixedUpdate", Type.EmptyTypes),
                        prefix: nameof(StartPrefix), postfix: nameof(TickPostfix));
            ok += Patch(harmony, "update total", AccessTools.Method(typeof(SimRoot), "Update", Type.EmptyTypes),
                        prefix: nameof(StartPrefix), postfix: nameof(UpdatePostfix));
            ok += Patch(harmony, "draw total", AccessTools.Method(typeof(SimRoot), "Draw", new[] { typeof(bool), typeof(float) }),
                        prefix: nameof(StartPrefix), postfix: nameof(DrawPostfix));

            s_windowStart = Stopwatch.GetTimestamp();
            s_lastFrameEnd = 0;
            s_enabled = ok > 0;
            Log.Info("perf probe: " + ok + "/7 hooks installed; loop calls wrapped tick=" + s_tickLoopHits +
                     " update=" + s_updateLoopHits + " draw=" + s_drawLoopHits +
                     "; reporting every " + Config.PerfReportSeconds + " s as PERF lines");
        }

        private static int Patch(Harmony harmony, string label, MethodBase original,
                                 string prefix = null, string postfix = null, string transpiler = null)
        {
            try
            {
                if (original == null)
                {
                    Log.Error("perf probe: " + label + " target not found; that hook is off");
                    return 0;
                }
                Type self = typeof(ZtxPerfProbe);
                harmony.Patch(original,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(self, prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(self, postfix)),
                    transpiler: transpiler == null ? null : new HarmonyMethod(AccessTools.Method(self, transpiler)));
                return 1;
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxPerfProbe.Patch(" + label + ")", ex);
                return 0;
            }
        }

        // ---------------------------------------------------------------- per-object timing

        private static IEnumerable<CodeInstruction> TickLoopTranspiler(IEnumerable<CodeInstruction> code) =>
            Rewire(code, AccessTools.Method(typeof(IFixedUpdateableSceneObject), nameof(IFixedUpdateableSceneObject.FixedUpdate)),
                   AccessTools.Method(typeof(ZtxPerfProbe), nameof(TimedFixedUpdate)), out s_tickLoopHits);

        private static IEnumerable<CodeInstruction> UpdateLoopTranspiler(IEnumerable<CodeInstruction> code) =>
            Rewire(code, AccessTools.Method(typeof(IUpdateableSceneObject), nameof(IUpdateableSceneObject.Update)),
                   AccessTools.Method(typeof(ZtxPerfProbe), nameof(TimedUpdate)), out s_updateLoopHits);

        private static IEnumerable<CodeInstruction> DrawLoopTranspiler(IEnumerable<CodeInstruction> code) =>
            Rewire(code, AccessTools.Method(typeof(IRenderableSceneObject), nameof(IRenderableSceneObject.Draw)),
                   AccessTools.Method(typeof(ZtxPerfProbe), nameof(TimedDraw)), out s_drawLoopHits);

        private static List<CodeInstruction> Rewire(IEnumerable<CodeInstruction> code, MethodInfo target,
                                                    MethodInfo wrapper, out int hits)
        {
            var list = new List<CodeInstruction>(code);
            hits = 0;
            foreach (CodeInstruction ci in list)
            {
                if (target == null || wrapper == null || !ci.Calls(target)) continue;
                ci.opcode = OpCodes.Call;
                ci.operand = wrapper;
                hits++;
            }
            return list;
        }

        public static void TimedFixedUpdate(IFixedUpdateableSceneObject obj, FixedUpdater fixedUpdater, SceneRoot root)
        {
            long t0 = Stopwatch.GetTimestamp();
            obj.FixedUpdate(fixedUpdater, root);
            long elapsed = Stopwatch.GetTimestamp() - t0;
            lock (s_lock) s_tickObjects.Add(obj.GetType(), elapsed);
        }

        public static void TimedUpdate(IUpdateableSceneObject obj, SceneRoot root)
        {
            long t0 = Stopwatch.GetTimestamp();
            obj.Update(root);
            long elapsed = Stopwatch.GetTimestamp() - t0;
            lock (s_lock) s_updateObjects.Add(obj.GetType(), elapsed);
        }

        public static void TimedDraw(IRenderableSceneObject obj, SceneViewport viewport, SceneCamera camera,
                                     SceneRoot root, RotRect viewRect)
        {
            long t0 = Stopwatch.GetTimestamp();
            obj.Draw(viewport, camera, root, viewRect);
            long elapsed = Stopwatch.GetTimestamp() - t0;
            lock (s_lock) s_drawObjects.Add(obj.GetType(), elapsed);
        }

        // ---------------------------------------------------------------- totals

        private static void StartPrefix(out long __state) => __state = Stopwatch.GetTimestamp();

        private static void BucketPostfix(SceneRoot __instance, int bucket, long __state)
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            lock (s_lock)
            {
                s_tickBuckets.Add(bucket, elapsed);
                if (!s_bucketNames.ContainsKey(bucket))
                {
                    s_bucketNames[bucket] = __instance.FixedUpdateBucketNames.TryGetValue(bucket, out string n)
                        ? n + "(" + bucket + ")"
                        : bucket.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static void TickPostfix(long __state)
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            lock (s_lock)
            {
                s_ticks++;
                s_tickTotal += elapsed;
                if (elapsed > s_tickMax) s_tickMax = elapsed;
            }
        }

        private static void UpdatePostfix(long __state)
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            lock (s_lock) s_updateTotal += elapsed;
        }

        private static void DrawPostfix(long __state)
        {
            long now = Stopwatch.GetTimestamp();
            lock (s_lock)
            {
                s_frames++;
                s_drawTotal += now - __state;
                if (s_lastFrameEnd != 0 && now - s_lastFrameEnd > s_frameMax) s_frameMax = now - s_lastFrameEnd;
                s_lastFrameEnd = now;

                if (!s_enabled) return;
                if (PerfMath.Ms(now - s_windowStart, Stopwatch.Frequency) < Config.PerfReportSeconds * 1000.0) return;
                try
                {
                    Report(now);
                }
                catch (Exception ex)
                {
                    // One report failing must not spam a line every frame after it.
                    s_enabled = false;
                    Log.Exception("ZtxPerfProbe.Report (probe now off)", ex);
                }
                ResetWindow(now);
            }
        }

        // ---------------------------------------------------------------- report

        private static void Report(long now)
        {
            long f = Stopwatch.Frequency;
            double seconds = PerfMath.Ms(now - s_windowStart, f) / 1000.0;
            double wallPerFrame = s_frames > 0 ? seconds * 1000.0 / s_frames : 0.0;
            double updatePerFrame = PerfMath.PerUnitMs(s_updateTotal, s_frames, f);
            double drawPerFrame = PerfMath.PerUnitMs(s_drawTotal, s_frames, f);

            Log.Info(Fmt("PERF {0:0.0} s | ticks {1} ({2:0.0}/s) avg {3:0.00} ms, worst {4:0.00} ms | frames {5} ({6:0.0}/s) " +
                         "{7:0.00} ms each: sim update {8:0.00} (includes ticks), draw {9:0.00}, rest {10:0.00} (GUI, input, present); worst frame {11:0.0} ms",
                         seconds, s_ticks, s_ticks / seconds, PerfMath.PerUnitMs(s_tickTotal, s_ticks, f),
                         PerfMath.Ms(s_tickMax, f), s_frames, s_frames / seconds, wallPerFrame, updatePerFrame,
                         drawPerFrame, Math.Max(0.0, wallPerFrame - updatePerFrame - drawPerFrame),
                         PerfMath.Ms(s_frameMax, f)));

            long bucketSum = s_tickBuckets.Sum();
            foreach (PerfLedger<int>.Entry e in s_tickBuckets.Top(6, BucketName))
                Log.Info(Fmt("PERF   tick bucket  {0,-34} {1,7:0.000} ms/tick {2,5:0.0}%  worst {3,6:0.00} ms",
                             BucketName(e.Key), PerfMath.PerUnitMs(e.Total, s_ticks, f),
                             PerfMath.Percent(e.Total, bucketSum), PerfMath.Ms(e.Max, f)));

            WriteObjects("tick object ", s_tickObjects, 8, s_ticks, "tick", f);
            WriteObjects("update obj  ", s_updateObjects, 5, s_frames, "frame", f);
            WriteObjects("draw object ", s_drawObjects, 8, s_frames, "frame", f);

            foreach (PerfLedger<string>.Entry e in s_sections.Top(16, s => s))
                Log.Info(Fmt("PERF   bio step     {0,-34} {1,7:0.000} ms/tick  {2,7:0.00} calls/tick  worst {3,6:0.00} ms",
                             e.Key, PerfMath.PerUnitMs(e.Total, s_ticks, f),
                             s_ticks > 0 ? (double)e.Calls / s_ticks : 0.0, PerfMath.Ms(e.Max, f)));

            var names = new List<string>(s_tallies.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
                Log.Info(Fmt("PERF   bio count    {0,-34} {1,10:0.0} per tick",
                             name, s_ticks > 0 ? (double)s_tallies[name] / s_ticks : 0.0));
        }

        private static void WriteObjects(string label, PerfLedger<Type> ledger, int n, long units, string unit, long f)
        {
            long sum = ledger.Sum();
            foreach (PerfLedger<Type>.Entry e in ledger.Top(n, TypeName))
                Log.Info(Fmt("PERF   {0} {1,-34} {2,7:0.000} ms/{3} {4,5:0.0}%  {5,7:0.0} calls/{3}  worst {6,6:0.00} ms",
                             label, TypeName(e.Key), PerfMath.PerUnitMs(e.Total, units, f), unit,
                             PerfMath.Percent(e.Total, sum), units > 0 ? (double)e.Calls / units : 0.0,
                             PerfMath.Ms(e.Max, f)));
        }

        private static void ResetWindow(long now)
        {
            s_tickObjects.Reset();
            s_updateObjects.Reset();
            s_drawObjects.Reset();
            s_tickBuckets.Reset();
            s_sections.Reset();
            s_tallies.Clear();
            s_ticks = s_tickTotal = s_tickMax = 0;
            s_frames = s_updateTotal = s_drawTotal = s_frameMax = 0;
            s_windowStart = now;
        }

        private static string BucketName(int bucket) =>
            s_bucketNames.TryGetValue(bucket, out string n) ? n : bucket.ToString(CultureInfo.InvariantCulture);

        private static string TypeName(Type t) => t?.Name ?? "?";

        private static string Fmt(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
