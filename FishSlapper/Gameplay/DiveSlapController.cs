using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using FishSlapper.Rendering;
using FishSlapper.Vanilla;

namespace FishSlapper.Gameplay
{
    internal sealed class DiveSlapController
    {
        private const int DiveSlapHealthCost = 20;
        private const float DiveSlapStaminaCost = 50f;
        private const int WindupTicks = 8;
        private const int DivingTicks = 36;
        private const int ResolveTicks = 8;
        private const int FailRetaliationTicks = 56;
        private const int ReturningTicks = 36;
        private const float DiveArcHeight = 72f;
        private const float ReturnArcHeight = 56f;
        private const float FailRetaliationImpactProgress = 0.52f;

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly DiveSlapRenderer renderer;
        private readonly VanillaFishingBridge vanillaBridge;
        private ModConfig config;
        private DiveSlapSession? activeSession;

        public DiveSlapController(
            IModHelper helper,
            IMonitor monitor,
            ModConfig config,
            DiveSlapRenderer renderer,
            VanillaFishingBridge vanillaBridge
        )
        {
            this.helper = helper;
            this.monitor = monitor;
            this.config = config;
            this.renderer = renderer;
            this.vanillaBridge = vanillaBridge;
        }

        public DiveSlapSession? ActiveSession => this.activeSession;

        public string? GetDiveSlapKeyHint()
        {
            if (this.activeSession is not null)
                return null;
            if (Game1.activeClickableMenu is not BobberBar)
                return null;
            return this.config.DiveSlapKey.ToString();
        }

        public string? GetSlapKeyHint()
        {
            if (this.activeSession is not null)
                return this.activeSession.State == DiveSlapState.Slapping
                    ? this.config.SlapKey.ToString()
                    : null;

            return this.vanillaBridge.TryGetCaughtFishRod(out _)
                ? this.config.SlapKey.ToString()
                : null;
        }

        public void UpdateConfig(ModConfig config)
        {
            this.config = config;
        }

        public bool TryDrawCaughtFishPreview(FishingRod rod, SpriteBatch spriteBatch, Farmer farmer)
        {
            return this.renderer.TryDrawCaughtFishPreview(spriteBatch, farmer, rod);
        }

        public void OnButtonPressed(ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (this.activeSession is not null)
            {
                this.HandleActiveSessionInput(e);
                return;
            }

            if (this.config.SlapKey.JustPressed() && this.vanillaBridge.TryGetCaughtFishRod(out _))
            {
                this.renderer.PlayCaughtFishSlap();
                this.helper.Input.Suppress(e.Button);
                return;
            }

            if (!this.config.DiveSlapKey.JustPressed())
                return;

            if (!this.vanillaBridge.TryCreateDiveSession(out DiveSlapSession? session) || session is null)
                return;

            this.activeSession = session;
            this.LockPlayerForDive(session);
            this.BeginPhase(session, DiveSlapState.Windup, WindupTicks, session.OriginalPlayerPosition, session.OriginalPlayerPosition);
            this.monitor.Log("Started dive slap session.", LogLevel.Trace);
            this.helper.Input.Suppress(e.Button);
        }

        public void OnUpdateTicked()
        {
            this.renderer.OnUpdateTicked(this.activeSession);

            if (this.activeSession is null)
                return;

            // 跳水扇鱼的主体状态机。这里只负责阶段推进，
            // 真正的原版结算桥接放在 VanillaFishingBridge 里。
            switch (this.activeSession.State)
            {
                case DiveSlapState.Windup:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        this.renderer.PlayDiveJump();
                        this.BeginPhase(
                            this.activeSession,
                            DiveSlapState.Diving,
                            DivingTicks,
                            this.activeSession.OriginalPlayerPosition,
                            this.vanillaBridge.GetDiveRenderTarget(this.activeSession)
                        );
                    }
                    break;

                case DiveSlapState.Diving:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        this.activeSession.State = DiveSlapState.Slapping;
                        this.activeSession.RenderPosition = this.activeSession.PhaseTargetPosition;
                        this.activeSession.PhaseStartPosition = this.activeSession.PhaseTargetPosition;
                        this.activeSession.PhaseTargetPosition = this.activeSession.PhaseTargetPosition;
                        this.renderer.PlayDiveWaterEntry();
                    }
                    break;

                case DiveSlapState.Slapping:
                    this.activeSession.RenderPosition = this.activeSession.PhaseTargetPosition;
                    this.activeSession.RemainingSlapTicks--;
                    if (this.activeSession.RemainingSlapTicks <= 0)
                        this.BeginResolveFail(this.activeSession);
                    break;

