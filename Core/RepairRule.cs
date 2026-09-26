namespace ZTX.BioCirculation.Core
{
    // When an organ may perform ORDINARY repair - the material-paid repair, as opposed to the blood
    // heal.
    public static class RepairRule
    {
        public static bool CanRepairNow(bool requiresStationary, float speedSquared,
                                        float stationarySpeed)
        {
            if (!requiresStationary) return true;
            if (stationarySpeed < 0f) return false;
            if (speedSquared < 0f) return false;
            return speedSquared <= stationarySpeed * stationarySpeed;
        }
    }
}
