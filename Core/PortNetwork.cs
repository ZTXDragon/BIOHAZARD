namespace ZTX.BioCirculation.Core
{
    public static class PortNetwork
    {
        public const string CapillarySuffix = "_cap";

        public static bool Carries(string portType, string network)
        {
            if (string.IsNullOrEmpty(portType) || string.IsNullOrEmpty(network)) return false;
            if (portType == network) return true;
            return portType.Length == network.Length + CapillarySuffix.Length
                && portType.StartsWith(network, System.StringComparison.Ordinal)
                && portType.EndsWith(CapillarySuffix, System.StringComparison.Ordinal);
        }
    }
}
