using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;

namespace ZTX.BioCirculation.Game
{
    internal static class ZtxModJobs
    {
        private static readonly ConditionalWeakTable<ResourceTransferJob, object> s_mine =
            new ConditionalWeakTable<ResourceTransferJob, object>();

        internal static void Register(ResourceTransferJob job)
        {
            if (job != null) s_mine.AddOrUpdate(job, null);
        }

        internal static bool IsMine(ResourceTransferJob job)
        {
            return job != null && s_mine.TryGetValue(job, out _);
        }

        internal static void Retire(ResourceTransferJob job)
        {
            if (!IsMine(job)) return;
            s_mine.Remove(job);
            if (job.ResourcesRequested.Confirmed > 0) job.Cancel();
            Ship ship = job.Ship;
            if (ship != null) ship.Jobs.RemoveJob(job, unassignCrew: true);
        }
    }
}
