using System;

namespace ZTX.BioCirculation.Core
{
    // When a tentacle arm draws its tip on the simulated carry tip (2026-09-26, ZTX: "every time
    // tentacle finishes caring an item it teleports to its default position and then teleports back
    public static class TentacleFollow
    {
        public const float HoldFanTiles = 0.45f;

        public static bool OnSimTip(bool initialized, bool hasJob, bool homing, bool holding)
        {
            return initialized && (hasJob || homing || holding);
        }

        public static bool StillHoming(float distanceBefore, float maxStep)
        {
            return distanceBefore > maxStep;
        }

        public static int WorkTarget(int slot, int targets)
        {
            if (targets <= 0 || slot < 0) return -1;
            return slot % targets;
        }

        public static void HoldOffset(int slot, int slots, float radius, out float dx, out float dy)
        {
            dx = 0f;
            dy = 0f;
            if (slots <= 1 || radius <= 0f) return;
            double a = 2.0 * Math.PI * (slot % slots) / slots;
            dx = (float)(Math.Cos(a) * radius);
            dy = (float)(Math.Sin(a) * radius);
        }
    }
}
