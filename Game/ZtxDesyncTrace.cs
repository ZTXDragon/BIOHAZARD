using System;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using Halfling.Gui.Dialogs;
using HarmonyLib;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    internal static class ZtxDesyncTrace
    {
        private static readonly ConditionalWeakTable<SimRoot, object> s_begun =
            new ConditionalWeakTable<SimRoot, object>();

        private static int s_nextLabel;

        private static int Label(SimRoot sim)
        {
            if (s_begun.TryGetValue(sim, out object boxed)) return (int)boxed;
            int label = ++s_nextLabel;
            s_begun.Add(sim, label);
            Log.Info("DSYNC-BEGIN sim=" + label + " t=" + sim.Tick + " in=" + InputTick(sim) +
                     " every=" + (Config.DesyncTraceTicks > 0 ? Config.DesyncTraceTicks : 30) +
                     " ships=" + sim.Ships.Count + " nuggets=" + sim.Nuggets.Count +
                     " mode=" + (sim.Mode?.GetType().Name ?? "?"));
            return label;
        }

        private static WeakReference<SimRoot> s_lastSim = new WeakReference<SimRoot>(null);

        internal static bool On => Config.DesyncTrace;

        internal static int InputTick(SimRoot sim)
        {
            return sim?.Game?.NetManager is BaseMPManager m ? m.NetworkInputTick : -1;
        }

        internal static void Event(Ship ship, string text)
        {
            if (!Config.DesyncTrace || ship?.Sim == null) return;
            Log.Info("DEVT t=" + ship.Sim.Tick + " sim=" + Label(ship.Sim) + " ship=" + ship.UniqueID + " " + text);
        }

        internal static string Loc(Part p)
        {
            return p == null ? "-" : p.Location.X + "," + p.Location.Y;
        }

        internal static uint JobKey(ResourceTransferJob j)
        {
            TraceHash h = TraceHash.Begin();
            if (j == null) return h.Add(-1).Value;
            try
            {
                IResourceSource src = j.Source;
                if (src is Nugget n)
                {
                    h.Add("N").Add(n.UniqueID.ToString());
                }
                else if (src?.Part != null)
                {
                    h.Add(src.Part.Location.X).Add(src.Part.Location.Y);
                    h.Add(src.Center.X).Add(src.Center.Y);
                }
                else
                {
                    h.Add("nosource");
                }
                if (j.Sink != null) h.Add(j.Sink.Center.X).Add(j.Sink.Center.Y);
                h.Add(j.ResourceType.ToString());
                h.Add(j.ResourcesRequested.Confirmed).Add(j.ResourcesInHand);
                h.Add(j.IsManual ? j.ManualID.Value.SequenceNum : -1);
            }
            catch (Exception)
            {
                h.Add("unreadable");
            }
            return h.Value;
        }

        internal static string JobText(ResourceTransferJob j)
        {
            if (j == null) return "none";
            string src = j.Source is Nugget n ? "nugget#" + n.UniqueID
                       : j.Source?.Part != null ? Loc(j.Source.Part) : "?";
            return j.ResourcesRequested.Confirmed - j.ResourcesInHand + " " + j.ResourceType +
                   " " + src + "->" + (j.Sink != null ? j.Sink.Center.X + "," + j.Sink.Center.Y : "?") +
                   " [" + JobKey(j).ToString("x8") + "]";
        }

        [HarmonyPatch(typeof(SimRoot), "FixedUpdate")]
        internal static class SnapshotPatch
        {
            private static void Postfix(SimRoot __instance)
            {
                if (!Config.DesyncTrace || __instance == null) return;
                try
                {
                    Snapshot(__instance);
                }
                catch (Exception ex)
                {
                    Log.Exception("ZtxDesyncTrace.Snapshot", ex);
                }
            }
        }

        private static void Snapshot(SimRoot sim)
        {
            s_lastSim = new WeakReference<SimRoot>(sim);
            int tick = sim.Tick;
            int every = Config.DesyncTraceTicks > 0 ? Config.DesyncTraceTicks : 30;
            int label = Label(sim);
            if (tick % every != 0) return;
            string head = "DSYNC t=" + tick + " sim=" + label + " in=" + InputTick(sim);

            TraceHash world = TraceHash.Begin();
            uint worldSet = 0;
            int nuggets = 0;
            foreach (Nugget g in sim.Nuggets)
            {
                if (g == null) continue;
                TraceHash one = TraceHash.Begin().Add(g.UniqueID.ToString()).Add(g.ResourceType.ToString())
                    .Add(g.Quantity).Add(g.DetWorldLocation.X).Add(g.DetWorldLocation.Y)
                    .Add(g.LinearVelocity.X).Add(g.LinearVelocity.Y);
                world.Add(one.Value);
                worldSet += one.Value;
                nuggets++;
            }
            Log.Info(head + " world nuggets=" + nuggets + " order=" + world.Hex + " set=" + worldSet.ToString("x8"));

            for (int i = 0; i < sim.Ships.Count; i++)
            {
                Ship ship = sim.Ships[i];
                if (ship == null) continue;
                ZtxHaulManager haul = ZtxHaulManager.Find(ship);
                BloodFlowManager blood = BloodFlowManager.Find(ship);
                BioHealManager heal = BioHealManager.Find(ship);
                ZtxDigestManager digest = ZtxDigestManager.Find(ship);
                ZtxBiomassShareManager share = ZtxBiomassShareManager.Find(ship);
                ZtxBrainManager brain = ZtxBrainManager.Find(ship);
                if (haul == null && blood == null && heal == null && digest == null &&
                    share == null && brain == null) continue;

                var line = new System.Text.StringBuilder(head);
                line.Append(" ship=").Append(ship.UniqueID);

                TraceHash jobs = TraceHash.Begin();
                uint jobSet = 0;
                int jobCount = 0;
                foreach (ResourceTransferJob j in ResourceTransferJob.GetTransferJobsFor(ship, MPValueType.Confirmed))
                {
                    uint k = JobKey(j);
                    jobs.Add(k);
                    jobSet += k;
                    jobCount++;
                }
                int allJobs = ResourceTransferJob.s_shipTransferJobs.TryGetValue(ship, out var list) ? list.Count : 0;
                line.Append(" jobs=").Append(jobs.Hex).Append(" jobset=").Append(jobSet.ToString("x8"))
                    .Append(" njobs=").Append(jobCount).Append(" alljobs=").Append(allJobs);

                Section(line, "haul", haul == null ? (uint?)null : Hash((ref TraceHash h) => haul.TraceState(ref h)));
                Section(line, "blood", blood == null ? (uint?)null : Hash((ref TraceHash h) => blood.TraceState(ref h)));
                Section(line, "heal", heal == null ? (uint?)null : Hash((ref TraceHash h) => heal.TraceState(ref h)));
                Section(line, "digest", digest == null ? (uint?)null : Hash((ref TraceHash h) => digest.TraceState(ref h)));
                Section(line, "share", share == null ? (uint?)null : Hash((ref TraceHash h) => share.TraceState(ref h)));
                Section(line, "brain", brain == null ? (uint?)null : Hash((ref TraceHash h) => brain.TraceState(ref h)));

                TraceHash eyes = TraceHash.Begin();
                int eyeCount = 0;
                var all = ZtxEyeCone.All;
                for (int e = 0; e < all.Count; e++)
                {
                    ZtxEyeCone eye = all[e];
                    if (eye?.Part?.Ship != ship) continue;
                    eye.TraceState(ref eyes);
                    eyeCount++;
                }
                if (eyeCount > 0) line.Append(" eye=").Append(eyes.Hex);

                TraceHash res = TraceHash.Begin();
                foreach (string r in new[] { "ztx.biomass", "ztx.boneshards", "ztx.saturated_blood",
                                             "ztx.depleted_blood", "ztx.healing" })
                    res.Add(ship.Resources.GetResourceTotal(new Cosmoteer.Data.ID<ResourceRules>(r)));
                line.Append(" res=").Append(res.Hex);

                if (haul != null) line.Append(' ').Append(haul.TraceCounts());
                Log.Info(line.ToString());
            }
        }

        private delegate void Fill(ref TraceHash h);

        private static uint Hash(Fill fill)
        {
            TraceHash h = TraceHash.Begin();
            fill(ref h);
            return h.Value;
        }

        private static void Section(System.Text.StringBuilder line, string name, uint? value)
        {
            if (value.HasValue) line.Append(' ').Append(name).Append('=').Append(value.Value.ToString("x8"));
        }

        [HarmonyPatch(typeof(OneButtonDialog), nameof(OneButtonDialog.Show), new[] { typeof(string), typeof(string) })]
        internal static class ReplayVerdictPatch
        {
            private static void Prefix(string message)
            {
                if (message == null) return;
                if (message.IndexOf("desynced", StringComparison.OrdinalIgnoreCase) < 0 &&
                    message.IndexOf("End of Replay", StringComparison.OrdinalIgnoreCase) < 0) return;
                s_lastSim.TryGetTarget(out SimRoot sim);
                bool desync = message.IndexOf("desynced", StringComparison.OrdinalIgnoreCase) >= 0;
                Log.Info((desync ? "DSYNC-DETECTED" : "DSYNC-CLEAN") + " replay \"" + message + "\" at sim tick " +
                         (sim != null ? sim.Tick.ToString() : "?") + ", input tick " + InputTick(sim) +
                         (desync ? " (the engine compares at input ticks; the divergence is at or before this)"
                                 : " (every integrity hash in the recording matched)"));
            }
        }

        [HarmonyPatch(typeof(ErrorReporter), nameof(ErrorReporter.OnOutOfSync))]
        internal static class OutOfSyncPatch
        {
            private static void Prefix(bool isHost, bool isOriginalClient, int inputTick)
            {
                s_lastSim.TryGetTarget(out SimRoot sim);
                Log.Info("DSYNC-DETECTED multiplayer out of sync at input tick " + inputTick +
                         " (host=" + isHost + ", originalClient=" + isOriginalClient + "), sim tick " +
                         (sim != null ? sim.Tick.ToString() : "?"));
            }
        }
    }
}
