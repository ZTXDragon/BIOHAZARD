using Cosmoteer.Data;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Graphics;
using Cosmoteer.Ships.Rendering;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;

namespace ZTX.BioCirculation.Game
{
    [HarmonyPatch(typeof(PartToggledBlendSprites), "CreateQuad")]
    internal static class ZtxMirrorBlendArtPatch
    {
        private static readonly ID<PartCategory> MirrorCategory =
            new ID<PartCategory>("ztx_mirror_art");

        private static bool Prefix(PartToggledBlendSprites __instance)
        {
            if (!Config.MirrorBlendArt)
            {
                return true;
            }

            Part part = __instance.Part;
            if (part == null || !part.FlipX)
            {
                return true;
            }

            PartRules rules = part.Rules;
            if (rules?.TypeCategories == null || !rules.TypeCategories.Contains(MirrorCategory))
            {
                return true;
            }

            var layer = __instance.Rules.Layer;
            __instance._quad = new ManagedShipQuad(
                __instance.Ship.Renderer.GetLayerQuads(layer),
                null,
                0,
                flipU: true,
                __instance.ShipLocation,
                Vector2.Zero,
                (Direction)__instance.ShipAngle,
                __instance.Ship.WorldUniformScale,
                __instance._getColorFrom?.Color ?? Color.White,
                __instance.Ship.Rules.RenderLayers[layer].Inflate,
                1f,
                part.Sim?.Clock);
            return false;
        }
    }
}
