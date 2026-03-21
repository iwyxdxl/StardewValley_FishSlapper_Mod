using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using FishSlapper.Gameplay;

namespace FishSlapper.Rendering
{
    internal sealed class DiveSlapRenderer
    {
        // 原玩法“鱼拿在手上时扇鱼”用的出拳帧，只影响本体临时渲染。
        private const int CaughtFishPunchFrame = 278;

        // 跳水玩法里真正会用到的动作帧：
        // 1. 水中扇鱼时的左右出拳帧
        // 2. 起跳/飞行/回岸时的四向移动帧
        // 3. 水中待机或强制复位时的四向站立帧
        private const int DiveSlapPunchRightFrame = 274;
        private const int DiveSlapPunchLeftFrame = 278;
        // 准备阶段单独用 walk 组；真正的入水/出水位移继续用 carryRun 组。
        private const int DiveSlapWindupDownFrame = 0;
        private const int DiveSlapWindupRightFrame = 8;
        private const int DiveSlapWindupUpFrame = 16;
        private const int DiveSlapWindupLeftFrame = 24;
        private const int DiveSlapMoveDownFrame = 128;
        private const int DiveSlapMoveRightFrame = 136;
        private const int DiveSlapMoveUpFrame = 144;
        private const int DiveSlapMoveLeftFrame = 152;
        private const int DiveSuccessHoldDownFrame = 96;
        // 原版 FarmerSprite 没有独立的 standDown 常量，静止朝下就是 frame 0。
        private const int FarmerIdleDownFrame = 0;
        private const int FarmerStandDownFrame = 0;
        private const int FarmerStandRightFrame = 8;
        private const int FarmerStandUpFrame = 16;
        private const int FarmerStandLeftFrame = 24;
        private const float CaughtFishHeldBaseYOffset = -36f;
        private const float CaughtFishMaxHorizontalTwitchVelocity = 0.8f;
        private const float CaughtFishInitialJumpVelocity = -11.4f;
        private const float CaughtFishBounceJumpVelocity = -4.2f;
        private const int CaughtFishStandResetTicks = 5;

        private const int CaughtFishSlapDurationTicks = 30;
        private const int DiveHitAnimationDurationTicks = 10;
        private const float FailRetaliationImpactProgress = 0.52f;
        private const float SlapFishScale = 4f;
        private const float SlapFishIdleBobAmplitude = 2.5f;
        private const float DiveSuccessHeldFishScale = 4f;
        private const float FailRetaliationFishScale = 4f;

        private const float HudBarWidth = 120f;
        private const float HudHitBarHeight = 11f;
        private const float HudTimeBarHeight = 8f;
        private const float HudBarGap = 3f;
        private const float HudBorderSize = 2f;
        private const float HudAboveFeetOffset = 160f;
        private const float HudSegmentGap = 2f;
        private const float HudTextScale = 1.1f;
        private const float PromptPadX = 10f;
        private const float PromptPadY = 5f;
        private const float PromptBelowFeetOffset = 48f;
        private const float MobileButtonScale = 4f;
        private const float MobileButtonOffsetX = 196f;
        private const float MobileButtonCenterYOffset = -22f;
        private const float MobileButtonMargin = 16f;
        private const int SwimShadowFrameSize = 16;
        private const int SwimShadowFrameCount = 10;
        private const int SwimShadowFrameDurationMs = 70;
        private const float SwimShadowScale = 4f;
        // MobileAtlas_manually_made 图集中圆角正方形按钮底板的 sprite 区域
        private static readonly Rectangle MobileActionButtonSourceRect = new(95, 131, 34, 34);

        private static readonly Color HudBorderColor = new(40, 30, 20, 230);
        private static readonly Color HudHitFilledColor = new(255, 200, 50);
        private static readonly Color HudHitEmptyColor = new(60, 50, 40, 130);
        private static readonly Color HudTimeBarBgColor = new(30, 25, 20, 150);
        private static readonly Color HudTimeGreenColor = new(80, 220, 80);
        private static readonly Color HudTimeYellowColor = new(240, 220, 50);
        private static readonly Color HudTimeRedColor = new(230, 60, 50);
        private static readonly Color HudTextShadowColor = new(20, 15, 10, 180);
        private static readonly Color MobileButtonTextColor = new(74, 48, 28);

        private readonly List<BurstParticle> burstParticles = new();
        private readonly Random rng = new();
        private Texture2D? pixelTexture;
        private Texture2D? mobileAtlasTexture;
        private Texture2D? swimShadowTexture;
        private bool attemptedMobileAtlasLoad;
        private int caughtFishSlapTick = -1;
        private int swimShadowFrame;
        private int swimShadowTimer = SwimShadowFrameDurationMs;
        private float fishTwitchOffsetX;
        private float fishTwitchOffsetY;
        private float fishTwitchRotation;
        private float fishTwitchRotationVelocity;
        private float fishTwitchVelocityX;
        private float fishTwitchVelocityY;
        private int fishTwitchBouncesRemaining;
        private int caughtFishStandFrameTicksRemaining;
        private int localPoseResetTicks;
        private int localPoseResetFacingDirection = 2;
        private bool hideCaughtFishPreview;
        private Farmer? diveRenderFarmer;
        private Farmer? toolSuppressedFarmer;

        private sealed class BurstParticle
        {
            public Vector2 WorldPos;
            public Vector2 Velocity;
            public float Alpha;
            public float AlphaDecay;
            public float Width;
            public float Height;
            public float Rotation;
            public float RotationSpeed;
            public Color Color;
            public float Gravity;
        }

        internal readonly record struct MobileActionButtonsLayout(
            bool HasDiveButton,
            Rectangle DiveButtonBounds,
            bool HasSlapButton,
            Rectangle SlapButtonBounds
        )
        {
            public bool HasAnyButton => this.HasDiveButton || this.HasSlapButton;
        }

        public bool ShouldHideCaughtFishToolPreview => this.hideCaughtFishPreview;

        public bool ShouldSuppressToolDraw(Farmer farmer)
        {
            return ReferenceEquals(farmer, this.toolSuppressedFarmer)
                || (ReferenceEquals(farmer, Game1.player) && this.localPoseResetTicks > 0 && this.caughtFishSlapTick < 0 && !Game1.player.UsingTool);
        }

        public void ResetLocalPlayerPose(int facingDirection)
        {
            this.localPoseResetFacingDirection = facingDirection;
            this.localPoseResetTicks = 4;
            Game1.player.faceDirection(facingDirection);
            this.ApplyPose(Game1.player, GetStandingFrame(facingDirection));
        }

        public int DiveHitTickDuration => DiveHitAnimationDurationTicks;

        public void PlayCaughtFishSlap()
        {
            Game1.playSound(ModConstants.SlapSoundId);
            Game1.player.jump(4f);
            this.caughtFishStandFrameTicksRemaining = this.caughtFishSlapTick >= 0 ? CaughtFishStandResetTicks : 0;
            this.caughtFishSlapTick = 8;
            this.fishTwitchOffsetX = 0f;
            this.fishTwitchOffsetY = 0f;
            this.fishTwitchRotation = 0f;
            this.fishTwitchVelocityX = (float)(this.rng.NextDouble() * (CaughtFishMaxHorizontalTwitchVelocity * 2f) - CaughtFishMaxHorizontalTwitchVelocity);
            this.fishTwitchVelocityY = CaughtFishInitialJumpVelocity;
            this.fishTwitchRotationVelocity = this.fishTwitchVelocityX * 0.065f;
            this.fishTwitchBouncesRemaining = 1;
            this.SpawnBurstParticles(Game1.player.Position + new Vector2(-16f, -64f));
        }

