using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace FishSlapper
{
    public class ModConfig
    {
        public KeybindList SlapKey { get; set; } = KeybindList.Parse("MouseRight, Space");
        public KeybindList DiveSlapKey { get; set; } = KeybindList.Parse("Q");
        public bool HideKeyPrompts { get; set; } = false;
        public bool EnableMobileButton { get; set; } = false;
    }
}
