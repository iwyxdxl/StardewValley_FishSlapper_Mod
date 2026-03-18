using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Events;
using StardewValley;
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
        // 跳水位移阶段，当前改用 docs 里的 carryRun 组。
        private const int DiveSlapMoveDownFrame = 128;
        private const int DiveSlapMoveRightFrame = 136;
        private const int DiveSlapMoveUpFrame = 144;
        private const int DiveSlapMoveLeftFrame = 152;
        private const int FarmerStandDownFrame = 0;
        private const int FarmerStandRightFrame = 8;
        private const int FarmerStandUpFrame = 16;
        private const int FarmerStandLeftFrame = 24;

        private const int CaughtFishSlapDurationTicks = 30;
        private const int DiveHitAnimationDurationTicks = 10;
        private readonly List<BurstParticle> burstParticles = new();
        private readonly Random rng = new();
        private Texture2D? pixelTexture;
        private int caughtFishSlapTick = -1;
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
        }

        public bool ShouldHideCaughtFishToolPreview => this.hideCaughtFishPreview;

        public bool ShouldSuppressToolDraw(Farmer farmer)
        {
            return ReferenceEquals(farmer, this.toolSuppressedFarmer)
                || (ReferenceEquals(farmer, Game1.player) && this.localPoseResetTicks > 0 && this.caughtFishSlapTick < 0);
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
            this.caughtFishSlapTick = 8;
            this.SpawnBurstParticles(Game1.player.Position + new Vector2(-16f, -64f));
        }

        public void PlayDiveSlap(Vector2 impactWorldPos)
        {
            Game1.playSound(ModConstants.SlapSoundId);
            this.SpawnBurstParticles(impactWorldPos);
        }

        public void PlayDiveWaterEntry()
        {
            Game1.playSound(ModConstants.DiveWaterEntrySoundId);
        }

        public void PlayDiveWaterExit()
        {
            Game1.playSound(ModConstants.DiveWaterExitSoundId);
        }

        public void PlayDiveJump()
        {
            Game1.playSound(ModConstants.DiveJumpSoundId);
        }

        public void OnUpdateTicked(DiveSlapSession? session)
        {
            if (this.caughtFishSlapTick >= 0)
            {
                this.caughtFishSlapTick++;
                if (this.caughtFishSlapTick > CaughtFishSlapDurationTicks)
                    this.caughtFishSlapTick = -1;
            }

            if (this.localPoseResetTicks > 0)
                this.localPoseResetTicks--;

            if (session is not null && session.SlapAnimationTicksRemaining > 0)
                session.SlapAnimationTicksRemaining--;

            foreach (var particle in this.burstParticles)
            {
                particle.WorldPos += particle.Velocity;
                particle.Velocity *= 0.96f;
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
                // 这里故意不清原版“手里举鱼”的状态，只临时覆写一帧出拳姿势。
                // 如果把动画栈整个清掉，会把老玩法里“拿着鱼无限扇”的行为打断。
                Game1.player.FarmerSprite.setCurrentFrame(CaughtFishPunchFrame);
                return;
            }

            this.hideCaughtFishPreview = false;

            if (this.localPoseResetTicks > 0)
                this.ApplyPose(Game1.player, GetStandingFrame(this.localPoseResetFacingDirection));
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

        public void OnRenderedWorld(RenderedWorldEventArgs e, DiveSlapSession? session)
        {
            if (session is null)
                this.diveRenderFarmer = null;

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

            this.hideCaughtFishPreview = false;
        }

        public void OnRenderedActiveMenu(RenderedActiveMenuEventArgs e, DiveSlapSession? session)
        {
            if (session is null)
                return;

            string title = $"Dive slap {session.CurrentHits}/{session.RequiredHits}";
            string detail = session.State switch
            {
                DiveSlapState.Windup => "Jumping...",
                DiveSlapState.Diving => "Diving...",
                DiveSlapState.Slapping => $"Time {session.RemainingSlapTicks / 60f:0.0}s",
                DiveSlapState.ResolveSuccess => "Caught!",
                DiveSlapState.ResolveFail => "Escaped!",
                DiveSlapState.Returning => "Returning...",
                _ => string.Empty
            };

            Utility.drawTextWithShadow(e.SpriteBatch, title, Game1.dialogueFont, new Vector2(64f, 64f), Game1.textColor);
            if (detail.Length > 0)
                Utility.drawTextWithShadow(e.SpriteBatch, detail, Game1.smallFont, new Vector2(64f, 108f), Game1.textColor);
        }

        private void DrawDiveSession(SpriteBatch spriteBatch, DiveSlapSession session)
        {
            Farmer renderFarmer = this.PrepareDiveRenderFarmer(session);
            this.toolSuppressedFarmer = renderFarmer;
            try
            {
                renderFarmer.draw(spriteBatch);
            }
            finally
            {
                this.toolSuppressedFarmer = null;
            }
        }

        private Farmer PrepareDiveRenderFarmer(DiveSlapSession session)
        {
            this.diveRenderFarmer ??= Game1.player.CreateFakeEventFarmer();

            Farmer renderFarmer = this.diveRenderFarmer;
            // 跳水时不真的移动玩家本体，而是把原版 farmer.draw 替换成这只 fake farmer。
            // 这样能吃到原版的环境着色、图层和农夫外观，但不会干扰玩家真实位置和碰撞。
            renderFarmer.currentLocation = Game1.currentLocation;
            renderFarmer.Position = session.RenderPosition;
            renderFarmer.faceDirection(GetDiveFacingDirection(session));
            renderFarmer.UsingTool = false;
            renderFarmer.canReleaseTool = false;
            renderFarmer.swimming.Value = false;
            renderFarmer.bathingClothes.Value = false;
            renderFarmer.yOffset = 0f;

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
                DiveSlapState.Windup => GetDiveMoveFrame(facingDirection),
                DiveSlapState.Diving => GetDiveMoveFrame(facingDirection),
                DiveSlapState.Returning => GetDiveMoveFrame(facingDirection),
                DiveSlapState.Slapping when session.SlapAnimationTicksRemaining > 0 => session.FacingRight ? DiveSlapPunchRightFrame : DiveSlapPunchLeftFrame,
                _ => GetDiveIdleFrame(facingDirection)
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

            return session.State == DiveSlapState.Returning
                ? GetOppositeFacingDirection(session.CastFacingDirection)
                : session.CastFacingDirection;
        }

        private static int GetDiveIdleFrame(int facingDirection)
        {
            return GetStandingFrame(facingDirection);
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

        private void SpawnBurstParticles(Vector2 impactWorldPos)
        {
            this.EnsurePixelTexture();
            this.burstParticles.Clear();

            Vector2 center = impactWorldPos + new Vector2(12f, 12f);
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
                    AlphaDecay = 0.013f + (float)(this.rng.NextDouble() * 0.007f),
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
                    AlphaDecay = 0.01f,
                    Width = 14f + (float)(this.rng.NextDouble() * 10f),
                    Height = 3f + (float)(this.rng.NextDouble() * 1.5f),
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
    }
}
