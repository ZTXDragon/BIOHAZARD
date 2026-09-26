namespace ZTX.BioCirculation.Core
{
    // The decisions behind venting cargo from a crewless ship.
    public static class EjectRule
    {
        public static bool IsVentRequest(object source, object sink)
        {
            return source != null && ReferenceEquals(source, sink);
        }

        public static int VentAmount(int requested, int stored, int anticipatedPickup)
        {
            if (requested <= 0) return 0;
            int free = stored - anticipatedPickup;
            if (free <= 0) return 0;
            return requested < free ? requested : free;
        }

        public static bool ShowFailure(int requested, int ejected)
        {
            return requested > 0 && ejected <= 0;
        }
    }
}
