using System;
using System.Collections.Generic;

namespace ZTX.BioCirculation.Core
{
    public static class ArrowLanes
    {
        public const float SamePoint = 0.01f;

        public static bool[] NeedsLane(IReadOnlyList<(float X, float Y, bool Artery)> joints)
        {
            var lane = new bool[joints.Count];
            for (int i = 0; i < joints.Count; i++)
            {
                for (int j = 0; j < joints.Count; j++)
                {
                    if (i == j || joints[i].Artery == joints[j].Artery) continue;
                    if (Math.Abs(joints[i].X - joints[j].X) < SamePoint && Math.Abs(joints[i].Y - joints[j].Y) < SamePoint)
                    {
                        lane[i] = true;
                        break;
                    }
                }
            }
            return lane;
        }
    }
}