        public void PlayDiveSlap(DiveSlapSession session, Vector2 impactWorldPos)
        {
            Game1.playSound(ModConstants.SlapSoundId);
            this.StartDiveSlapFishJump(session);
            this.SpawnBurstParticles(impactWorldPos);
            this.SpawnSlapWaterDroplets(impactWorldPos);
        }

        public void PlayDiveWaterEntry(Vector2 splashWorldPos)
        {
            Game1.playSound(ModConstants.DiveWaterEntrySoundId);
            this.SpawnPlayerDiveSplash(splashWorldPos);
        }

        public void PlayDiveWaterExit(Vector2 splashWorldPos)
        {
            Game1.playSound(ModConstants.DiveWaterExitSoundId);
            this.SpawnPlayerExitSplash(splashWorldPos);
        }

        public void PlayDiveJump()
        {
            Game1.playSound(ModConstants.DiveJumpSoundId);
        }

        public void PlayDiveRetaliationLaunch(Vector2 launchWorldPos)
        {
            this.PlayFishWaterSplash(launchWorldPos);
        }

        public void PlayDiveRetaliationImpact(Vector2 impactWorldPos)
        {
            Game1.playSound(ModConstants.SlapSoundId);
            this.SpawnRetaliationImpactParticles(impactWorldPos);
        }

        public void PlayDiveRetaliationSplashdown(Vector2 splashWorldPos)
        {
            this.PlayFishWaterSplash(splashWorldPos);
        }

        public void OnUpdateTicked(DiveSlapSession? session)
        {
            this.UpdateSwimShadowAnimation(session);

            if (this.caughtFishSlapTick >= 0)
            {
                this.caughtFishSlapTick++;
                if (this.caughtFishSlapTick > CaughtFishSlapDurationTicks)
                    this.caughtFishSlapTick = -1;
            }

            if (
                this.fishTwitchVelocityX != 0f
                || this.fishTwitchVelocityY != 0f
                || this.fishTwitchOffsetX != 0f
                || this.fishTwitchOffsetY < 0f
                || this.fishTwitchRotation != 0f
                || this.fishTwitchRotationVelocity != 0f
            )
            {
                this.fishTwitchOffsetX += this.fishTwitchVelocityX;
                this.fishTwitchOffsetY += this.fishTwitchVelocityY;
                this.fishTwitchRotation += this.fishTwitchRotationVelocity;
                this.fishTwitchVelocityX *= 0.72f;
                this.fishTwitchVelocityY += 1.1f;
                this.fishTwitchRotationVelocity *= 0.78f;
                if (this.fishTwitchOffsetY >= 0f)
                {
                    this.fishTwitchOffsetY = 0f;
                    if (this.fishTwitchBouncesRemaining > 0)
                    {
                        this.fishTwitchVelocityY = CaughtFishBounceJumpVelocity;
                        this.fishTwitchRotationVelocity = -this.fishTwitchRotationVelocity * 0.6f;
                        this.fishTwitchBouncesRemaining--;
                    }
                    else
                    {
                        this.fishTwitchVelocityY = 0f;
                    }
                }

                if (MathF.Abs(this.fishTwitchOffsetX) < 0.05f && MathF.Abs(this.fishTwitchVelocityX) < 0.05f)
                {
                    this.fishTwitchOffsetX = 0f;
                    this.fishTwitchVelocityX = 0f;
                }

                if (MathF.Abs(this.fishTwitchRotation) < 0.005f && MathF.Abs(this.fishTwitchRotationVelocity) < 0.005f)
                {
                    this.fishTwitchRotation = 0f;
                    this.fishTwitchRotationVelocity = 0f;
                }
            }

            if (this.localPoseResetTicks > 0)
            {
                if (Game1.player.UsingTool)
                    this.localPoseResetTicks = 0;
                else
                    this.localPoseResetTicks--;
            }

            if (session is not null && session.SlapAnimationTicksRemaining > 0)
                session.SlapAnimationTicksRemaining--;

            if (session is not null)
                this.UpdateDiveSlapFish(session);

            foreach (var particle in this.burstParticles)
            {
                particle.WorldPos += particle.Velocity;
                particle.Velocity *= 0.91f;
                particle.Velocity.Y += particle.Gravity;
                particle.Alpha -= particle.AlphaDecay;
                particle.Rotation += particle.RotationSpeed;
            }

            this.burstParticles.RemoveAll(p => p.Alpha <= 0f);
        }

        public void OnRenderingWorld(DiveSlapSession? session)
        {
            if (this.caughtFishSlapTick >= 0)
            {
                this.hideCaughtFishPreview = true;
                if (this.caughtFishStandFrameTicksRemaining > 0)
                {
                    this.caughtFishStandFrameTicksRemaining--;
                    Game1.player.FarmerSprite.setCurrentFrame(GetStandingFrame(Game1.player.FacingDirection));
                    return;
                }

                // 这里故意不清原版“手里举鱼”的状态，只临时覆写一帧出拳姿势。
                // 如果把动画栈整个清掉，会把老玩法里“拿着鱼无限扇”的行为打断。
                Game1.player.FarmerSprite.setCurrentFrame(CaughtFishPunchFrame);
                return;
            }

            this.hideCaughtFishPreview = false;

            if (this.localPoseResetTicks > 0)
            {
                if (Game1.player.UsingTool)
                    this.localPoseResetTicks = 0;
                else
                    this.ApplyPose(Game1.player, GetStandingFrame(this.localPoseResetFacingDirection));
            }
        }

        public bool TryDrawDiveSession(SpriteBatch spriteBatch, DiveSlapSession? session)
        {
            if (session is null)
            {
                this.diveRenderFarmer = null;
                return false;
            }

            this.DrawDiveSession(spriteBatch, session);
            return true;
        }

        public bool TryDrawCaughtFishPreview(SpriteBatch spriteBatch, Farmer farmer, StardewValley.Tools.FishingRod rod)
        {
            if (this.caughtFishSlapTick < 0 || !rod.fishCaught || rod.whichFish is null || rod.whichFish.TypeIdentifier != "(O)")
                return false;

            Farmer drawFarmer = rod.lastUser ?? farmer;
            this.DrawCaughtFishPreview(spriteBatch, drawFarmer, rod);
            return true;
        }

