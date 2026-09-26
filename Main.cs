using System;
using System.Reflection;
using HarmonyLib;

namespace ZTX.BioCirculation
{
    public static class Main
    {
        private static bool s_initialized;
        public static void AssemblyLoadInitializer() => RunInitOnce();
        public static void InitializePatches() => RunInitOnce();

        private static void RunInitOnce()
        {
            if (s_initialized) return;
            s_initialized = true;
            try
            {
                Config.Load();
                var harmony = new Harmony("ztx.biocirculation");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("init OK. " + Log.VERSION + " | .NET " + Environment.Version);
                // After PatchAll and outside it: the probe patches each hook on its own, so a
                // hook that fails cannot take the mod's real patches down with it.
                if (Config.PerfProbe) Game.ZtxPerfProbe.Install(harmony);
            }
            catch (Exception ex) { Log.Exception("Main.RunInitOnce", ex); }
        }
    }
}
