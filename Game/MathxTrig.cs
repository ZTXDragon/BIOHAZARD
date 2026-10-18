using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation.Game
{
    internal sealed class MathxTrig : ITrig
    {
        public static readonly MathxTrig Instance = new MathxTrig();

        private MathxTrig() { }

        public double Sin(double v) => Halfling.Mathx.Sin(v);
        public double Cos(double v) => Halfling.Mathx.Cos(v);
        public double Log(double v) => Halfling.Mathx.Log(v);
        public double Pow(double v, double exponent) => Halfling.Mathx.Pow(v, exponent);
    }
}
