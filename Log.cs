using System;
using Halfling.Logging;

namespace ZTX.BioCirculation
{
    public static class Log
    {
        public const string VERSION = "ZTX.BioCirculation v1.0.0";
        private const string P = "[ZTX.Bio] ";
        public static void Info(string m)  => Logger.Log(P + m);

        public static void Verbose(string m)
        {
            if (Config.VerboseLog) Logger.Log(P + m);
        }
        public static void Error(string m) => Logger.Log(P + "ERROR " + m);
        public static void Exception(string where, Exception ex) =>
            Logger.Log(P + "EXCEPTION in " + where + ": " + ex.GetType().Name + " " + ex.Message + "\n" + ex.StackTrace);
    }
}
