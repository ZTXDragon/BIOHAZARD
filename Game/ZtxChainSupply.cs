using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Resources;

namespace ZTX.BioCirculation.Game
{
    // Files the transfer jobs behind player SUPPLY-CHAIN LINKS on crewless ships (2026-09-12, user
    // report: linking a storage to feed the stomach did nothing while moving individual resources
    internal static class ZtxChainSupply
    {
        private const int MaxJobsPerLink = 4;

        public static int Update(Ship ship, List<ResourceTransferJob> filed)
        {
            if (ship?.Sim == null) return 0;

            // ---- GC our previously filed jobs (same discipline as construction) ----
            for (int i = filed.Count - 1; i >= 0; i--)
            {
                ResourceTransferJob job = filed[i];
                if (job == null) { filed.RemoveAt(i); continue; }
                int remaining = job.ResourcesRequested.Confirmed - job.ResourcesInHand;
                bool done = remaining <= 0;
                bool sinkStale = !done && job.Sink.GetRemainingCapacity(job.ResourceType, false) <= 0;
                bool sourceDead = !done && !sinkStale &&
                                  (job.Source == null || job.Source.Resources <= 0);
                if (done || sinkStale || sourceDead)
                {
                    if (!done)
                    {
                        if (ZtxHaulDebug.Verbose)
                            Log.Info("chain GC: cancel (" + (sinkStale ? "sink-full" : "source-dead") +
                                     ") " + ZtxHaulDebug.Src(job.Source) + " -> " +
                                     ZtxHaulDebug.Sink(job.Sink) + " " + job.ResourceType +
                                     " remaining=" + remaining);
                        job.Cancel();
                    }
                    filed.RemoveAt(i);
                }
            }

            int filedCount = 0;
            var targets = new List<BaseResourceConsumer>();
            var sources = new List<(IResourceSource Source, int Available)>();

            var linkPairs = new HashSet<(Part, Part)>();
            foreach (Part supplier in ship.Parts)
            {
                targets.Clear();
                BaseResourceConsumer.GetSupplierTargets(supplier, targets);
                for (int i = 0; i < targets.Count; i++)
                {
                    Part tp = targets[i]?.Part;
                    if (tp != null && tp.Ship == ship && tp != supplier)
                        linkPairs.Add((supplier, tp));
                }
            }

            foreach (Part supplier in ship.Parts)
            {
                targets.Clear();
                BaseResourceConsumer.GetSupplierTargets(supplier, targets);
                if (targets.Count == 0) continue;

                foreach (BaseResourceConsumer consumer in targets)
                {
                    Part targetPart = consumer?.Part;
                    if (targetPart == null || targetPart.Ship != ship || targetPart == supplier) continue;
                    if (linkPairs.Contains((targetPart, supplier)))
                    {
                        if (ZtxHaulDebug.Verbose)
                            Log.Info("chain: RECIPROCAL links " + supplier + "@" + supplier.Location +
                                     " <-> " + targetPart + "@" + targetPart.Location +
                                     " - filing neither direction");
                        continue;
                    }

                    int budget = MaxJobsPerLink;
                    if (consumer is FlexResourceGrid flex)
                    {
                        // Any stackable the supplier holds may flow into the maw.
                        CollectPartSources(ship, supplier, null, sources);
                        foreach ((IResourceSource source, int available) in sources)
                        {
                            if (budget <= 0) break;
                            int avail = available;
                            foreach (FlexResourceGrid.ResourceTile tile in flex)
                            {
                                if (budget <= 0 || avail <= 0) break;
                                filedCount += TryFile(ship, filed, source, tile,
                                                      source.ResourceType, ref avail, ref budget);
                            }
                        }
                    }
                    else if (consumer is TypedResourceGridConsumer)
                    {
                        ID<ResourceRules> rt = consumer.ResourceType;
                        CollectPartSources(ship, supplier, rt, sources);
                        foreach (TypedResourceGrid grid in TypedResourceGrid.GetAllOnShip(ship))
                        {
                            if (budget <= 0) break;
                            if (grid.Part != targetPart || grid.ResourceType != rt) continue;
                            foreach ((IResourceSource source, int available) in sources)
                            {
                                if (budget <= 0) break;
                                int avail = available;
                                filedCount += TryFile(ship, filed, source, grid, rt, ref avail, ref budget);
                            }
                        }
                    }
                    // Other consumer kinds (factory ResourceConsumer inputs) are fed by
                    // their own systems; a chain into one is ignored rather than fought.
                }
            }
            return filedCount;
        }