        public void OnRenderedWorld(RenderedWorldEventArgs e, DiveSlapSession? session, string? slapPrompt)
        {
            if (session is null)
                this.diveRenderFarmer = null;

            if (session is not null && session.State == DiveSlapState.Slapping)
                this.DrawDiveSlapFish(e.SpriteBatch, session);

            if (session is not null && IsDiveSuccessHeldFishVisible(session))
                this.DrawDiveSuccessHeldFish(e.SpriteBatch, session);

            if (session is not null && session.State == DiveSlapState.ResolveFail)
                this.DrawFailRetaliationFish(e.SpriteBatch, session);

            if (this.pixelTexture is not null)
            {
                foreach (var particle in this.burstParticles)
                {
                    Vector2 particleScreen = Game1.GlobalToLocal(Game1.viewport, particle.WorldPos);
                    e.SpriteBatch.Draw(
                        this.pixelTexture,
                        particleScreen,
                        sourceRectangle: null,
                        color: particle.Color * particle.Alpha,
                        rotation: particle.Rotation,
                        origin: new Vector2(0.5f, 0.5f),
                        scale: new Vector2(particle.Width, particle.Height),
                        effects: SpriteEffects.None,
                        layerDepth: 1f
                    );
                }
            }

            if (session is not null && session.State == DiveSlapState.Slapping)
            {
                this.DrawSlapProgressHud(e.SpriteBatch, session);
                if (slapPrompt is not null)
                {
                    Vector2 sp = Game1.GlobalToLocal(Game1.viewport, session.RenderPosition);
                    float cx = sp.X + 32f;
                    this.DrawPromptBox(e.SpriteBatch, cx, sp.Y + PromptBelowFeetOffset, slapPrompt);
                }
            }
            else if (session is null && slapPrompt is not null)
            {
                Vector2 sp = Game1.GlobalToLocal(Game1.viewport, Game1.player.Position);
                float cx = sp.X + 32f;
                this.DrawPromptBox(e.SpriteBatch, cx, sp.Y + PromptBelowFeetOffset, slapPrompt);
            }

            this.hideCaughtFishPreview = false;
        }

        public void OnRenderedActiveMenu(RenderedActiveMenuEventArgs e, DiveSlapSession? session, string? diveSlapPrompt)
        {
            if (diveSlapPrompt is not null)
                this.DrawDiveSlapPrompt(e.SpriteBatch, diveSlapPrompt);
        }

        public MobileActionButtonsLayout GetMobileActionButtonsLayout(
            DiveSlapSession? session,
            bool uiScaled,
            bool showDiveButton,
            bool showSlapButton
        )
        {
            int buttonCount = (showDiveButton ? 1 : 0) + (showSlapButton ? 1 : 0);
            if (buttonCount <= 0)
                return default;

            float buttonSize = MobileActionButtonSourceRect.Width * MobileButtonScale;
            float totalHeight = buttonCount * buttonSize;
            Vector2 anchorScreenPos = this.GetMobileButtonsAnchorScreenPosition(session, uiScaled);
            float left = anchorScreenPos.X + MobileButtonOffsetX;
            float top = anchorScreenPos.Y + MobileButtonCenterYOffset - totalHeight / 2f;

            // clamp 必须使用与按钮 bounds 同一坐标空间的视口尺寸：
            // uiScaled → UI SpriteBatch 空间 → uiViewport；否则 → 世界 SpriteBatch 空间 → viewport。
            int viewportW = uiScaled ? Game1.uiViewport.Width : Game1.viewport.Width;
            int viewportH = uiScaled ? Game1.uiViewport.Height : Game1.viewport.Height;
            left = MathHelper.Clamp(left, MobileButtonMargin, viewportW - buttonSize - MobileButtonMargin);
            top = MathHelper.Clamp(top, MobileButtonMargin, viewportH - totalHeight - MobileButtonMargin);

            Rectangle diveBounds = Rectangle.Empty;
            Rectangle slapBounds = Rectangle.Empty;
            int buttonIndex = 0;

            if (showDiveButton)
            {
                diveBounds = new Rectangle(
                    (int)MathF.Round(left),
                    (int)MathF.Round(top + buttonIndex * buttonSize),
                    (int)MathF.Round(buttonSize),
                    (int)MathF.Round(buttonSize)
                );
                buttonIndex++;
            }

            if (showSlapButton)
            {
                slapBounds = new Rectangle(
                    (int)MathF.Round(left),
                    (int)MathF.Round(top + buttonIndex * buttonSize),
                    (int)MathF.Round(buttonSize),
                    (int)MathF.Round(buttonSize)
                );
            }

            return new MobileActionButtonsLayout(
                showDiveButton,
                diveBounds,
                showSlapButton,
                slapBounds
            );
        }

        public void DrawMobileActionButtons(
            SpriteBatch spriteBatch,
            MobileActionButtonsLayout layout,
            string diveButtonLabel,
            string slapButtonLabel
        )
        {
            if (!layout.HasAnyButton)
                return;

            this.EnsureMobileAtlasTexture();

            if (layout.HasDiveButton)
                this.DrawMobileActionButton(spriteBatch, layout.DiveButtonBounds, diveButtonLabel);

            if (layout.HasSlapButton)
                this.DrawMobileActionButton(spriteBatch, layout.SlapButtonBounds, slapButtonLabel);
        }

        private void DrawDiveSession(SpriteBatch spriteBatch, DiveSlapSession session)
        {
            Farmer renderFarmer = this.PrepareDiveRenderFarmer(session);
            this.toolSuppressedFarmer = renderFarmer;
            try
            {
                if (renderFarmer.swimming.Value)
                    this.DrawDiveSwimShadow(spriteBatch, renderFarmer);

                renderFarmer.draw(spriteBatch);
            }
            finally
            {
                this.toolSuppressedFarmer = null;
            }
        }

        private void DrawCaughtFishPreview(SpriteBatch spriteBatch, Farmer farmer, StardewValley.Tools.FishingRod rod)
        {
            if (rod.whichFish is null)
                return;

            float boardBobOffset = 4f * (float)Math.Round(
                Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 250.0),
                2
            );
            int standingPixelY = farmer.StandingPixel.Y;
            float boardLayerDepth = standingPixelY / 10000f + 0.06f;
            float iconLayerDepth = standingPixelY / 10000f + 0.0601f;
            var fishData = rod.whichFish.GetParsedOrErrorData();
            Texture2D fishTexture = fishData.GetTexture();
            Rectangle fishSourceRect = fishData.GetSourceRect(0, null);

            spriteBatch.Draw(
                Game1.mouseCursors,
                Game1.GlobalToLocal(Game1.viewport, farmer.Position + new Vector2(-120f, -288f + boardBobOffset)),
                new Rectangle(31, 1870, 73, 49),
                Color.White * 0.8f,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                boardLayerDepth
            );

            spriteBatch.Draw(
                fishTexture,
                Game1.GlobalToLocal(Game1.viewport, farmer.Position + new Vector2(-80f, -216f + boardBobOffset)),
                fishSourceRect,
                Color.White,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                iconLayerDepth
            );

            if (rod.numberOfFishCaught > 1)
            {
                Utility.drawTinyDigits(
                    rod.numberOfFishCaught,
                    spriteBatch,
                    Game1.GlobalToLocal(Game1.viewport, farmer.Position + new Vector2(-28f, -168f + boardBobOffset)),
                    3f,
                    standingPixelY / 10000f + 0.061f,
                    Color.White
                );
            }

            this.DrawCaughtFishHeldSprite(
                spriteBatch,
                fishTexture,
                fishSourceRect,
                Game1.GlobalToLocal(
                    Game1.viewport,
                    farmer.Position + new Vector2(this.fishTwitchOffsetX, CaughtFishHeldBaseYOffset + this.fishTwitchOffsetY)
                ),
                GetHeldFishBaseRotation(rod) + this.fishTwitchRotation,
                standingPixelY / 10000f + 0.062f
            );

