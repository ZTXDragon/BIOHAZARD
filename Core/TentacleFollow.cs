namespace ZTX.BioCirculation.Core
{
    // When a tentacle arm draws its tip on the simulated carry tip (2026-09-26, ZTX: "every time
    // tentacle finishes caring an item it teleports to its default position and then teleports back
    public static class TentacleFollow
    {
        public static bool OnSimTip(bool initialized, bool hasJob, bool homing)
        {
            return initialized && (hasJob || homing);
        }

        public static bool StillHoming(float distanceBefore, float maxStep)
        {
            return distanceBefore > maxStep;
        }
    }
}