                case DiveSlapState.ResolveSuccess:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        this.BeginPhase(
                            this.activeSession,
                            DiveSlapState.Returning,
                            ReturningTicks,
                            this.activeSession.RenderPosition,
                            this.activeSession.OriginalPlayerPosition
                        );
                        this.renderer.PlayDiveWaterExit();
                    }
                    break;

                case DiveSlapState.ResolveFail:
                    bool failPhaseFinished = this.AdvancePhase(this.activeSession);
                    if (!this.activeSession.FailRetaliationImpactTriggered
                        && (this.GetPhaseProgress(this.activeSession) >= FailRetaliationImpactProgress || failPhaseFinished))
                    {
                        this.TriggerFailRetaliationImpact(this.activeSession);
                    }

                    if (failPhaseFinished)
                    {
                        if (!this.activeSession.OutcomeApplied)
                        {
                            this.vanillaBridge.ApplyFailure(this.activeSession);
                            this.activeSession.OutcomeApplied = true;
                        }

                        this.BeginPhase(
                            this.activeSession,
                            DiveSlapState.Returning,
                            ReturningTicks,
                            this.activeSession.RenderPosition,
                            this.activeSession.OriginalPlayerPosition
                        );
                        this.renderer.PlayDiveWaterExit();
                    }
                    break;