            for (int i = 1; i < rod.numberOfFishCaught; i++)
            {
                float bonusRotation = i == 2 ? MathF.PI : 2.5132742f;
                this.DrawCaughtFishHeldSprite(
                    spriteBatch,
                    fishTexture,
                    fishSourceRect,
                    Game1.GlobalToLocal(Game1.viewport, farmer.Position + new Vector2(-12f * i, CaughtFishHeldBaseYOffset)),
                    GetHeldFishBaseRotation(rod) > 0f ? bonusRotation : 0f,
                    standingPixelY / 10000f + 0.058f
                );
            }

            string fishName = fishData.DisplayName ?? "???";
            Vector2 fishNameSize = Game1.smallFont.MeasureString(fishName);
            spriteBatch.DrawString(
                Game1.smallFont,
                fishName,
                Game1.GlobalToLocal(
                    Game1.viewport,
                    farmer.Position + new Vector2(26f - fishNameSize.X / 2f, -278f + boardBobOffset)
                ),
                rod.bossFish ? new Color(126, 61, 237) : Game1.textColor,
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                boardLayerDepth
            );

            if (rod.fishSize == -1)
                return;

            string sizeLabel = Game1.content.LoadString(@"Strings\StringsFromCSFiles:FishingRod.cs.14082");
            spriteBatch.DrawString(
                Game1.smallFont,
                sizeLabel,
                Game1.GlobalToLocal(Game1.viewport, farmer.Position + new Vector2(20f, -214f + boardBobOffset)),
                Game1.textColor,
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                boardLayerDepth
            );

            double displaySize = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.en
                ? rod.fishSize
                : Math.Round(rod.fishSize * 2.54);
            string sizeText = Game1.content.LoadString(@"Strings\StringsFromCSFiles:FishingRod.cs.14083", displaySize);
            Vector2 sizeTextSize = Game1.smallFont.MeasureString(sizeText);
            spriteBatch.DrawString(
                Game1.smallFont,
                sizeText,
                Game1.GlobalToLocal(
                    Game1.viewport,
                    farmer.Position + new Vector2(85f - sizeTextSize.X / 2f, -179f + boardBobOffset)
                ),
                rod.recordSize ? Color.Blue : Game1.textColor,
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                boardLayerDepth
            );
        }

        private Farmer PrepareDiveRenderFarmer(DiveSlapSession session)
        {
            this.diveRenderFarmer ??= Game1.player.CreateFakeEventFarmer();

            Farmer renderFarmer = this.diveRenderFarmer;
            // 跳水时不真的移动玩家本体，而是把原版 farmer.draw 替换成这只 fake farmer。
            // 这样能吃到原版的环境着色、图层和农夫外观，但不会干扰玩家真实位置和碰撞。
            renderFarmer.currentLocation = Game1.currentLocation;
            renderFarmer.Position = session.RenderPosition;
            if (session.State == DiveSlapState.ResolveSuccess)
            {
                renderFarmer.Halt();
                renderFarmer.faceDirection(2);
            }
            else
            {
                renderFarmer.faceDirection(GetDiveFacingDirection(session));
            }
            renderFarmer.UsingTool = false;
            renderFarmer.canReleaseTool = false;
            renderFarmer.swimming.Value = ShouldRenderDiveAsSwimming(session);
            renderFarmer.bathingClothes.Value = renderFarmer.swimming.Value;
            renderFarmer.yOffset = 0f;

            if (session.State == DiveSlapState.ResolveSuccess)
            {
                this.ApplyCarryHoldPose(renderFarmer);
                return renderFarmer;
            }

            int frame = GetDiveFrame(session);
            this.ApplyPose(renderFarmer, frame);
            return renderFarmer;
        }

        private void ApplyPose(Farmer farmer, int frame)
        {
            // fake farmer 可能残留上一帧的动作状态；每次绘制前都先清掉，再强制切到目标帧。
            farmer.completelyStopAnimatingOrDoingAction();
            farmer.FarmerSprite.StopAnimation();
            farmer.FarmerSprite.ClearAnimation();
            farmer.FarmerSprite.setCurrentFrame(frame, 0, 0, 1, false, false);
        }

        private void ApplyCarryHoldPose(Farmer farmer)
        {
            farmer.completelyStopAnimatingOrDoingAction();
            farmer.FarmerSprite.StopAnimation();
            farmer.FarmerSprite.ClearAnimation();
            farmer.FarmerSprite.setCurrentFrame(DiveSuccessHoldDownFrame, 1);
        }

        private void UpdateSwimShadowAnimation(DiveSlapSession? session)
        {
            if (session is null || !ShouldRenderDiveAsSwimming(session))
                return;

            this.swimShadowTimer -= Game1.currentGameTime.ElapsedGameTime.Milliseconds;
            if (this.swimShadowTimer > 0)
                return;

            this.swimShadowTimer = SwimShadowFrameDurationMs;
            this.swimShadowFrame = (this.swimShadowFrame + 1) % SwimShadowFrameCount;
        }

        private void DrawDiveSwimShadow(SpriteBatch spriteBatch, Farmer farmer)
        {
            this.EnsureSwimShadowTexture();
            if (this.swimShadowTexture is null)
                return;

            Vector2 screenPosition = Game1.GlobalToLocal(
                Game1.viewport,
                farmer.Position + new Vector2(0f, farmer.FarmerSprite.SpriteHeight / 4 * 4)
            );

            spriteBatch.Draw(
                this.swimShadowTexture,
                screenPosition,
                new Rectangle(this.swimShadowFrame * SwimShadowFrameSize, 0, SwimShadowFrameSize, SwimShadowFrameSize),
                Color.White,
                0f,
                Vector2.Zero,
                SwimShadowScale,
                SpriteEffects.None,
                0f
            );
        }

        private static int GetStandingFrame(int facingDirection)
        {
            return facingDirection switch
            {
                0 => FarmerStandUpFrame,
                1 => FarmerStandRightFrame,
                2 => FarmerStandDownFrame,
                _ => FarmerStandLeftFrame
            };
        }

        private static int GetDiveFrame(DiveSlapSession session)
        {
            int facingDirection = GetDiveFacingDirection(session);
            return session.State switch
            {
                DiveSlapState.Windup => GetDiveWindupFrame(facingDirection),
                DiveSlapState.Diving => GetDiveMoveFrame(facingDirection),
                DiveSlapState.Returning => GetDiveMoveFrame(facingDirection),
                DiveSlapState.Slapping when session.SlapAnimationTicksRemaining > 0 => session.FacingRight ? DiveSlapPunchRightFrame : DiveSlapPunchLeftFrame,
                DiveSlapState.Slapping => FarmerIdleDownFrame,
                DiveSlapState.ResolveSuccessPauseBefore => FarmerIdleDownFrame,
                DiveSlapState.ResolveSuccess => DiveSuccessHoldDownFrame,
                DiveSlapState.ResolveFailPauseBefore => FarmerIdleDownFrame,
                DiveSlapState.ResolveFail => FarmerIdleDownFrame,
                DiveSlapState.ResolveFailPauseAfter => FarmerIdleDownFrame,
                _ => GetDiveIdleFrame(facingDirection)
            };
        }

        private static int GetDiveWindupFrame(int facingDirection)
        {
            return facingDirection switch
            {
                0 => DiveSlapWindupUpFrame,
                1 => DiveSlapWindupRightFrame,
                2 => DiveSlapWindupDownFrame,
                _ => DiveSlapWindupLeftFrame
            };
        }

