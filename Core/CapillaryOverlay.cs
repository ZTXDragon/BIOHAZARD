namespace ZTX.BioCirculation.Core
{
    public static class CapillaryOverlay
    {
        public static void Lateral(bool facesUpDown, bool artery, float offset, out float dx, out float dy)
        {
            if (facesUpDown)
            {
                dx = artery ? -offset : offset;
                dy = 0f;
            }
            else
            {
                dx = 0f;
                dy = artery ? offset : -offset;
            }
        }
    }
}
