namespace ZTX.BioCirculation.Core
{
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
                                         bool hasCommandParts, int crewOnShip, int crewCapacity,
                                         bool hasBioBrain)
        {
            if (vanillaResult) return true;
            if (hasSpecialProvider) return false;
            bool crewless = crewOnShip == 0 && crewCapacity == 0 && hasBioBrain;
            return crewless && hasCommandParts;
        }
    }
}