        private static int GetDiveMoveFrame(int facingDirection)
        {
            return facingDirection switch
            {
                0 => DiveSlapMoveUpFrame,
                1 => DiveSlapMoveRightFrame,
                2 => DiveSlapMoveDownFrame,
                _ => DiveSlapMoveLeftFrame
            };
        }

        private static int GetDiveFacingDirection(DiveSlapSession session)
        {
            if (session.State == DiveSlapState.Slapping && session.SlapAnimationTicksRemaining > 0)
                return session.FacingRight ? 1 : 3;

            if (session.State == DiveSlapState.ResolveSuccess)
                return 2;

            return session.State == DiveSlapState.Returning
                ? GetOppositeFacingDirection(session.CastFacingDirection)
                : session.CastFacingDirection;
        }

        private static int GetDiveIdleFrame(int facingDirection)
        {
            return GetStandingFrame(facingDirection);
        }

        private static bool ShouldRenderDiveAsSwimming(DiveSlapSession session)
        {
            return session.State is DiveSlapState.Slapping
                or DiveSlapState.ResolveSuccessPauseBefore
                or DiveSlapState.ResolveFailPauseBefore
                or DiveSlapState.ResolveFail
                or DiveSlapState.ResolveFailPauseAfter;
        }

        private static int GetOppositeFacingDirection(int facingDirection)
        {
            return facingDirection switch
            {
                0 => 2,
                1 => 3,
                2 => 0,
                _ => 1
            };
        }

        private static float GetHeldFishBaseRotation(StardewValley.Tools.FishingRod rod)
        {
            if (rod.whichFish is null || rod.fishSize == -1)
                return 0f;

            return GetHeldFishBaseRotation(rod.whichFish.QualifiedItemId);
        }

        private static float GetHeldFishBaseRotation(string? itemId)
        {
            return itemId is not "(O)800" and not "(O)798" and not "(O)149" and not "(O)151"
                ? 2.3561945f
                : 0f;
        }

        private void DrawCaughtFishHeldSprite(
            SpriteBatch spriteBatch,
            Texture2D fishTexture,
            Rectangle fishSourceRect,
            Vector2 screenPosition,
            float rotation,
            float layerDepth
        )
        {
            spriteBatch.Draw(
                fishTexture,
                screenPosition,
                fishSourceRect,
                Color.White,
                rotation,
                new Vector2(8f, 8f),
                3f,
                SpriteEffects.None,
                layerDepth
            );
        }

        private void DrawSlapProgressHud(SpriteBatch spriteBatch, DiveSlapSession session)
        {
            this.EnsurePixelTexture();
            if (this.pixelTexture is null || session.RequiredHits <= 0)
                return;

            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, session.RenderPosition);
            float centerX = screenPos.X + 32f;
            float barLeft = centerX - HudBarWidth / 2f;
            float hitBarTop = screenPos.Y - HudAboveFeetOffset;
            float timeBarTop = hitBarTop + HudHitBarHeight + HudBarGap;

            string hitsText = $"{session.CurrentHits}/{session.RequiredHits}";
            Vector2 textSize = Game1.smallFont.MeasureString(hitsText) * HudTextScale;
            Vector2 textPos = new(centerX - textSize.X / 2f, hitBarTop - textSize.Y - 2f);

            spriteBatch.DrawString(Game1.smallFont, hitsText,
                textPos + new Vector2(1f, 1f), HudTextShadowColor,
                0f, Vector2.Zero, HudTextScale, SpriteEffects.None, 1f);
            spriteBatch.DrawString(Game1.smallFont, hitsText,
                textPos, Color.White,
                0f, Vector2.Zero, HudTextScale, SpriteEffects.None, 1f);

            this.DrawHudRect(spriteBatch, barLeft - HudBorderSize, hitBarTop - HudBorderSize,
                HudBarWidth + HudBorderSize * 2f, HudHitBarHeight + HudBorderSize * 2f, HudBorderColor);

            float totalGaps = (session.RequiredHits - 1) * HudSegmentGap;
            float segWidth = (HudBarWidth - totalGaps) / session.RequiredHits;
            for (int i = 0; i < session.RequiredHits; i++)
            {
                float segX = barLeft + i * (segWidth + HudSegmentGap);
                Color segColor = i < session.CurrentHits ? HudHitFilledColor : HudHitEmptyColor;
                this.DrawHudRect(spriteBatch, segX, hitBarTop, segWidth, HudHitBarHeight, segColor);
            }

            this.DrawHudRect(spriteBatch, barLeft - HudBorderSize, timeBarTop - HudBorderSize,
                HudBarWidth + HudBorderSize * 2f, HudTimeBarHeight + HudBorderSize * 2f, HudBorderColor);
            this.DrawHudRect(spriteBatch, barLeft, timeBarTop, HudBarWidth, HudTimeBarHeight, HudTimeBarBgColor);

