namespace ZTX.BioCirculation.Core
{
    public static class ConstructionStock
    {
        public static int Unreachable(int stored, int spendable)
        {
            int left = stored - spendable;
            return left > 0 ? left : 0;
        }
    }
}