        private static int TryFile(Ship ship, List<ResourceTransferJob> filed,
                                   IResourceSource source, IResourceSink sink,
                                   ID<ResourceRules> rt, ref int avail, ref int budget)
        {
            if (source == sink) return 0;
            int need = sink.GetRemainingCapacity(rt, false);
            if (need <= 0) return 0;
            foreach (ResourceTransferJob j in ResourceTransferJob.GetTransferJobsFor(sink, MPValueType.Confirmed))
            {
                need -= j.ResourcesRequested.Confirmed - j.ResourcesInHand;
                if (need <= 0) return 0;
            }
            int amount = need < avail ? need : avail;
            if (amount <= 0) return 0;

            float dist = source.Part != null
                ? source.Part.LocalCenter.DistanceTo(sink.Center)
                : 0f;
            ResourceTransferJob job = new ResourceTransferJob(
                source, sink, rt, new MPValue<int>(amount), dist);
            ship.Jobs.AddJob(job);
            filed.Add(job);
            if (ZtxHaulDebug.Verbose)
                Log.Info("chain file: " + amount + " " + rt + " " + ZtxHaulDebug.Src(source) +
                         " -> " + ZtxHaulDebug.Sink(sink));
            avail -= amount;
            budget--;
            return 1;
        }

        private static void CollectPartSources(Ship ship, Part supplier, ID<ResourceRules>? onlyType,
                                               List<(IResourceSource Source, int Available)> sources)
        {
            sources.Clear();
            foreach (TypedResourceGrid grid in TypedResourceGrid.GetAllOnShip(ship))
            {
                if (grid.Part != supplier) continue;
                if (onlyType.HasValue && grid.ResourceType != onlyType.Value) continue;
                foreach (TypedResourceGrid.ResourceTile tile in grid)
                    AddSource(ship, tile, sources);
            }
            foreach (FlexResourceGrid flex in FlexResourceGrid.GetAllOnShip(ship))
            {
                if (flex.Part != supplier) continue;
                foreach (FlexResourceGrid.ResourceTile tile in flex)
                {
                    if (onlyType.HasValue && tile.ResourceType != onlyType.Value) continue;
                    AddSource(ship, tile, sources);
                }
            }
            IReadOnlyList<PartComponent> comps = supplier.Components;
            for (int i = 0; i < comps.Count; i++)
            {
                if (!(comps[i] is ResourceStorage store)) continue;
                if (!store.Rules.AllowExternalPickupAndDelivery) continue;
                if (onlyType.HasValue && store.ResourceType != onlyType.Value) continue;
                AddSource(ship, store, sources);
            }
            if (sources.Count > 1) sources.Sort(CompareSources);
        }

        private static void AddSource(Ship ship, IResourceSource source,
                                      List<(IResourceSource Source, int Available)> sources)
        {
            int available = source.Resources -
                            ship.Resources.GetAnticipatedPickUp(source, MPValueType.Confirmed);
            if (available > 0) sources.Add((source, available));
        }

        private static int CompareSources((IResourceSource Source, int Available) a,
                                          (IResourceSource Source, int Available) b)
        {
            int c = b.Available.CompareTo(a.Available);
            if (c != 0) return c;
            Part pa = a.Source.Part, pb = b.Source.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }
    }
}
