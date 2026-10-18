namespace ZTX.BioCirculation.Core
{
    public static class HealWant
    {
        public static int Units(int missing, int credit, int bank, int room, int hpPerUnit)
        {
            if (room <= 0 || missing <= 0) return 0;
            if (hpPerUnit <= 0) return room;
            int uncovered = missing - (credit > 0 ? credit : 0);
            if (uncovered <= 0) return 0;
            int units = (uncovered + hpPerUnit - 1) / hpPerUnit - (bank > 0 ? bank : 0);
            if (units <= 0) return 0;
            return units < room ? units : room;
        }
    }
}
