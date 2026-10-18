namespace ZTX.BioCirculation.Core
{
    public static class HealGate
    {
        public static bool MayHeal(bool hasBrain, int salvageDamage)
        {
            return hasBrain && salvageDamage <= 0;
        }
    }
}
