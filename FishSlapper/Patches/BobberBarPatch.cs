using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.Menus;
using FishSlapper.Gameplay;

namespace FishSlapper.Patches
{
    internal static class BobberBarPatch
    {
        private static DiveSlapController? controller;

        public static void Initialize(DiveSlapController controller)
        {
            BobberBarPatch.controller = controller;
        }

        public static void Apply(Harmony harmony)
        {
            var updateOriginal = AccessTools.DeclaredMethod(typeof(BobberBar), nameof(BobberBar.update));
            var drawOriginal = AccessTools.DeclaredMethod(typeof(BobberBar), nameof(BobberBar.draw), new[] { typeof(SpriteBatch) });
            if (updateOriginal is null || drawOriginal is null)
                return;

            harmony.Patch(updateOriginal, prefix: new HarmonyMethod(typeof(BobberBarPatch), nameof(PrefixUpdate)));
            harmony.Patch(drawOriginal, prefix: new HarmonyMethod(typeof(BobberBarPatch), nameof(PrefixDraw)));
        }

        private static bool PrefixUpdate(BobberBar __instance)
        {
            return BobberBarPatch.controller?.ShouldFreezeBobberBarUpdate(__instance) != true;
        }

        private static bool PrefixDraw(BobberBar __instance, SpriteBatch b)
        {
            return BobberBarPatch.controller?.ShouldSuppressBobberBarDraw(__instance) != true;
        }
    }
}
