namespace ZTX.BioCirculation.Core
{
    public static class CareerDefeatRule
    {
        public static bool VanillaJunksReducedShip(int assignedCrew, bool hasCommandParts,
                                                   int crewOnShip, int crewCapacity)
        {
            return assignedCrew == 0
                || (!hasCommandParts && crewOnShip == 0 && crewCapacity == 0);
        }

        public static bool JunksReducedShip(int assignedCrew, bool hasCommandParts,
                                            int crewOnShip, int crewCapacity, bool hasBioBrain)
        {
            bool crewless = crewOnShip == 0 && crewCapacity == 0 && hasBioBrain;
            if (!crewless)
                return VanillaJunksReducedShip(assignedCrew, hasCommandParts, crewOnShip, crewCapacity);
            return !hasCommandParts;
        }

        public static bool VanillaDeclaresDefeat(bool gameOverShown, bool allowFreeBuild,
                                                 int shipsWithAssignedCrew, int shipsWithParts,
                                                 bool friendlyShipsElsewhere)
        {
            if (gameOverShown) return false;
            if (shipsWithAssignedCrew > 0) return false;
            if (allowFreeBuild && shipsWithParts > 0) return false;
            return !friendlyShipsElsewhere;
        }

        public static bool ShouldSuppressDefeat(bool gameOverShown, int shipsWithAssignedCrew,
                                                int shipsWithBrains)
        {
            return !gameOverShown && shipsWithAssignedCrew == 0 && shipsWithBrains > 0;
        }
    }
}
