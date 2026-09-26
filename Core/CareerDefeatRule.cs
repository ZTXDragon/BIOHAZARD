namespace ZTX.BioCirculation.Core
{
    // When a CREWLESS fleet loses its career run.
    public static class CareerDefeatRule
    {
        public static bool VanillaJunksReducedShip(int assignedCrew, bool hasCommandParts,
                                                   int crewOnShip, int crewCapacity)
        {
            return assignedCrew == 0
                || (!hasCommandParts && crewOnShip == 0 && crewCapacity == 0);
        }

        public static bool JunksReducedShip(int assignedCrew, bool hasCommandParts,
                                            int crewOnShip, int crewCapacity)
        {
            bool crewless = crewOnShip == 0 && crewCapacity == 0;
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
