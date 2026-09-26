namespace ZTX.BioCirculation.Core
{
    // Whether a ship is "significant" - the engine's word for "a live ship rather than debris".
    public static class SignificanceRule
    {
        public static bool VanillaIsSignificant(bool? specialProvider, bool hasCommandParts,
                                                int assignedCrew, bool constructionQueued)
        {
            if (specialProvider.HasValue) return specialProvider.Value;
            if (hasCommandParts && assignedCrew > 0) return true;
            return constructionQueued;
        }

        public static bool IsSignificant(bool vanillaResult, bool hasSpecialProvider,
                                         bool hasCommandParts, int crewOnShip, int crewCapacity)
        {
            if (vanillaResult) return true;
            if (hasSpecialProvider) return false;
            bool crewless = crewOnShip == 0 && crewCapacity == 0;
            return crewless && hasCommandParts;
        }
    }
}
