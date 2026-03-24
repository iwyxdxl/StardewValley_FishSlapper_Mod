using System.Collections.Generic;
using System.IO;
using FishSlapper.Gameplay;
using FishSlapper.Patches;
using FishSlapper.Rendering;
using FishSlapper.Vanilla;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace FishSlapper
{
    public class ModEntry : Mod
    {
        private ModConfig Config = null!;
        private DiveSlapController Controller = null!;
        private DiveSlapRenderer Renderer = null!;
        private readonly PerScreen<DiveSlapRenderer.MobileActionButtonsLayout> cachedMobileLayout = new();
        private readonly PerScreen<bool> cachedMobileLayoutUiScaled = new();

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.Renderer = new DiveSlapRenderer();
            this.Controller = new DiveSlapController(
                helper,
                this.Monitor,
                this.ModManifest.UniqueID,
                this.Config,
                this.Renderer,
                new VanillaFishingBridge()
            );

            var harmony = new Harmony(this.ModManifest.UniqueID);
            BobberBarPatch.Initialize(this.Controller);
            FarmerDrawPatch.Initialize(this.Controller);
            FarmerShadowPatch.Initialize(this.Controller);
            Game1DrawToolPatch.Initialize(this.Controller);
            FishingRodDrawPatch.Initialize(this.Controller);
            BobberBarPatch.Apply(harmony);
            FarmerDrawPatch.Apply(harmony);
            FarmerShadowPatch.Apply(harmony);
            Game1DrawToolPatch.Apply(harmony);
            FishingRodDrawPatch.Apply(harmony);

            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Display.RenderingWorld += this.OnRenderingWorld;
            helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
            helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
            helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () =>
                {
                    this.Config = new ModConfig();
                    this.Controller.UpdateConfig(this.Config);
                },
                save: () =>
                {
                    this.Helper.WriteConfig(this.Config);
                    this.Controller.UpdateConfig(this.Config);
                }
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.Config.HideKeyPrompts,
                setValue: value => this.Config.HideKeyPrompts = value,
                name: () => this.Helper.Translation.Get("config.hide-key-prompts.name"),
                tooltip: () => this.Helper.Translation.Get("config.hide-key-prompts.tooltip")
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                getValue: () => this.Config.EnableMobileButton,
                setValue: value => this.Config.EnableMobileButton = value,
                name: () => this.Helper.Translation.Get("config.mobile-button.name"),
                tooltip: () => this.Helper.Translation.Get("config.mobile-button.tooltip")
            );

            configMenu.AddKeybindList(
                mod: this.ModManifest,
                getValue: () => this.Config.SlapKey,
                setValue: value => this.Config.SlapKey = value,
                name: () => this.Helper.Translation.Get("config.slap-key.name"),
                tooltip: () => this.Helper.Translation.Get("config.slap-key.tooltip")
            );

            configMenu.AddKeybindList(
                mod: this.ModManifest,
                getValue: () => this.Config.DiveSlapKey,
                setValue: value => this.Config.DiveSlapKey = value,
                name: () => this.Helper.Translation.Get("config.dive-slap-key.name"),
                tooltip: () => this.Helper.Translation.Get("config.dive-slap-key.tooltip")
            );
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/AudioChanges"))
                return;

            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, StardewValley.GameData.AudioCueData>().Data;
                string slapAudioFilePath = Path.Combine(this.Helper.DirectoryPath, "assets", "slap.wav");
                string playerDiveAudioFilePath = Path.Combine(this.Helper.DirectoryPath, "assets", "PlayerDive.wav");

                data[ModConstants.SlapSoundId] = new StardewValley.GameData.AudioCueData
                {
                    Id = ModConstants.SlapSoundId,
                    FilePaths = new List<string> { slapAudioFilePath },
                    Category = "Sound"
                };

                data[ModConstants.PlayerDiveSoundId] = new StardewValley.GameData.AudioCueData
                {
                    Id = ModConstants.PlayerDiveSoundId,
                    FilePaths = new List<string> { playerDiveAudioFilePath },
                    Category = "Sound"
                };
            });
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (this.TryHandleMobileActionButtonPress(e))
                return;

            this.Controller.OnButtonPressed(e);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            this.Controller.OnUpdateTicked(e.Ticks);
        }

        private void OnRenderingWorld(object? sender, RenderingWorldEventArgs e)
        {
            this.Renderer.OnRenderingWorld(this.Controller.ActiveSession);
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            string? slapKey = this.Config.HideKeyPrompts ? null : this.Controller.GetSlapKeyHint();
            string? slapPrompt = slapKey is not null
                ? this.Helper.Translation.Get("hud.slap-prompt", new { key = slapKey }).ToString()
                : null;
            this.Renderer.OnRenderedWorld(e, this.Controller.ActiveSession, this.Controller.GetObservedDiveStatesForCurrentScreen(), slapPrompt);

            if (Game1.activeClickableMenu is null)
                this.DrawMobileActionButtons(e.SpriteBatch, uiScaled: false);
        }

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            string? keyHint = this.Config.HideKeyPrompts ? null : this.Controller.GetDiveSlapKeyHint();
            string? promptText = keyHint is not null
                ? this.Helper.Translation.Get("hud.dive-slap-prompt", new { key = keyHint }).ToString()
                : null;
            this.Renderer.OnRenderedActiveMenu(e, this.Controller.ActiveSession, promptText);

            if (Game1.activeClickableMenu is BobberBar)
                this.DrawMobileActionButtons(e.SpriteBatch, uiScaled: true);
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            this.Controller.OnMenuChanged(e);
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            this.Controller.OnModMessageReceived(e);
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            this.Controller.OnPeerDisconnected(e);
        }

        private void DrawMobileActionButtons(SpriteBatch spriteBatch, bool uiScaled)
        {
            var layout = this.GetMobileActionButtonsLayout(uiScaled);
            this.cachedMobileLayout.Value = layout;
            this.cachedMobileLayoutUiScaled.Value = uiScaled;

            if (!layout.HasAnyButton)
                return;

            string diveLabel = this.Helper.Translation.Get("hud.mobile-dive-button").ToString();
            string slapLabel = this.Helper.Translation.Get("hud.mobile-slap-button").ToString();
            this.Renderer.DrawMobileActionButtons(spriteBatch, layout, diveLabel, slapLabel);
        }

        private DiveSlapRenderer.MobileActionButtonsLayout GetMobileActionButtonsLayout(bool uiScaled)
        {
            bool showButtons = this.Config.EnableMobileButton;
            bool showDiveButton = showButtons && this.Controller.CanUseMobileDiveButton();
            bool showSlapButton = showButtons && this.Controller.CanUseMobileSlapButton();
            return this.Renderer.GetMobileActionButtonsLayout(
                this.Controller.ActiveSession,
                uiScaled,
                showDiveButton,
                showSlapButton
            );
        }

        private bool TryHandleMobileActionButtonPress(ButtonPressedEventArgs e)
        {
            if (e.Button != SButton.MouseLeft)
                return false;

            // 使用上一帧绘制时缓存的 layout，保证 bounds 与玩家看到的按钮位置一致。
            // 手机端 pinch-zoom 会在帧间持续改变 viewport，
            // 如果此处重新计算 layout，位置可能与已绘制的按钮产生偏差。
            var layout = this.cachedMobileLayout.Value;
            if (!layout.HasAnyButton)
                return false;

            // 使用 Game1.getMouseX/Y 而非 ScreenPixels：
            // 前者基于 getMouseXRaw() 并除以 SpriteBatch 对应的缩放系数，
            // 在所有平台（PC / Android / iOS）上都与绘制坐标空间一致。
            bool uiScaled = this.cachedMobileLayoutUiScaled.Value;
            int cursorX = Game1.getMouseX(ui_scale: uiScaled);
            int cursorY = Game1.getMouseY(ui_scale: uiScaled);

            if (layout.HasDiveButton && layout.DiveButtonBounds.Contains(cursorX, cursorY))
            {
                this.Helper.Input.Suppress(e.Button);
                this.Controller.TryUseMobileDiveButton();
                return true;
            }

            if (layout.HasSlapButton && layout.SlapButtonBounds.Contains(cursorX, cursorY))
            {
                this.Helper.Input.Suppress(e.Button);
                this.Controller.TryUseMobileSlapButton();
                return true;
            }

            return false;
        }

    }
}
