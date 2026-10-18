namespace ZTX.BioCirculation.Core
{
    public enum WallNeighbour { Empty, Bio, Other }

    public static class BioWallSide
    {
        public static bool Hide(WallNeighbour neighbour, bool bioLink)
        {
            return neighbour == WallNeighbour.Bio
                || (neighbour == WallNeighbour.Empty && bioLink);
        }
    }
}
