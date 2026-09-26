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
using Vector2 = Halfling.Geometry.Vector2;

namespace ZTX.BioCirculation.Game
{
    // Files the resource-delivery jobs that feed build sites on crewless ships.
    internal static class ZtxConstructionSupply
    {

        public static int Update(Ship ship, List<ResourceTransferJob> filed)
        {
            if (ship?.Sim == null) return 0;
            var sources = new List<(IResourceSource Source, int Available)>();

            // ---- GC our previously filed jobs -------------------------------------
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
                    if (!done) job.Cancel();
                    filed.RemoveAt(i);
                }
            }

            // ---- file new jobs -----------------------------------------------------
            ConstructionManager cm = ship.OptionalConstructionManager;
            if (cm == null) return 0;

            int filedCount = 0;
            IReadOnlyList<QueuedPartConstruction> queue = cm.QueuedPartConstructions;
            for (int q = 0; q < queue.Count; q++)
            {
                QueuedPartConstruction qpc = queue[q];
                // IsReady gates PartConstructionSink.Sim — the haul manager's candidate
                // filter drops sinks whose Sim is null, so filing early is pure waste.
                if (qpc == null || !qpc.IsReady) continue;

                IReadOnlyList<QueuedPartConstruction.PartConstructionSink> sinks = qpc.ResourceSinks;
                for (int s = 0; s < sinks.Count; s++)
                {
                    QueuedPartConstruction.PartConstructionSink sink = sinks[s];
                    filedCount += FileForSink(ship, sink, sink.ResourceType, sink.Center,
                                              sources, filed);
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
                                              sources, filed);
                }
            }
            return filedCount;
        }

        private static int FileForSink(Ship ship, IResourceSink sink, ID<ResourceRules> type,
                                       Vector2 center,
                                       List<(IResourceSource Source, int Available)> sources,
                                       List<ResourceTransferJob> filed)
        {
            int need = sink.GetRemainingCapacity(type, false);
            if (need <= 0) return 0;

            // Net out everything already on its way — our jobs, nugget jobs the pickup half
            // re-filed, anything else targeting this sink.
            foreach (ResourceTransferJob j in
                     ResourceTransferJob.GetTransferJobsFor(sink, MPValueType.Confirmed))
            {
                need -= j.ResourcesRequested.Confirmed - j.ResourcesInHand;
                if (need <= 0) break;
            }
            if (need <= 0) return 0;

            int filedCount = 0;
            CollectSources(ship, type, sources);
            for (int i = 0; i < sources.Count && need > 0; i++)
            {
                (IResourceSource source, int available) = sources[i];
                int amount = need < available ? need : available;
                if (amount <= 0) continue;

                float dist = source.Part != null
                    ? source.Part.LocalCenter.DistanceTo(center)
                    : 0f;
                ResourceTransferJob job = new ResourceTransferJob(
                    source, sink, type, new MPValue<int>(amount), dist);
                ship.Jobs.AddJob(job);
                filed.Add(job);
                filedCount++;
                need -= amount;
            }
            return filedCount;
        }

        private static void CollectSources(Ship ship, ID<ResourceRules> type,
                                           List<(IResourceSource Source, int Available)> sources)
        {
            sources.Clear();

            foreach (BaseResourceStorage storage in BaseResourceStorage.GetAllSuppliersOnShip(ship))
            {
                if (storage.ResourceType != type) continue;
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
