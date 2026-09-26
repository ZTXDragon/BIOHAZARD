using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ZTX.BioCirculation.Core;

namespace ZTX.BioCirculation
{
    // The DLL's global switches.
    public static class Config
    {
        public static float GradientGain       = 0.25f;
        public static bool  Enabled            = true;

        public static bool  HideVanillaWalls   = true;

        public static bool  MirrorBlendArt     = true;

        public static bool  EyesSeeThroughCartilage = true;

        public static bool  TentacleBasePaintOnly = true;

        public static float PressureSpriteFullScale = 100f;

        public static bool  PerfProbe          = false;

        public static float PerfReportSeconds  = 10f;

        public static bool  VerboseLog         = false;

        public static float BiomassShareSeconds = 0.5f;

        private const string GROUP = "Behaviours/";

        public static void Load()
        {
            string path = null;
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Info("config: assembly folder unknown; using defaults. " + Describe());
                    return;
                }
                path = Path.Combine(dir, "config.rules");
                if (!File.Exists(path))
                {
                    Log.Info("config: " + path + " not found; using defaults. " + Describe());
                    return;
                }
                var values = ConfigText.Parse(File.ReadAllText(path));
                int applied = 0;
                var rejected = new List<string>();
                foreach (var kv in values)
                {
                    if (!kv.Key.StartsWith(GROUP, StringComparison.Ordinal)) continue;
                    string key = kv.Key.Substring(GROUP.Length);
                    switch (Apply(key, kv.Value))
                    {
                        case ApplyResult.Applied: applied++; break;
                        case ApplyResult.Unknown: rejected.Add(key + " (unknown key)"); break;
                        default: rejected.Add(key + " = " + kv.Value + " (not a valid value)"); break;
                    }
                }
                Log.Info("config: " + applied + " Behaviours value(s) applied from " + path
                         + (rejected.Count > 0 ? "; IGNORED " + string.Join(", ", rejected) : "")
                         + ". " + Describe());
            }
            catch (Exception ex)
            {
                Log.Exception("Config.Load(" + path + ")", ex);
            }
        }

        private enum ApplyResult { Applied, Unknown, Invalid }

        private static ApplyResult Apply(string key, string raw)
        {
            bool ok;
            switch (key)
            {
                case "Enabled":                 ok = ConfigText.TryGetBool(raw, ref Enabled); break;
                case "GradientGain":            ok = ConfigText.TryGetFloat(raw, ref GradientGain); break;
                case "BiomassShareSeconds":     ok = ConfigText.TryGetFloat(raw, ref BiomassShareSeconds); break;
                case "EyesSeeThroughCartilage": ok = ConfigText.TryGetBool(raw, ref EyesSeeThroughCartilage); break;
                case "PressureSpriteFullScale": ok = ConfigText.TryGetFloat(raw, ref PressureSpriteFullScale); break;
                case "HideVanillaWalls":        ok = ConfigText.TryGetBool(raw, ref HideVanillaWalls); break;
                case "MirrorBlendArt":          ok = ConfigText.TryGetBool(raw, ref MirrorBlendArt); break;
                case "TentacleBasePaintOnly":   ok = ConfigText.TryGetBool(raw, ref TentacleBasePaintOnly); break;
                case "PerfProbe":               ok = ConfigText.TryGetBool(raw, ref PerfProbe); break;
                case "PerfReportSeconds":       ok = ConfigText.TryGetFloat(raw, ref PerfReportSeconds); break;
                case "VerboseLog":              ok = ConfigText.TryGetBool(raw, ref VerboseLog); break;
                default: return ApplyResult.Unknown;
            }
            return ok ? ApplyResult.Applied : ApplyResult.Invalid;
        }

        public static string Describe()
        {
            return "Enabled=" + Enabled
                 + " GradientGain=" + GradientGain
                 + " BiomassShareSeconds=" + BiomassShareSeconds
                 + " EyesSeeThroughCartilage=" + EyesSeeThroughCartilage
                 + " PressureSpriteFullScale=" + PressureSpriteFullScale
                 + " HideVanillaWalls=" + HideVanillaWalls
                 + " MirrorBlendArt=" + MirrorBlendArt
                 + " TentacleBasePaintOnly=" + TentacleBasePaintOnly
                 + " PerfProbe=" + PerfProbe
                 + " PerfReportSeconds=" + PerfReportSeconds
                 + " VerboseLog=" + VerboseLog;
        }
    }
}
