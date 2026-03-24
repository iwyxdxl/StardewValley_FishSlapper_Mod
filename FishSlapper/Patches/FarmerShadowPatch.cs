using HarmonyLib;
using StardewValley;
using FishSlapper.Gameplay;

namespace FishSlapper.Patches
{
    internal static class FarmerShadowPatch
    {
        private static DiveSlapController? controller;

        public static void Initialize(DiveSlapController controller)
        {
            FarmerShadowPatch.controller = controller;
        }

        public static void Apply(Harmony harmony)
        {
            var farmerShadow = AccessTools.DeclaredMethod(typeof(Farmer), nameof(Farmer.DrawShadow), new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            if (farmerShadow is not null)
                harmony.Patch(farmerShadow, prefix: new HarmonyMethod(typeof(FarmerShadowPatch), nameof(PrefixFarmerDrawShadow)));

            var characterShadow = AccessTools.DeclaredMethod(typeof(Character), nameof(Character.DrawShadow), new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            if (characterShadow is not null)
                harmony.Patch(characterShadow, prefix: new HarmonyMethod(typeof(FarmerShadowPatch), nameof(PrefixCharacterDrawShadow)));
        }

        private static bool PrefixFarmerDrawShadow(Farmer __instance)
        {
            if (FarmerShadowPatch.controller is null)
                return true;

            return !FarmerShadowPatch.controller.ShouldSuppressFarmerShadow(__instance);
        }

        private static bool PrefixCharacterDrawShadow(Character __instance)
        {
            if (FarmerShadowPatch.controller is null || __instance is not Farmer farmer)
                return true;

            return !FarmerShadowPatch.controller.ShouldSuppressFarmerShadow(farmer);
        }
    }
}
