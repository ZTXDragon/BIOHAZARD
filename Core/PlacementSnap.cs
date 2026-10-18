namespace ZTX.BioCirculation.Core
{
    public enum ScreenSide { Right = 0, Bottom = 1, Left = 2, Top = 3 }

    public static class PlacementSnap
    {
        public static ScreenSide ArterySide(bool arteryDrawnRight, int rotation, bool mirrored)
        {
            int local = (arteryDrawnRight != mirrored) ? (int)ScreenSide.Right : (int)ScreenSide.Left;
            return (ScreenSide)((local + (rotation & 3)) % 4);
        }

        public static bool Conventional(ScreenSide arterySide)
        {
            return arterySide == ScreenSide.Left || arterySide == ScreenSide.Bottom;
        }

        public static bool MirrorAt(bool arteryDrawnRight, int rotation)
        {
            return !Conventional(ArterySide(arteryDrawnRight, rotation, false));
        }
    }
}
