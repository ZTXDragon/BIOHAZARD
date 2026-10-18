namespace ZTX.BioCirculation.Core
{
    public static class MiningRule
    {
        public static bool AcceptsTarget(bool targetOnOwnShip, bool serveMining)
        {
            return targetOnOwnShip || serveMining;
        }

        public static bool InWorldRange(float worldDistance, float rangeTiles, float shipScale)
        {
            if (rangeTiles <= 0f) return false;
            if (shipScale <= 0f) return false;
            if (worldDistance < 0f) return false;
            return worldDistance <= rangeTiles * shipScale;
        }
    }
}
