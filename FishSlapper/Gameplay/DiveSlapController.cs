using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework.Audio;
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
        private const int DivingTicks = 45;
        private const int SuccessReturnDelayTicks = 30;
        private const int ResolveSuccessTicks = 60;
        private const int FailRetaliationDelayTicks = 30;
        private const int FailRetaliationTicks = 56;
        private const int FailRetaliationRecoveryTicks = 60;
        private const int ReturningTicks = 45;
        private const float DiveArcHeight = 72f;
        private const float ReturnArcHeight = 56f;
        private const float FailRetaliationImpactProgress = 0.52f;
        private static readonly FieldInfo? FishingRodChargeSoundField = typeof(FishingRod).GetField("chargeSound", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? FishingRodReelSoundField = typeof(FishingRod).GetField("reelSound", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? FishingRodHadBobberField = typeof(FishingRod).GetField("hadBobber", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? FishingRodPlayerAdjustedBobberField = typeof(FishingRod).GetField("_hasPlayerAdjustedBobber", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? FishingRodBobberBobField = typeof(FishingRod).GetField("bobberBob", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? FishingRodBobberTimeAccumulatorField = typeof(FishingRod).GetField("bobberTimeAccumulator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? FishingRodTimePerBobberBobField = typeof(FishingRod).GetField("timePerBobberBob", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? BobberBarReelSoundField = typeof(BobberBar).GetField("reelSound", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? BobberBarUnReelSoundField = typeof(BobberBar).GetField("unReelSound", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly DiveSlapRenderer renderer;
        private readonly VanillaFishingBridge vanillaBridge;
        private ModConfig config;
        private DiveSlapSession? activeSession;
        private CaughtFishSlapSummary? activeCaughtFishSlapSummary;
        private int worldTickCounter;

        private sealed class CaughtFishSlapSummary
        {
            public FishingRod Rod { get; set; } = null!;
            public string FishDisplayName { get; set; } = "???";
            public int FirstSlapTick { get; set; }
            public int SlapCount { get; set; }
        }

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

        public bool CanUseMobileDiveButton()
        {
            return Context.IsWorldReady
                && this.activeSession is null
                && this.vanillaBridge.CanCreateDiveSession();
        }

        public bool CanUseMobileSlapButton()
        {
            if (!Context.IsWorldReady)
                return false;

            if (this.activeSession is not null)
                return this.activeSession.State == DiveSlapState.Slapping;

            return this.vanillaBridge.TryGetCaughtFishRod(out _);
        }

        public bool TryUseMobileDiveButton()
        {
            return this.TryStartDiveSlapSession();
        }

        public bool TryUseMobileSlapButton()
        {
            if (!Context.IsWorldReady)
                return false;

            if (this.activeSession is not null)
                return this.TryPerformDiveSessionSlap();

            return this.TryPerformCaughtFishSlap();
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

            if (this.config.SlapKey.JustPressed() && this.TryPerformCaughtFishSlap())
            {
                this.helper.Input.Suppress(e.Button);
                return;
            }

            if (this.config.DiveSlapKey.JustPressed() && this.TryStartDiveSlapSession())
                this.helper.Input.Suppress(e.Button);
        }

        public void OnUpdateTicked()
        {
            if (Context.IsWorldReady)
            {
                this.worldTickCounter++;
                this.UpdateCaughtFishSlapSummary();
            }
            else
            {
                this.CompleteCaughtFishSlapSummary(showMessage: false);
            }

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
                        this.renderer.PlayDiveWaterEntry(this.activeSession.RenderPosition);
                    }
                    break;

                case DiveSlapState.Slapping:
                    this.activeSession.RenderPosition = this.activeSession.PhaseTargetPosition;
                    this.activeSession.RemainingSlapTicks--;
                    if (this.activeSession.RemainingSlapTicks <= 0)
                        this.BeginResolveFail(this.activeSession);
                    break;

                case DiveSlapState.ResolveSuccessPauseBefore:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        this.BeginPhase(
                            this.activeSession,
                            DiveSlapState.Returning,
                            ReturningTicks,
                            this.activeSession.RenderPosition,
                            this.activeSession.OriginalPlayerPosition
                        );
                        this.renderer.PlayDiveWaterExit(this.activeSession.RenderPosition);
                    }
                    break;

                case DiveSlapState.ResolveSuccess:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        if (!this.activeSession.OutcomeApplied)
                        {
                            this.vanillaBridge.ApplySuccess(this.activeSession);
                            this.ShowDiveSuccessMessage(this.activeSession);
                            this.activeSession.OutcomeApplied = true;
                        }

                        this.EndSession();
                    }
                    break;

                case DiveSlapState.ResolveFailPauseBefore:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        this.BeginPhase(
                            this.activeSession,
                            DiveSlapState.ResolveFail,
                            FailRetaliationTicks,
                            this.activeSession.RenderPosition,
                            this.activeSession.RenderPosition
                        );
                        this.renderer.PlayDiveRetaliationLaunch(this.activeSession.FailRetaliationStartPosition);
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
                        this.renderer.PlayDiveRetaliationSplashdown(this.activeSession.FailRetaliationExitPosition);
                        if (!this.activeSession.OutcomeApplied)
                        {
                            this.vanillaBridge.ApplyFailure(this.activeSession);
                            this.activeSession.OutcomeApplied = true;
                        }

                        this.BeginPhase(
                            this.activeSession,
                            DiveSlapState.ResolveFailPauseAfter,
                            FailRetaliationRecoveryTicks,
                            this.activeSession.RenderPosition,
                            this.activeSession.RenderPosition
                        );
                    }
                    break;

                case DiveSlapState.ResolveFailPauseAfter:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        this.BeginPhase(
                            this.activeSession,
                            DiveSlapState.Returning,
                            ReturningTicks,
                            this.activeSession.RenderPosition,
                            this.activeSession.OriginalPlayerPosition
                        );
                        this.renderer.PlayDiveWaterExit(this.activeSession.RenderPosition);
                    }
                    break;

                case DiveSlapState.Returning:
                    if (this.AdvancePhase(this.activeSession))
                    {
                        if (!this.activeSession.OutcomeApplied)
                        {
                            this.BeginPhase(
                                this.activeSession,
                                DiveSlapState.ResolveSuccess,
                                ResolveSuccessTicks,
                                this.activeSession.RenderPosition,
                                this.activeSession.RenderPosition
                            );
                        }
                        else
                        {
                            this.EndSession();
                        }
                    }
                    break;
            }
        }

        public void OnMenuChanged(MenuChangedEventArgs e)
        {
            if (this.activeSession is null || !ReferenceEquals(e.OldMenu, this.activeSession.BobberBar))
                return;

            if (this.activeSession.State is DiveSlapState.ResolveSuccess
                or DiveSlapState.ResolveSuccessPauseBefore
                or DiveSlapState.ResolveFailPauseBefore
                or DiveSlapState.ResolveFail
                or DiveSlapState.ResolveFailPauseAfter
                or DiveSlapState.Returning)
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

        public bool ShouldSuppressBobberBarDraw(BobberBar bobberBar)
        {
            return this.activeSession is not null
                && ReferenceEquals(this.activeSession.BobberBar, bobberBar);
        }

        private void HandleActiveSessionInput(ButtonPressedEventArgs e)
        {
            if (e.Button == SButton.MouseLeft)
            {
                this.helper.Input.Suppress(e.Button);
                return;
            }

            bool pressedDiveKey = this.config.DiveSlapKey.JustPressed();
            bool pressedSlapKey = this.config.SlapKey.JustPressed();
            if (!pressedDiveKey && !pressedSlapKey)
                return;

            this.helper.Input.Suppress(e.Button);

            if (pressedSlapKey)
                this.TryPerformDiveSessionSlap();
        }

        private bool TryPerformCaughtFishSlap()
        {
            if (!this.vanillaBridge.TryGetCaughtFishRod(out FishingRod? caughtFishRod) || caughtFishRod is null)
                return false;

            this.RecordCaughtFishSlap(caughtFishRod);
            this.renderer.PlayCaughtFishSlap();
            return true;
        }

        private bool TryStartDiveSlapSession()
        {
            if (!this.vanillaBridge.TryCreateDiveSession(out DiveSlapSession? session) || session is null)
                return false;

            this.activeSession = session;
            StopFishingRodLoopingAudio(session.Rod);
            this.LockPlayerForDive(session);
            this.BeginPhase(session, DiveSlapState.Windup, WindupTicks, session.OriginalPlayerPosition, session.OriginalPlayerPosition);
            this.monitor.Log("Started dive slap session.", LogLevel.Trace);
            return true;
        }

        private bool TryPerformDiveSessionSlap()
        {
            if (this.activeSession is null || this.activeSession.State != DiveSlapState.Slapping)
                return false;

            this.activeSession.CurrentHits++;
            this.activeSession.SlapAnimationTicksRemaining = this.renderer.DiveHitTickDuration;
            this.renderer.PlayDiveSlap(this.activeSession, this.activeSession.TargetBobberPosition + new Vector2(0f, -8f));

            if (this.activeSession.CurrentHits >= this.activeSession.RequiredHits)
                this.BeginResolveSuccess(this.activeSession);

            return true;
        }

        private void BeginResolveSuccess(DiveSlapSession session)
        {
            ResetDiveSlapFishState(session);
            StopFishingRodLoopingAudio(session.Rod);
            if (ReferenceEquals(Game1.activeClickableMenu, session.BobberBar))
                Game1.activeClickableMenu = null;

            this.BeginPhase(
                session,
                DiveSlapState.ResolveSuccessPauseBefore,
                SuccessReturnDelayTicks,
                session.RenderPosition,
                session.RenderPosition
            );
        }

        private void BeginResolveFail(DiveSlapSession session)
        {
            StopFishingRodLoopingAudio(session.Rod);
            session.FailRetaliationImpactTriggered = false;
            this.BeginPhase(
                session,
                DiveSlapState.ResolveFailPauseBefore,
                FailRetaliationDelayTicks,
                session.RenderPosition,
                session.RenderPosition
            );
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
            string playerName = string.IsNullOrWhiteSpace(Game1.player.Name) ? "Player" : Game1.player.Name;
            string retaliationText = this.helper.Translation
                .Get(
                    "hud.dive-slap-retaliation",
                    new
                    {
                        player = playerName,
                        fish = session.TargetFishDisplayName
                    }
                )
                .ToString();
            this.renderer.PlayDiveRetaliationImpact(session.FailRetaliationImpactPosition);
            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(retaliationText));
        }

        private void ShowDiveSuccessMessage(DiveSlapSession session)
        {
            int elapsedTicks = Math.Max(1, session.TotalSlapTicks - session.RemainingSlapTicks);
            float elapsedSeconds = Math.Max(0.1f, elapsedTicks / 60f);
            string timeText = elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string playerName = string.IsNullOrWhiteSpace(Game1.player.Name) ? "Player" : Game1.player.Name;
            string successText = this.helper.Translation
                .Get(
                    "hud.dive-slap-success",
                    new
                    {
                        player = playerName,
                        time = timeText,
                        fish = session.TargetFishDisplayName,
                        slap_num = session.CurrentHits
                    }
                )
                .ToString();
            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(successText));
        }

        private void RecordCaughtFishSlap(FishingRod rod)
        {
            if (this.activeCaughtFishSlapSummary is not null && !ReferenceEquals(this.activeCaughtFishSlapSummary.Rod, rod))
                this.CompleteCaughtFishSlapSummary(showMessage: true);

            string fishDisplayName = ResolveCaughtFishDisplayName(rod);
            if (this.activeCaughtFishSlapSummary is null)
            {
                this.activeCaughtFishSlapSummary = new CaughtFishSlapSummary
                {
                    Rod = rod,
                    FishDisplayName = fishDisplayName,
                    FirstSlapTick = this.worldTickCounter,
                    SlapCount = 1
                };
                return;
            }

            this.activeCaughtFishSlapSummary.FishDisplayName = fishDisplayName;
            this.activeCaughtFishSlapSummary.SlapCount++;
        }

        private void UpdateCaughtFishSlapSummary()
        {
            if (this.activeCaughtFishSlapSummary is null)
                return;

            if (ReferenceEquals(Game1.player.CurrentTool, this.activeCaughtFishSlapSummary.Rod) && this.activeCaughtFishSlapSummary.Rod.fishCaught)
                return;

            this.CompleteCaughtFishSlapSummary(showMessage: true);
        }

        private void CompleteCaughtFishSlapSummary(bool showMessage)
        {
            if (this.activeCaughtFishSlapSummary is null)
                return;

            CaughtFishSlapSummary summary = this.activeCaughtFishSlapSummary;
            this.activeCaughtFishSlapSummary = null;
            if (showMessage)
                this.ShowCaughtFishSlapMessage(summary);
        }

        private void ShowCaughtFishSlapMessage(CaughtFishSlapSummary summary)
        {
            if (summary.SlapCount <= 0)
                return;

            int elapsedTicks = Math.Max(1, this.worldTickCounter - summary.FirstSlapTick);
            float elapsedSeconds = Math.Max(0.1f, elapsedTicks / 60f);
            string timeText = elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string playerName = string.IsNullOrWhiteSpace(Game1.player.Name) ? "Player" : Game1.player.Name;
            string successText = this.helper.Translation
                .Get(
                    "hud.caught-slap-success",
                    new
                    {
                        player = playerName,
                        time = timeText,
                        fish = summary.FishDisplayName,
                        slap_num = summary.SlapCount
                    }
                )
                .ToString();
            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(successText));
        }

        private static string ResolveCaughtFishDisplayName(FishingRod rod)
        {
            if (rod.whichFish is null)
                return "???";

            var fishData = rod.whichFish.GetParsedOrErrorData();
            return fishData.DisplayName ?? rod.whichFish.QualifiedItemId ?? "???";
        }

        private static void ResetDiveSlapFishState(DiveSlapSession session)
        {
            session.SlapFishOffsetX = 0f;
            session.SlapFishOffsetY = 0f;
            session.SlapFishRotation = 0f;
            session.SlapFishVelocityX = 0f;
            session.SlapFishVelocityY = 0f;
            session.SlapFishRotationVelocity = 0f;
            session.SlapFishBouncesRemaining = 0;
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
            StopFishingRodLoopingAudio(completedSession.Rod);
            ResetFishingRodPostDiveVisualState(completedSession.Rod);
            this.RestorePlayerFromDive(completedSession);
            this.activeSession = null;
            this.ApplyDiveCostOnReturn();
        }

        private void CancelSession()
        {
            if (this.activeSession is null)
                return;

            StopFishingRodLoopingAudio(this.activeSession.Rod);
            ResetFishingRodPostDiveVisualState(this.activeSession.Rod);
            this.RestorePlayerFromDive(this.activeSession);
            this.activeSession = null;
        }

        private static void StopFishingRodLoopingAudio(FishingRod rod)
        {
            StopFishingRodCue(FishingRodChargeSoundField);
            StopFishingRodCue(FishingRodReelSoundField);
            StopFishingRodCue(BobberBarReelSoundField);
            StopFishingRodCue(BobberBarUnReelSoundField);
        }

        private static void ResetFishingRodPostDiveVisualState(FishingRod rod)
        {
            rod.bobber.Set(Vector2.Zero);
            rod.castedButBobberStillInAir = false;
            rod.pullingOutOfWater = false;
            Game1.player.armOffset = Vector2.Zero;

            SetFishingRodFieldValue(FishingRodHadBobberField, rod, false);
            SetFishingRodFieldValue(FishingRodPlayerAdjustedBobberField, rod, false);
            SetFishingRodFieldValue(FishingRodBobberBobField, rod, 0);
            SetFishingRodFieldValue(FishingRodBobberTimeAccumulatorField, rod, 0f);
            SetFishingRodFieldValue(FishingRodTimePerBobberBobField, rod, 0f);
        }

        private static void StopFishingRodCue(FieldInfo? cueField)
        {
            if (cueField?.GetValue(null) is not ICue cue)
                return;

            if (cue.IsPlaying)
                cue.Stop(AudioStopOptions.Immediate);

            cueField.SetValue(null, null);
        }

        private static void SetFishingRodFieldValue<T>(FieldInfo? field, FishingRod rod, T value)
        {
            field?.SetValue(rod, value);
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
