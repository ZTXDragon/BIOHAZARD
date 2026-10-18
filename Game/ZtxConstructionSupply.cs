using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Construction;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Resources;
using ZTX.BioCirculation.Core;
using Vector2 = Halfling.Geometry.Vector2;

namespace ZTX.BioCirculation.Game
{
    internal static class ZtxConstructionSupply
    {
        public static int Update(Ship ship, List<ResourceTransferJob> filed, ZtxHaulManager haul)
        {
            if (ship?.Sim == null) return 0;
            var sources = new List<(IResourceSource Source, int Available)>();

            for (int i = filed.Count - 1; i >= 0; i--)
            {
                ResourceTransferJob job = filed[i];
                if (job == null) { filed.RemoveAt(i); continue; }

                int remaining = job.ResourcesRequested.Confirmed - job.ResourcesInHand;
                bool done = remaining <= 0;
                bool sinkStale = !done && job.Sink.GetRemainingCapacity(job.ResourceType, false) <= 0;
                bool sourceDead = !done && !sinkStale &&
                                  (job.Source == null || job.Source.Resources <= 0);
                bool orphaned = !done && !sinkStale && !sourceDead &&
                                (!job.Source.SuppliesResources || !Covered(haul, job.Source, job.Sink.Center));
                if (done || sinkStale || sourceDead || orphaned)
                {
                    if (!done)
                        ZtxDesyncTrace.Event(ship, "supply cancel (" +
                                             (sinkStale ? "site full" : sourceDead ? "source empty" : "no organ or opted out") +
                                             ") " + ZtxDesyncTrace.JobText(job));
                    ZtxModJobs.Retire(job);
                    filed.RemoveAt(i);
                }
            }

            ConstructionManager cm = ship.OptionalConstructionManager;
            if (cm == null) return 0;

            int filedCount = 0;
            IReadOnlyList<QueuedPartConstruction> queue = cm.QueuedPartConstructions;
            for (int q = 0; q < queue.Count; q++)
            {
                QueuedPartConstruction qpc = queue[q];
                if (qpc == null || !qpc.IsReady) continue;

                IReadOnlyList<QueuedPartConstruction.PartConstructionSink> sinks = qpc.ResourceSinks;
                for (int s = 0; s < sinks.Count; s++)
                {
                    QueuedPartConstruction.PartConstructionSink sink = sinks[s];
                    filedCount += FileForSink(ship, sink, sink.ResourceType, sink.Center,
                                              sources, filed, haul);
                }
            }

            IReadOnlyList<QueuedPartRepair> repairs = cm.QueuedPartRepairs;
            for (int q = 0; q < repairs.Count; q++)
            {
                QueuedPartRepair qpr = repairs[q];
                if (qpr == null || !qpr.IsReady) continue;

                IReadOnlyList<QueuedPartRepair.PartRepairSink> sinks = qpr.ResourceSinks;
                for (int s = 0; s < sinks.Count; s++)
                {
                    QueuedPartRepair.PartRepairSink sink = sinks[s];
                    filedCount += FileForSink(ship, sink, sink.ResourceType, sink.Center,
                                              sources, filed, haul);
                }
            }
            return filedCount;
        }

        private static int FileForSink(Ship ship, IResourceSink sink, ID<ResourceRules> type,
                                       Vector2 center,
                                       List<(IResourceSource Source, int Available)> sources,
                                       List<ResourceTransferJob> filed, ZtxHaulManager haul)
        {
            int need = sink.GetRemainingCapacity(type, false);
            if (need <= 0) return 0;

            foreach (ResourceTransferJob j in
                     ResourceTransferJob.GetTransferJobsFor(sink, MPValueType.Confirmed))
            {
                need -= j.ResourcesRequested.Confirmed - j.ResourcesInHand;
                if (need <= 0) break;
            }
            if (need <= 0) return 0;

            int filedCount = 0;
            CollectSources(ship, type, center, sources);
            for (int i = 0; i < sources.Count && need > 0; i++)
            {
                (IResourceSource source, int available) = sources[i];
                if (!Covered(haul, source, center)) continue;
                int amount = need < available ? need : available;
                if (amount <= 0) continue;

                float dist = source.Part != null
                    ? source.Part.LocalCenter.DistanceTo(center)
                    : 0f;
                ResourceTransferJob job = new ResourceTransferJob(
                    source, sink, type, new MPValue<int>(amount), dist);
                ship.Jobs.AddJob(job);
                ZtxModJobs.Register(job);
                filed.Add(job);
                ZtxDesyncTrace.Event(ship, "supply file " + ZtxDesyncTrace.JobText(job));
                filedCount++;
                need -= amount;
            }
            return filedCount;
        }

        private static void CollectSources(Ship ship, ID<ResourceRules> type, Vector2 site,
                                           List<(IResourceSource Source, int Available)> sources)
        {
            sources.Clear();

            foreach (BaseResourceStorage storage in BaseResourceStorage.GetAllSuppliersOnShip(ship))
            {
                if (storage.ResourceType != type) continue;
                if (!((IResourceSource)storage).SuppliesResources) continue;
                if (storage is TypedResourceGrid grid)
                {
                    foreach (TypedResourceGrid.ResourceTile tile in grid)
                        AddSource(ship, tile, sources);
                }
                else
                {
                    AddSource(ship, storage, sources);
                }
            }

            if (sources.Count > 1) sources.Sort((a, b) => CompareSources(a, b, site));
        }

        private static void AddSource(Ship ship, IResourceSource source,
                                      List<(IResourceSource Source, int Available)> sources)
        {
            int owed = 0;
            foreach (ResourceTransferJob j in
                     ResourceTransferJob.GetTransferJobsFor(source, MPValueType.Confirmed))
            {
                owed += j.ResourcesRequested.Confirmed - j.ResourcesInHand;
            }
            int available = SupplyRule.Available(source.Resources, owed);
            if (available > 0) sources.Add((source, available));
        }

        private static bool Covered(ZtxHaulManager haul, IResourceSource source, Vector2 sink)
        {
            Part part = source?.Part;
            return part != null && haul != null && haul.AnyBuilderCovers(part.LocalCenter, sink);
        }

        private static int CompareSources((IResourceSource Source, int Available) a,
                                          (IResourceSource Source, int Available) b, Vector2 site)
        {
            Part pa = a.Source.Part, pb = b.Source.Part;
            if (pa == null) return pb == null ? 0 : 1;
            if (pb == null) return -1;
            int c = SupplyRule.Compare(a.Source.Center.DistanceSquaredTo(site), a.Available,
                                       b.Source.Center.DistanceSquaredTo(site), b.Available);
            if (c != 0) return c;
            c = pa.Location.X.CompareTo(pb.Location.X);
            return c != 0 ? c : pa.Location.Y.CompareTo(pb.Location.Y);
        }
    }
}