                case DiveSlapState.Returning:
                    if (this.AdvancePhase(this.activeSession))
                        this.EndSession();
                    break;
            }
        }

        public void OnMenuChanged(MenuChangedEventArgs e)
        {
            if (this.activeSession is null || !ReferenceEquals(e.OldMenu, this.activeSession.BobberBar))
                return;

            if (this.activeSession.State is DiveSlapState.ResolveSuccess or DiveSlapState.ResolveFail or DiveSlapState.Returning)
                return;

            this.monitor.Log("Cancelled dive slap session because the BobberBar closed unexpectedly.", LogLevel.Trace);
            this.CancelSession();
        }

        public bool TryDrawLocalPlayerReplacement(Farmer farmer, SpriteBatch spriteBatch)
        {
            return this.activeSession is not null
                && ReferenceEquals(farmer, Game1.player)
                && this.renderer.TryDrawDiveSession(spriteBatch, this.activeSession);
        }

        public bool ShouldHideCaughtFishToolPreview(Farmer farmer)
        {
            return ReferenceEquals(farmer, Game1.player) && this.renderer.ShouldHideCaughtFishToolPreview;
        }

        public bool ShouldSuppressToolDraw(Farmer farmer)
        {
            if (ReferenceEquals(farmer, Game1.player) && this.activeSession is not null)
                return true;

            return this.renderer.ShouldSuppressToolDraw(farmer);
        }

        public bool ShouldFreezeBobberBarUpdate(BobberBar bobberBar)
        {
            return this.vanillaBridge.ShouldFreezeBobberBarUpdate(bobberBar, this.activeSession);
        }

        private void HandleActiveSessionInput(ButtonPressedEventArgs e)
        {
            bool pressedDiveKey = this.config.DiveSlapKey.JustPressed();
            bool pressedSlapKey = this.config.SlapKey.JustPressed();
            if (!pressedDiveKey && !pressedSlapKey)
                return;

            this.helper.Input.Suppress(e.Button);

            if (this.activeSession is null || this.activeSession.State != DiveSlapState.Slapping || !pressedSlapKey)
                return;

            // 入水后只认扇鱼键；累计次数达到阈值后立刻走成功结算。
            this.activeSession.CurrentHits++;
            this.activeSession.SlapAnimationTicksRemaining = this.renderer.DiveHitTickDuration;
            this.renderer.PlayDiveSlap(this.activeSession, this.activeSession.TargetBobberPosition + new Vector2(0f, -8f));

            if (this.activeSession.CurrentHits >= this.activeSession.RequiredHits)
                this.BeginResolveSuccess(this.activeSession);
        }

        private void BeginResolveSuccess(DiveSlapSession session)
        {
            this.BeginPhase(session, DiveSlapState.ResolveSuccess, ResolveTicks, session.RenderPosition, session.RenderPosition);

            if (!session.OutcomeApplied)
            {
                this.vanillaBridge.ApplySuccess(session);
                this.ShowDiveSuccessMessage(session);
                session.OutcomeApplied = true;
            }
        }

        private void BeginResolveFail(DiveSlapSession session)
        {
            session.FailRetaliationImpactTriggered = false;
            this.BeginPhase(session, DiveSlapState.ResolveFail, FailRetaliationTicks, session.RenderPosition, session.RenderPosition);
            this.renderer.PlayDiveRetaliationLaunch(session.FailRetaliationStartPosition);
        }

        private void BeginPhase(DiveSlapSession session, DiveSlapState state, int duration, Vector2 startPosition, Vector2 targetPosition)
        {
            session.State = state;
            session.PhaseDurationTicks = duration;
            session.PhaseTicksRemaining = duration;
            session.PhaseStartPosition = startPosition;
            session.PhaseTargetPosition = targetPosition;
            session.RenderPosition = startPosition;
        }

        private bool AdvancePhase(DiveSlapSession session)
        {
            session.PhaseTicksRemaining = Math.Max(0, session.PhaseTicksRemaining - 1);
            float progress = this.GetPhaseProgress(session);
            Vector2 renderPosition = Vector2.Lerp(session.PhaseStartPosition, session.PhaseTargetPosition, progress);
            float arcHeight = session.State switch
            {
                DiveSlapState.Diving => DiveArcHeight,
                DiveSlapState.Returning => ReturnArcHeight,
                _ => 0f
            };

            if (arcHeight > 0f)
                renderPosition.Y -= MathF.Sin(progress * MathF.PI) * arcHeight;

            session.RenderPosition = renderPosition;
            return session.PhaseTicksRemaining <= 0;
        }

        private float GetPhaseProgress(DiveSlapSession session)
        {
            float progress = session.PhaseDurationTicks <= 0
                ? 1f
                : 1f - (float)session.PhaseTicksRemaining / session.PhaseDurationTicks;
            return MathHelper.Clamp(progress, 0f, 1f);
        }

        private void TriggerFailRetaliationImpact(DiveSlapSession session)
        {
            session.FailRetaliationImpactTriggered = true;
            string retaliationText = this.helper.Translation
                .Get("hud.dive-slap-retaliation", new { fish = session.TargetFishDisplayName })
                .ToString();
            this.renderer.PlayDiveRetaliationImpact(session.FailRetaliationImpactPosition);
            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(retaliationText));
        }

        private void ShowDiveSuccessMessage(DiveSlapSession session)
        {
            int elapsedTicks = Math.Max(1, session.TotalSlapTicks - session.RemainingSlapTicks);
            float elapsedSeconds = Math.Max(0.1f, elapsedTicks / 60f);
            string timeText = elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string successText = this.helper.Translation
                .Get(
                    "hud.dive-slap-success",
                    new
                    {
                        time = timeText,
                        fish = session.TargetFishDisplayName,
                        slap_num = session.CurrentHits
                    }
                )
                .ToString();
            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(successText));
        }

        private void LockPlayerForDive(DiveSlapSession session)
        {
            // 玩家逻辑上仍留在岸边，只是在渲染层替换成跳水分身。
            // 因此这里要锁输入，避免和原版钓鱼/移动状态互相打架。
            Game1.player.Halt();
            Game1.player.canMove = false;
            Game1.player.freezePause = Math.Max(1, Game1.player.freezePause);
            Game1.player.faceDirection(session.CastFacingDirection);
        }

        private void RestorePlayerFromDive(DiveSlapSession session)
        {
            Game1.player.Position = session.OriginalPlayerPosition;
            Game1.player.FacingDirection = session.PreviousFacingDirection;
            Game1.player.Halt();

            if (session.OutcomeApplied)
            {
                // 成功/失败分支都已经走完原版收尾逻辑，这里必须强制解锁玩家。
                // 如果把进入 BobberBar 之前的锁定状态原样恢复，会再次把人卡死。
                Game1.player.forceCanMove();
                Game1.player.freezePause = 0;
                Game1.player.canMove = true;
            }
            else
            {
                Game1.player.canMove = session.PreviousCanMove;
                Game1.player.freezePause = session.PreviousFreezePause;
            }

            this.renderer.ResetLocalPlayerPose(session.PreviousFacingDirection);
        }

        private void EndSession()
        {
            if (this.activeSession is null)
                return;

            DiveSlapSession completedSession = this.activeSession;
            this.RestorePlayerFromDive(completedSession);
            this.activeSession = null;
            this.ApplyDiveCostOnReturn();
        }

        private void CancelSession()
        {
            if (this.activeSession is null)
                return;

            this.RestorePlayerFromDive(this.activeSession);
            this.activeSession = null;
        }

        private void ApplyDiveCostOnReturn()
        {
            Farmer player = Game1.player;
            float oldStamina = player.Stamina;

            if (player.health <= DiveSlapHealthCost)
            {
                player.takeDamage(player.health, true, null!);
                return;
            }

            if (oldStamina <= DiveSlapStaminaCost)
            {
                player.Stamina = 0f;
                player.checkForExhaustion(oldStamina);
                return;
            }

            player.health -= DiveSlapHealthCost;
            player.Stamina = oldStamina - DiveSlapStaminaCost;
        }
    }
}
