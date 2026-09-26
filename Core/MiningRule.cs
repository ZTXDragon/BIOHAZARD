namespace ZTX.BioCirculation.Core
{
    // Whether a hauler organ may work a salvage job on ANOTHER ship - which is what mining an
    // asteroid is, since an asteroid is a ship built out of rock and deposit parts
    public static class MiningRule
    {
        public static bool AcceptsTarget(bool targetOnOwnShip, bool serveMining)
        {
            return targetOnOwnShip || serveMining;
        }

        public static bool InWorldRange(float worldDistance, float rangeTiles, float shipScale)
        {
            if (rangeTiles <= 0f) return false;
            if (shipScale <= 0f) return false;      // a degenerate transform reaches nothing
            if (worldDistance < 0f) return false;
            return worldDistance <= rangeTiles * shipScale;
        }
    }
}