            float timeFraction = session.TotalSlapTicks > 0
                ? MathHelper.Clamp((float)session.RemainingSlapTicks / session.TotalSlapTicks, 0f, 1f)
                : 0f;
            if (timeFraction > 0f)
            {
                float fillWidth = HudBarWidth * timeFraction;
                Color timeColor = GetTimeBarColor(timeFraction);
                this.DrawHudRect(spriteBatch, barLeft, timeBarTop, fillWidth, HudTimeBarHeight, timeColor);
            }
        }

        private void DrawFailRetaliationFish(SpriteBatch spriteBatch, DiveSlapSession session)
        {
            if (string.IsNullOrWhiteSpace(session.TargetFishQualifiedItemId))
                return;

            var fishData = ItemRegistry.GetMetadata(session.TargetFishQualifiedItemId).GetParsedOrErrorData();
            Texture2D fishTexture = fishData.GetTexture();
            Rectangle fishSourceRect = fishData.GetSourceRect(0, null);
            Vector2 worldPos = GetFailRetaliationFishWorldPosition(session, this.GetPhaseProgress(session));
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
            Vector2 origin = new(fishSourceRect.Width / 2f, fishSourceRect.Height / 2f);

            spriteBatch.Draw(
                fishTexture,
                screenPos,
                fishSourceRect,
                Color.White,
                this.GetFailRetaliationFishRotation(session),
                origin,
                FailRetaliationFishScale,
                SpriteEffects.FlipHorizontally,
                1f
            );
        }

        private void DrawDiveSlapFish(SpriteBatch spriteBatch, DiveSlapSession session)
        {
            if (string.IsNullOrWhiteSpace(session.TargetFishQualifiedItemId))
                return;

            var fishData = ItemRegistry.GetMetadata(session.TargetFishQualifiedItemId).GetParsedOrErrorData();
            Texture2D fishTexture = fishData.GetTexture();
            Rectangle fishSourceRect = fishData.GetSourceRect(0, null);
            Vector2 worldPos = GetDiveSlapFishWorldPosition(session);
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
            Vector2 origin = new(fishSourceRect.Width / 2f, fishSourceRect.Height / 2f);

            spriteBatch.Draw(
                fishTexture,
                screenPos,
                fishSourceRect,
                Color.White,
                session.SlapFishRotation,
                origin,
                SlapFishScale,
                SpriteEffects.None,
                1f
            );
        }

        private void DrawDiveSuccessHeldFish(SpriteBatch spriteBatch, DiveSlapSession session)
        {
            if (string.IsNullOrWhiteSpace(session.TargetFishQualifiedItemId))
                return;

            var fishData = ItemRegistry.GetMetadata(session.TargetFishQualifiedItemId).GetParsedOrErrorData();
            Texture2D fishTexture = fishData.GetTexture();
            Rectangle fishSourceRect = fishData.GetSourceRect(0, null);
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, GetDiveSuccessHeldFishWorldPosition(session));
            Vector2 origin = new(fishSourceRect.Width / 2f, fishSourceRect.Height / 2f);

            spriteBatch.Draw(
                fishTexture,
                screenPos,
                fishSourceRect,
                Color.White,
                GetHeldFishBaseRotation(session.TargetFishQualifiedItemId) - MathF.PI / 2f,
                origin,
                DiveSuccessHeldFishScale,
                SpriteEffects.None,
                1f
            );
        }

        private void DrawDiveSlapPrompt(SpriteBatch spriteBatch, string promptText)
        {
            this.EnsurePixelTexture();
            if (this.pixelTexture is null)
                return;

            float zoom = Game1.options.zoomLevel;
            Vector2 viewportOffset = Game1.player.Position - new Vector2(Game1.viewport.X, Game1.viewport.Y);
            float uiCenterX = (viewportOffset.X + 32f) * zoom;
            float uiFeetY = viewportOffset.Y * zoom;
            this.DrawPromptBox(spriteBatch, uiCenterX, uiFeetY + PromptBelowFeetOffset * zoom, promptText);
        }

        private void DrawMobileActionButton(SpriteBatch spriteBatch, Rectangle bounds, string label)
        {
            if (this.mobileAtlasTexture is not null)
            {
                spriteBatch.Draw(
                    this.mobileAtlasTexture,
                    bounds,
                    MobileActionButtonSourceRect,
                    Color.White,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    1f
                );
            }
            else
            {
                IClickableMenu.drawTextureBox(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White);
            }

            Vector2 textSize = Game1.smallFont.MeasureString(label);
            float availableWidth = bounds.Width - 16f;
            float textScale = textSize.X > 0f
                ? Math.Min(1f, availableWidth / textSize.X)
                : 1f;
            Vector2 textPos = new(
                bounds.X + (bounds.Width - textSize.X * textScale) / 2f,
                bounds.Y + (bounds.Height - textSize.Y * textScale) / 2f
            );

            Utility.drawTextWithShadow(spriteBatch, label, Game1.smallFont, textPos, MobileButtonTextColor, textScale);
        }

        private void DrawPromptBox(SpriteBatch spriteBatch, float centerX, float topY, string text)
        {
            this.EnsurePixelTexture();
            if (this.pixelTexture is null)
                return;

            float pulse = (float)(0.75 + 0.25 * Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 400.0));
            Vector2 textSize = Game1.smallFont.MeasureString(text);
            float boxW = textSize.X + PromptPadX * 2f;
            float boxH = textSize.Y + PromptPadY * 2f;
            float boxX = centerX - boxW / 2f;

            this.DrawHudRect(spriteBatch, boxX, topY, boxW, boxH, Color.Black * 0.65f);

            Vector2 textPos = new(boxX + PromptPadX, topY + PromptPadY);
            spriteBatch.DrawString(Game1.smallFont, text,
                textPos + Vector2.One, HudTextShadowColor * pulse,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
            spriteBatch.DrawString(Game1.smallFont, text,
                textPos, Color.White * pulse,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
        }

        private Vector2 GetMobileButtonsAnchorScreenPosition(DiveSlapSession? session, bool uiScaled)
        {
            Vector2 worldPos = session?.RenderPosition ?? Game1.player.Position;
            Vector2 viewportOffset = worldPos - new Vector2(Game1.viewport.X, Game1.viewport.Y);

            if (uiScaled)
            {
                // 世界坐标 → 屏幕像素(×zoom) → UI SpriteBatch 坐标(÷uiScale)
                float scale = Game1.options.zoomLevel / Game1.options.uiScale;
                return new Vector2((viewportOffset.X + 32f) * scale, viewportOffset.Y * scale);
            }

            return new Vector2(viewportOffset.X + 32f, viewportOffset.Y);
        }

        private float GetPhaseProgress(DiveSlapSession session)
        {
            float progress = session.PhaseDurationTicks <= 0
                ? 1f
                : 1f - (float)session.PhaseTicksRemaining / session.PhaseDurationTicks;
            return MathHelper.Clamp(progress, 0f, 1f);
        }

        private void StartDiveSlapFishJump(DiveSlapSession session)
        {
            session.SlapFishOffsetX = 0f;
            session.SlapFishOffsetY = 0f;
            session.SlapFishRotation = 0f;
            session.SlapFishVelocityX = (float)(this.rng.NextDouble() * (CaughtFishMaxHorizontalTwitchVelocity * 2f) - CaughtFishMaxHorizontalTwitchVelocity);
            session.SlapFishVelocityY = CaughtFishInitialJumpVelocity;
            session.SlapFishRotationVelocity = session.SlapFishVelocityX * 0.065f;
            session.SlapFishBouncesRemaining = 1;
            this.PlayFishWaterSplash(session.SlapFishSurfacePosition);
        }

        private void UpdateDiveSlapFish(DiveSlapSession session)
        {
            if (!IsDiveSlapFishAnimationActive(session))
                return;

            bool isAirborne = session.SlapFishOffsetY < 0f || session.SlapFishVelocityY < 0f;
            float previousOffsetY = session.SlapFishOffsetY;
            session.SlapFishOffsetX += session.SlapFishVelocityX;
            session.SlapFishRotation += session.SlapFishRotationVelocity;
            session.SlapFishVelocityX *= 0.72f;
            session.SlapFishRotationVelocity *= 0.78f;

            if (isAirborne)
            {
                session.SlapFishOffsetY += session.SlapFishVelocityY;
                session.SlapFishVelocityY += 1.1f;

                if (previousOffsetY < 0f && session.SlapFishOffsetY >= 0f)
                {
                    session.SlapFishOffsetY = 0f;
                    this.PlayFishWaterSplash(session.SlapFishSurfacePosition);
                    if (session.SlapFishBouncesRemaining > 0)
                    {
                        session.SlapFishVelocityY = CaughtFishBounceJumpVelocity;
                        session.SlapFishRotationVelocity = -session.SlapFishRotationVelocity * 0.6f;
                        session.SlapFishBouncesRemaining--;
                    }
                    else
                    {
                        session.SlapFishVelocityY = 0f;
                    }
                }
            }
            else
            {
                session.SlapFishOffsetY = 0f;
                session.SlapFishVelocityY = 0f;
            }

            if (MathF.Abs(session.SlapFishOffsetX) < 0.05f && MathF.Abs(session.SlapFishVelocityX) < 0.05f)
            {
                session.SlapFishOffsetX = 0f;
                session.SlapFishVelocityX = 0f;
            }

            if (MathF.Abs(session.SlapFishRotation) < 0.005f && MathF.Abs(session.SlapFishRotationVelocity) < 0.005f)
            {
                session.SlapFishRotation = 0f;
                session.SlapFishRotationVelocity = 0f;
            }
        }

        private static Vector2 GetDiveSlapFishWorldPosition(DiveSlapSession session)
        {
            float idleBobOffsetY = IsDiveSlapFishAnimationActive(session)
                ? 0f
                : -MathF.Sin((float)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 220.0)) * SlapFishIdleBobAmplitude;
            return session.SlapFishSurfacePosition + new Vector2(session.SlapFishOffsetX, session.SlapFishOffsetY + idleBobOffsetY);
        }

        private static Vector2 GetDiveSuccessHeldFishWorldPosition(DiveSlapSession session)
        {
            return session.RenderPosition + new Vector2(32f, -90f);
        }

        private static bool IsDiveSuccessHeldFishVisible(DiveSlapSession session)
        {
            return session.State == DiveSlapState.ResolveSuccess
                || (session.State == DiveSlapState.Returning && !session.OutcomeApplied);
        }

        private static bool IsDiveSlapFishAnimationActive(DiveSlapSession session)
        {
            return session.SlapFishVelocityX != 0f
                || session.SlapFishVelocityY != 0f
                || session.SlapFishOffsetX != 0f
                || session.SlapFishOffsetY < 0f
                || session.SlapFishRotation != 0f
                || session.SlapFishRotationVelocity != 0f;
        }

        private static Vector2 GetFailRetaliationFishWorldPosition(DiveSlapSession session, float progress)
        {
            progress = MathHelper.Clamp(progress, 0f, 1f);
            if (progress <= FailRetaliationImpactProgress)
            {
                float arcProgress = progress / FailRetaliationImpactProgress;
                return GetArcPosition(
                    session.FailRetaliationStartPosition,
                    session.FailRetaliationImpactPosition,
                    session.FailRetaliationArcHeight,
                    arcProgress
                );
            }

            float postImpactProgress = (progress - FailRetaliationImpactProgress) / (1f - FailRetaliationImpactProgress);
            return GetArcPosition(
                session.FailRetaliationImpactPosition,
                session.FailRetaliationExitPosition,
                -28f,
                postImpactProgress
            );
        }

        private float GetFailRetaliationFishRotation(DiveSlapSession session)
        {
            float progress = this.GetPhaseProgress(session);
            float previousProgress = Math.Max(0f, progress - 0.015f);
            float nextProgress = Math.Min(1f, progress + 0.015f);
            Vector2 tangent = GetFailRetaliationFishWorldPosition(session, nextProgress)
                - GetFailRetaliationFishWorldPosition(session, previousProgress);
            if (tangent.LengthSquared() <= 0.0001f)
                return 0f;

            // 当前鱼物品贴图在水平翻转后，视觉朝向更接近“头朝上”，
            // 所以这里用切线角再补一个 90 度偏移，让鱼头真正沿轨迹前进。
            return MathF.Atan2(tangent.Y, tangent.X) + MathF.PI / 2f;
        }

        private static Vector2 GetArcPosition(Vector2 start, Vector2 end, float arcHeight, float progress)
        {
            Vector2 worldPos = Vector2.Lerp(start, end, MathHelper.Clamp(progress, 0f, 1f));
            worldPos.Y -= MathF.Sin(progress * MathF.PI) * arcHeight;
            return worldPos;
        }

        private void DrawHudRect(SpriteBatch spriteBatch, float x, float y, float width, float height, Color color)
        {
            spriteBatch.Draw(
                this.pixelTexture!,
                new Vector2(x, y),
                sourceRectangle: null,
                color: color,
                rotation: 0f,
                origin: Vector2.Zero,
                scale: new Vector2(width, height),
                effects: SpriteEffects.None,
                layerDepth: 1f);
        }

        private static Color GetTimeBarColor(float fraction)
        {
            if (fraction > 0.5f)
                return Color.Lerp(HudTimeYellowColor, HudTimeGreenColor, (fraction - 0.5f) * 2f);
            if (fraction > 0.2f)
                return Color.Lerp(HudTimeRedColor, HudTimeYellowColor, (fraction - 0.2f) / 0.3f);
            return HudTimeRedColor;
        }

        private void PlayFishWaterSplash(Vector2 splashWorldPos)
        {
            Game1.playSound(ModConstants.FishWaterSplashSoundId);
            this.SpawnSplashParticles(splashWorldPos);
        }

        private void SpawnSplashParticles(Vector2 splashWorldPos)
        {
            this.EnsurePixelTexture();

            Color[] palette = { new Color(170, 220, 255), new Color(120, 190, 255), Color.White };
            int dropletCount = this.rng.Next(10, 15);
            for (int i = 0; i < dropletCount; i++)
            {
                float angle = MathHelper.Pi + (float)(this.rng.NextDouble() * MathHelper.Pi);
                float speed = 3.6f + (float)(this.rng.NextDouble() * 7.6f);
                float width = 4f + (float)(this.rng.NextDouble() * 4f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = splashWorldPos + new Vector2((float)(this.rng.NextDouble() * 32f - 16f), 30f + (float)(this.rng.NextDouble() * 12f - 6f)),
                    Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed - 3.6f),
                    Alpha = 0.95f,
                    AlphaDecay = 0.035f + (float)(this.rng.NextDouble() * 0.01f),
                    Width = width,
                    Height = width * 1.3f,
                    Rotation = angle,
                    RotationSpeed = (float)(this.rng.NextDouble() * 0.2f - 0.1f),
                    Color = palette[this.rng.Next(palette.Length)]
                });
            }
        }

        private void SpawnPlayerDiveSplash(Vector2 splashWorldPos)
        {
            this.EnsurePixelTexture();

            Vector2 center = splashWorldPos + new Vector2(32f, 30f);
            Color[] palette = { new Color(170, 225, 255), new Color(120, 195, 255), new Color(200, 235, 255), Color.White };

            int dropletCount = this.rng.Next(20, 28);
            for (int i = 0; i < dropletCount; i++)
            {
                float angle = -MathHelper.PiOver2 + (float)(this.rng.NextDouble() - 0.5) * MathHelper.Pi * 0.85f;
                float speed = 7f + (float)(this.rng.NextDouble() * 11f);
                float size = 5f + (float)(this.rng.NextDouble() * 6f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = center + new Vector2((float)(this.rng.NextDouble() * 40f - 20f), (float)(this.rng.NextDouble() * 12f - 6f)),
                    Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
                    Alpha = 0.95f,
                    AlphaDecay = 0.022f + (float)(this.rng.NextDouble() * 0.01f),
                    Width = size,
                    Height = size * 1.4f,
                    Rotation = angle,
                    RotationSpeed = (float)(this.rng.NextDouble() * 0.15f - 0.075f),
                    Color = palette[this.rng.Next(palette.Length)],
                    Gravity = 0.15f
                });
            }

            int rippleCount = this.rng.Next(8, 12);
            for (int i = 0; i < rippleCount; i++)
            {
                float direction = (float)(this.rng.NextDouble() * MathHelper.TwoPi);
                float speed = 5f + (float)(this.rng.NextDouble() * 7f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = center + new Vector2((float)(this.rng.NextDouble() * 20f - 10f), 0f),
                    Velocity = new Vector2(MathF.Cos(direction) * speed, MathF.Sin(direction) * speed * 0.3f),
                    Alpha = 0.7f,
                    AlphaDecay = 0.022f + (float)(this.rng.NextDouble() * 0.008f),
                    Width = 16f + (float)(this.rng.NextDouble() * 12f),
                    Height = 4f + (float)(this.rng.NextDouble() * 3f),
                    Rotation = direction,
                    RotationSpeed = 0f,
                    Color = new Color(150, 215, 255, 180)
                });
            }
        }

        private void SpawnPlayerExitSplash(Vector2 splashWorldPos)
        {
            this.EnsurePixelTexture();

            Vector2 center = splashWorldPos + new Vector2(32f, 30f);
            Color[] palette = { new Color(170, 225, 255), new Color(120, 195, 255), Color.White };

            int dropletCount = this.rng.Next(14, 20);
            for (int i = 0; i < dropletCount; i++)
            {
                float angle = -MathHelper.PiOver2 + (float)(this.rng.NextDouble() - 0.5) * MathHelper.Pi * 0.7f;
                float speed = 5.6f + (float)(this.rng.NextDouble() * 9f);
                float size = 4f + (float)(this.rng.NextDouble() * 5f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = center + new Vector2((float)(this.rng.NextDouble() * 32f - 16f), (float)(this.rng.NextDouble() * 8f - 4f)),
                    Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
                    Alpha = 0.9f,
                    AlphaDecay = 0.028f + (float)(this.rng.NextDouble() * 0.012f),
                    Width = size,
                    Height = size * 1.3f,
                    Rotation = angle,
                    RotationSpeed = (float)(this.rng.NextDouble() * 0.12f - 0.06f),
                    Color = palette[this.rng.Next(palette.Length)],
                    Gravity = 0.15f
                });
            }

            int rippleCount = this.rng.Next(5, 8);
            for (int i = 0; i < rippleCount; i++)
            {
                float direction = (float)(this.rng.NextDouble() * MathHelper.TwoPi);
                float speed = 4f + (float)(this.rng.NextDouble() * 5f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = center + new Vector2((float)(this.rng.NextDouble() * 16f - 8f), 0f),
                    Velocity = new Vector2(MathF.Cos(direction) * speed, MathF.Sin(direction) * speed * 0.25f),
                    Alpha = 0.6f,
                    AlphaDecay = 0.025f + (float)(this.rng.NextDouble() * 0.01f),
                    Width = 12f + (float)(this.rng.NextDouble() * 10f),
                    Height = 4f + (float)(this.rng.NextDouble() * 2f),
                    Rotation = direction,
                    RotationSpeed = 0f,
                    Color = new Color(150, 215, 255, 160)
                });
            }
        }

        private void SpawnSlapWaterDroplets(Vector2 impactWorldPos)
        {
            this.EnsurePixelTexture();

            Color[] palette = { new Color(170, 220, 255), new Color(140, 200, 255), Color.White };
            int dropletCount = this.rng.Next(5, 9);
            for (int i = 0; i < dropletCount; i++)
            {
                float angle = -MathHelper.PiOver2 + (float)(this.rng.NextDouble() - 0.5) * MathHelper.Pi;
                float speed = 4f + (float)(this.rng.NextDouble() * 6f);
                float size = 3f + (float)(this.rng.NextDouble() * 4f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = impactWorldPos + new Vector2((float)(this.rng.NextDouble() * 24f - 12f), (float)(this.rng.NextDouble() * 8f - 4f)),
                    Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
                    Alpha = 0.8f,
                    AlphaDecay = 0.035f + (float)(this.rng.NextDouble() * 0.015f),
                    Width = size,
                    Height = size * 1.2f,
                    Rotation = (float)(this.rng.NextDouble() * MathHelper.TwoPi),
                    RotationSpeed = (float)(this.rng.NextDouble() * 0.1f - 0.05f),
                    Color = palette[this.rng.Next(palette.Length)],
                    Gravity = 0.12f
                });
            }
        }

        private void SpawnRetaliationImpactParticles(Vector2 impactWorldPos)
        {
            this.SpawnBurstParticles(impactWorldPos);

            Color[] palette = { Color.White, new Color(255, 245, 160), new Color(255, 165, 80) };
            int shockCount = this.rng.Next(18, 24);
            for (int i = 0; i < shockCount; i++)
            {
                float angle = (float)(this.rng.NextDouble() * MathHelper.TwoPi);
                float speed = 4.5f + (float)(this.rng.NextDouble() * 8f);
                float width = 6f + (float)(this.rng.NextDouble() * 5f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = impactWorldPos + new Vector2((float)(this.rng.NextDouble() * 8f - 4f), (float)(this.rng.NextDouble() * 8f - 4f)),
                    Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
                    Alpha = 1f,
                    AlphaDecay = 0.028f + (float)(this.rng.NextDouble() * 0.01f),
                    Width = width,
                    Height = 2f + (float)(this.rng.NextDouble() * 1.2f),
                    Rotation = angle,
                    RotationSpeed = 0f,
                    Color = palette[this.rng.Next(palette.Length)]
                });
            }
        }

        private void SpawnBurstParticles(Vector2 impactWorldPos)
        {
            this.EnsurePixelTexture();

            Vector2 center = impactWorldPos + new Vector2(12f, 0f);
            Color[] palette = { Color.Yellow, Color.Orange, Color.White, new Color(255, 255, 100) };

            int sparkCount = this.rng.Next(12, 18);
            for (int i = 0; i < sparkCount; i++)
            {
                float angle = (float)(this.rng.NextDouble() * MathHelper.TwoPi);
                float speed = 3f + (float)(this.rng.NextDouble() * 7f);
                float size = 3f + (float)(this.rng.NextDouble() * 3.5f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = center + new Vector2(
                        (float)(this.rng.NextDouble() * 6 - 3),
                        (float)(this.rng.NextDouble() * 6 - 3)),
                    Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
                    Alpha = 1f,
                    AlphaDecay = 0.03f + (float)(this.rng.NextDouble() * 0.008f),
                    Width = size,
                    Height = size,
                    Rotation = (float)(this.rng.NextDouble() * MathHelper.TwoPi),
                    RotationSpeed = (float)(this.rng.NextDouble() * 0.3f - 0.15f),
                    Color = palette[this.rng.Next(palette.Length)]
                });
            }

            int lineCount = this.rng.Next(6, 10);
            for (int i = 0; i < lineCount; i++)
            {
                float angle = (float)(this.rng.NextDouble() * MathHelper.TwoPi);
                float dist = 10f + (float)(this.rng.NextDouble() * 8f);
                this.burstParticles.Add(new BurstParticle
                {
                    WorldPos = center + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist),
                    Velocity = new Vector2(MathF.Cos(angle) * 2.4f, MathF.Sin(angle) * 2.4f),
                    Alpha = 0.9f,
                    AlphaDecay = 0.018f,
                    Width = 7f + (float)(this.rng.NextDouble() * 5f),
                    Height = 1.5f + (float)(this.rng.NextDouble() * 0.75f),
                    Rotation = angle,
                    RotationSpeed = 0f,
                    Color = palette[this.rng.Next(palette.Length)]
                });
            }
        }

        private void EnsurePixelTexture()
        {
            if (this.pixelTexture != null)
                return;

            this.pixelTexture = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            this.pixelTexture.SetData(new[] { Color.White });
        }

        private void EnsureMobileAtlasTexture()
        {
            if (this.mobileAtlasTexture is not null || this.attemptedMobileAtlasLoad)
                return;

            this.attemptedMobileAtlasLoad = true;
            try
            {
                this.mobileAtlasTexture = Game1.content.Load<Texture2D>(@"LooseSprites\MobileAtlas_manually_made");
            }
            catch
            {
                this.mobileAtlasTexture = null;
            }
        }

        private void EnsureSwimShadowTexture()
        {
            this.swimShadowTexture ??= Game1.content.Load<Texture2D>(@"LooseSprites\swimShadow");
        }
    }
}
