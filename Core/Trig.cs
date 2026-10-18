using System;

namespace ZTX.BioCirculation.Core
{
    public interface ITrig
    {
        double Sin(double v);
        double Cos(double v);
        double Log(double v);
        double Pow(double v, double exponent);
    }

    public sealed class SystemTrig : ITrig
    {
        public static readonly SystemTrig Instance = new SystemTrig();

        private SystemTrig() { }

        public double Sin(double v) => Math.Sin(v);
        public double Cos(double v) => Math.Cos(v);
        public double Log(double v) => Math.Log(v);
        public double Pow(double v, double exponent) => Math.Pow(v, exponent);
    }
}
