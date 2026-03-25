using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
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
        private readonly string modUniqueId;
        private readonly DiveSlapRenderer renderer;
        private readonly VanillaFishingBridge vanillaBridge;
        private readonly PerScreen<ScreenState> screenStates = new(() => new ScreenState());
        private readonly Dictionary<long, DiveSlapVisualSnapshot> observedDiveStates = new();
        private ModConfig config;

        private sealed class CaughtFishSlapSummary
        {
            public FishingRod Rod { get; set; } = null!;
            public uint CaughtFishSessionId { get; set; }
            public string FishDisplayName { get; set; } = "???";
            public string PlayerName { get; set; } = "Player";
            public uint FirstSlapTick { get; set; }
            public uint LastSlapTick { get; set; }
            public int SlapCount { get; set; }
        }

        private sealed class ScreenState
        {
            public DiveSlapSession? ActiveSession { get; set; }
            public CaughtFishSlapSummary? ActiveCaughtFishSlapSummary { get; set; }
            public uint CurrentCaughtFishSessionId { get; set; }
            public FishingRod? ObservedCaughtFishRod { get; set; }
            public string ObservedCaughtFishQualifiedItemId { get; set; } = string.Empty;
            public int ObservedCaughtFishSize { get; set; }
            public int ObservedCaughtFishQuality { get; set; }
            public bool ObservedCaughtFishBoss { get; set; }
        }

        public DiveSlapController(
            IModHelper helper,
            IMonitor monitor,
            string modUniqueId,
            ModConfig config,
            DiveSlapRenderer renderer,
            VanillaFishingBridge vanillaBridge
        )
        {
            this.helper = helper;
            this.monitor = monitor;
            this.modUniqueId = modUniqueId;
            this.config = config;
            this.renderer = renderer;
            this.vanillaBridge = vanillaBridge;
            this.LogMissingReflectionBindings();
        }

        public DiveSlapSession? ActiveSession => this.screenStates.Value.ActiveSession;

        public string? GetDiveSlapKeyHint()
        {
            if (this.ActiveSession is not null)
                return null;
            if (Game1.activeClickableMenu is not BobberBar)
                return null;
            return this.config.DiveSlapKey.ToString();
        }

        public string? GetSlapKeyHint()
        {
            Farmer? player = Game1.player;
            if (player is null)
                return null;

            if (this.ActiveSession is not null)
                return this.ActiveSession.State == DiveSlapState.Slapping
                    ? this.config.SlapKey.ToString()
                    : null;

            return this.vanillaBridge.TryGetCaughtFishRod(player, out _)
                ? this.config.SlapKey.ToString()
                : null;
        }

        public bool CanUseMobileDiveButton()
        {
            Farmer? player = Game1.player;
            return Context.IsWorldReady
                && player is not null
                && this.ActiveSession is null
                && this.vanillaBridge.CanCreateDiveSession(player, Game1.activeClickableMenu);
        }

        public bool CanUseMobileSlapButton()
        {
            Farmer? player = Game1.player;
            if (!Context.IsWorldReady || player is null)
                return false;

            if (this.ActiveSession is not null)
                return this.ActiveSession.State == DiveSlapState.Slapping;

            return this.vanillaBridge.TryGetCaughtFishRod(player, out _);
        }

        public bool TryUseMobileDiveButton()
        {
            return this.TryStartDiveSlapSession();
        }

        public bool TryUseMobileSlapButton()
        {
            if (!Context.IsWorldReady)
                return false;

            if (this.ActiveSession is not null)
                return this.TryPerformDiveSessionSlap();

            return this.TryPerformCaughtFishSlap();
        }

        public void UpdateConfig(ModConfig config)
        {
            this.config = config;
        }

        public void OnButtonPressed(ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (this.ActiveSession is not null)
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

        public void OnUpdateTicked(uint ticks)
        {
            if (!Context.IsWorldReady)
            {
                this.ClearTransientState();
                return;
            }

            ScreenState state = this.screenStates.Value;
            this.SyncObservedCaughtFishSession(state);
            this.UpdateCaughtFishSlapSummary(state);
            this.renderer.OnUpdateTicked(this.ActiveSession, ticks);

            if (state.ActiveSession is null)
                return;

            // 跳水扇鱼的主体状态机。这里只负责阶段推进，
            // 真正的原版结算桥接放在 VanillaFishingBridge 里。
            switch (state.ActiveSession.State)
            {
                case DiveSlapState.Windup:
                    if (this.AdvancePhase(state.ActiveSession))
                    {
                        this.renderer.PlayDiveJump();
                        this.BeginPhase(
                            state.ActiveSession,
                            DiveSlapState.Diving,
                            DivingTicks,
                            state.ActiveSession.OriginalPlayerPosition,
                            this.vanillaBridge.GetDiveRenderTarget(state.ActiveSession)
                        );
                    }
                    break;

                case DiveSlapState.Diving:
                    if (this.AdvancePhase(state.ActiveSession))
                    {
                        state.ActiveSession.State = DiveSlapState.Slapping;
                        state.ActiveSession.RenderPosition = state.ActiveSession.PhaseTargetPosition;
                        state.ActiveSession.PhaseStartPosition = state.ActiveSession.PhaseTargetPosition;
                        state.ActiveSession.PhaseTargetPosition = state.ActiveSession.PhaseTargetPosition;
                        this.renderer.PlayDiveWaterEntry(state.ActiveSession.RenderPosition);
                        this.BroadcastVisualEffect(
                            state.ActiveSession,
                            MultiplayerVisualEffectKind.PlayerDiveSplash,
                            state.ActiveSession.RenderPosition
                        );
                    }
                    break;

                case DiveSlapState.Slapping:
                    state.ActiveSession.RenderPosition = state.ActiveSession.PhaseTargetPosition;
                    state.ActiveSession.RemainingSlapTicks--;
                    if (state.ActiveSession.RemainingSlapTicks <= 0)
                        this.BeginResolveFail(state.ActiveSession);
                    break;

                case DiveSlapState.ResolveSuccessPauseBefore:
                    if (this.AdvancePhase(state.ActiveSession))
                    {
                        this.BeginPhase(
                            state.ActiveSession,
                            DiveSlapState.Returning,
                            ReturningTicks,
                            state.ActiveSession.RenderPosition,
                            state.ActiveSession.OriginalPlayerPosition
                        );
                        this.renderer.PlayDiveWaterExit(state.ActiveSession.RenderPosition);
                        this.BroadcastVisualEffect(
                            state.ActiveSession,
                            MultiplayerVisualEffectKind.PlayerExitSplash,
                            state.ActiveSession.RenderPosition
                        );
                    }
                    break;

                case DiveSlapState.ResolveSuccess:
                    if (this.AdvancePhase(state.ActiveSession))
                    {
                        if (!state.ActiveSession.OutcomeApplied)
                        {
                            this.vanillaBridge.ApplySuccess(state.ActiveSession);
                            this.ShowDiveSuccessMessage(state.ActiveSession);
                            state.ActiveSession.OutcomeApplied = true;
                        }

                        this.EndSession(state);
                    }
                    break;

                case DiveSlapState.ResolveFailPauseBefore:
                    if (this.AdvancePhase(state.ActiveSession))
                    {
                        this.BeginPhase(
                            state.ActiveSession,
                            DiveSlapState.ResolveFail,
                            FailRetaliationTicks,
                            state.ActiveSession.RenderPosition,
                            state.ActiveSession.RenderPosition
                        );
                        this.renderer.PlayDiveRetaliationLaunch(state.ActiveSession.FailRetaliationStartPosition);
                        this.BroadcastVisualEffect(
                            state.ActiveSession,
                            MultiplayerVisualEffectKind.WaterSurfaceSplash,
                            state.ActiveSession.FailRetaliationStartPosition
                        );
                    }
                    break;

                case DiveSlapState.ResolveFail:
                    bool failPhaseFinished = this.AdvancePhase(state.ActiveSession);
                    if (!state.ActiveSession.FailRetaliationImpactTriggered
                        && (this.GetPhaseProgress(state.ActiveSession) >= FailRetaliationImpactProgress || failPhaseFinished))
                    {
                        this.TriggerFailRetaliationImpact(state.ActiveSession);
                    }

                    if (failPhaseFinished)
                    {
                        this.renderer.PlayDiveRetaliationSplashdown(state.ActiveSession.FailRetaliationExitPosition);
                        this.BroadcastVisualEffect(
                            state.ActiveSession,
                            MultiplayerVisualEffectKind.WaterSurfaceSplash,
                            state.ActiveSession.FailRetaliationExitPosition
                        );
                        if (!state.ActiveSession.OutcomeApplied)
                        {
                            this.vanillaBridge.ApplyFailure(state.ActiveSession);
                            state.ActiveSession.OutcomeApplied = true;
                        }

                        this.BeginPhase(
                            state.ActiveSession,
                            DiveSlapState.ResolveFailPauseAfter,
                            FailRetaliationRecoveryTicks,
                            state.ActiveSession.RenderPosition,
                            state.ActiveSession.RenderPosition
                        );
                    }
                    break;

                case DiveSlapState.ResolveFailPauseAfter:
                    if (this.AdvancePhase(state.ActiveSession))
                    {
                        this.BeginPhase(
                            state.ActiveSession,
                            DiveSlapState.Returning,
                            ReturningTicks,
                            state.ActiveSession.RenderPosition,
                            state.ActiveSession.OriginalPlayerPosition
                        );
                        this.renderer.PlayDiveWaterExit(state.ActiveSession.RenderPosition);
                        this.BroadcastVisualEffect(
                            state.ActiveSession,
                            MultiplayerVisualEffectKind.PlayerExitSplash,
                            state.ActiveSession.RenderPosition
                        );
                    }
                    break;

                case DiveSlapState.Returning:
                    if (this.AdvancePhase(state.ActiveSession))
                    {
                        if (!state.ActiveSession.OutcomeApplied)
                        {
                            this.BeginPhase(
                                state.ActiveSession,
                                DiveSlapState.ResolveSuccess,
                                ResolveSuccessTicks,
                                state.ActiveSession.RenderPosition,
                                state.ActiveSession.RenderPosition
                            );
                        }
                        else
                        {
                            this.EndSession(state);
                        }
                    }
                    break;
            }

            if (state.ActiveSession is not null)
                this.BroadcastDiveSnapshot(state.ActiveSession);
        }

        public void OnMenuChanged(MenuChangedEventArgs e)
        {
            ScreenState state = this.screenStates.Value;
            if (state.ActiveSession is null || !ReferenceEquals(e.OldMenu, state.ActiveSession.BobberBar))
                return;

            if (state.ActiveSession.State is DiveSlapState.ResolveSuccess
                or DiveSlapState.ResolveSuccessPauseBefore
                or DiveSlapState.ResolveFailPauseBefore
                or DiveSlapState.ResolveFail
                or DiveSlapState.ResolveFailPauseAfter
                or DiveSlapState.Returning)
                return;

            this.monitor.Log("Cancelled dive slap session because the BobberBar closed unexpectedly.", LogLevel.Trace);
            this.CancelSession(state);
        }

        public void OnModMessageReceived(ModMessageReceivedEventArgs e)
        {
            if (!Context.IsWorldReady || e.FromModID != this.modUniqueId)
                return;

            long currentPlayerId = Game1.player?.UniqueMultiplayerID ?? -1;
            switch (e.Type)
            {
                case MultiplayerMessageTypes.DiveSlapSync:
                    DiveSlapVisualSnapshot snapshot = e.ReadAs<DiveSlapVisualSnapshot>();
                    if (snapshot.OwnerPlayerId == currentPlayerId)
                        return;

                    this.TryPlayObservedDiveFishSplash(snapshot);
                    this.observedDiveStates[snapshot.OwnerPlayerId] = snapshot;
                    break;

                case MultiplayerMessageTypes.DiveSlapStop:
                    DiveSlapStopMessage stopMessage = e.ReadAs<DiveSlapStopMessage>();
                    this.observedDiveStates.Remove(stopMessage.OwnerPlayerId);
                    this.renderer.RemovePlayerVisuals(stopMessage.OwnerPlayerId);
                    break;

                case MultiplayerMessageTypes.CaughtFishSlap:
                    CaughtFishSlapVisualData visualData = e.ReadAs<CaughtFishSlapVisualData>();
                    if (visualData.OwnerPlayerId == currentPlayerId)
                        return;

                    this.renderer.PlayCaughtFishSlap(visualData, playLocalEffects: false);
                    break;

                case MultiplayerMessageTypes.VisualEffect:
                    MultiplayerVisualEffectData effectData = e.ReadAs<MultiplayerVisualEffectData>();
                    if (effectData.OwnerPlayerId == currentPlayerId
                        || !IsSameLocation(Game1.currentLocation?.NameOrUniqueName ?? string.Empty, effectData.LocationName))
                    {
                        return;
                    }

                    this.renderer.PlayObservedVisualEffect(effectData);
                    break;

                case MultiplayerMessageTypes.GlobalHudMessage:
                    GlobalHudMessageData hudMessage = e.ReadAs<GlobalHudMessageData>();
                    if (!string.IsNullOrWhiteSpace(hudMessage.Text))
                        Game1.addHUDMessage(HUDMessage.ForCornerTextbox(hudMessage.Text));
                    break;
            }
        }

        public void OnPeerDisconnected(PeerDisconnectedEventArgs e)
        {
            this.observedDiveStates.Remove(e.Peer.PlayerID);
            this.renderer.RemovePlayerVisuals(e.Peer.PlayerID);
        }

        public IEnumerable<DiveSlapVisualSnapshot> GetObservedDiveStatesForCurrentScreen()
        {
            string currentLocationName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
            long currentPlayerId = Game1.player?.UniqueMultiplayerID ?? -1;

            foreach (var pair in this.screenStates.GetActiveValues())
            {
                ScreenState state = pair.Value;
                if (state.ActiveSession is null
                    || state.ActiveSession.OwnerPlayerId == currentPlayerId
                    || !IsSameLocation(currentLocationName, state.ActiveSession.LocationName))
                {
                    continue;
                }

                yield return DiveSlapVisualSnapshot.FromState(state.ActiveSession);
            }

            foreach (DiveSlapVisualSnapshot snapshot in this.observedDiveStates.Values)
            {
                if (snapshot.OwnerPlayerId == currentPlayerId
                    || !IsSameLocation(currentLocationName, snapshot.LocationName))
                {
                    continue;
                }

                yield return snapshot;
            }
        }

        public bool TryDrawFarmerReplacement(Farmer farmer, SpriteBatch spriteBatch)
        {
            if (this.renderer.IsReplacementRenderFarmer(farmer))
                return false;

            if (this.TryGetDiveRenderState(farmer, out IDiveSlapRenderState? diveState) && diveState is not null)
                return this.renderer.TryDrawDiveState(spriteBatch, farmer, diveState);

            return this.renderer.TryDrawCaughtFishSlapReplacement(spriteBatch, farmer);
        }

        public bool ShouldSuppressToolDraw(Farmer farmer)
        {
            return this.renderer.ShouldSuppressToolDraw(farmer)
                || this.renderer.HasCaughtFishSlapReplacement(farmer)
                || this.TryGetDiveRenderState(farmer, out _);
        }

        public bool ShouldSuppressFishingRodDraw(FishingRod rod)
        {
            Farmer? owner = rod.lastUser;
            if (owner is null)
            {
                foreach (Farmer farmer in Game1.getAllFarmers())
                {
                    if (ReferenceEquals(farmer.CurrentTool, rod))
                    {
                        owner = farmer;
                        break;
                    }
                }
            }

            return owner is not null
                && (this.renderer.HasCaughtFishSlapReplacement(owner)
                    || this.TryGetDiveRenderState(owner, out _));
        }

        public bool ShouldSuppressFarmerShadow(Farmer farmer)
        {
            if (this.renderer.IsReplacementRenderFarmer(farmer))
                return false;

            return this.renderer.HasCaughtFishSlapReplacement(farmer)
                || this.TryGetDiveRenderState(farmer, out _);
        }

        public bool ShouldFreezeBobberBarUpdate(BobberBar bobberBar)
        {
            return this.vanillaBridge.ShouldFreezeBobberBarUpdate(bobberBar, this.ActiveSession);
        }

        public bool ShouldSuppressBobberBarDraw(BobberBar bobberBar)
        {
            return this.ActiveSession is not null
                && ReferenceEquals(this.ActiveSession.BobberBar, bobberBar);
        }

        private void HandleActiveSessionInput(ButtonPressedEventArgs e)
        {
            // 跳水会话期间必须吞掉所有 MouseLeft，否则多余的点击会泄漏给
            // 原版 FishingRod，在返回动画播放中触发错误的"鱼上钩了"流程。
            // 移动端按钮的点击已由 ModEntry.TryHandleMobileActionButtonPress 优先处理，不受影响。
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
            Farmer? player = Game1.player;
            if (player is null || !this.vanillaBridge.TryGetCaughtFishRod(player, out FishingRod? caughtFishRod) || caughtFishRod is null)
                return false;

            ScreenState state = this.screenStates.Value;
            this.RecordCaughtFishSlap(state, player, caughtFishRod);
            CaughtFishSlapVisualData visualData = CreateCaughtFishVisualData(player, caughtFishRod);
            this.renderer.PlayCaughtFishSlap(visualData, playLocalEffects: true);
            this.BroadcastCaughtFishSlap(visualData);
            this.BroadcastVisualEffect(
                player.UniqueMultiplayerID,
                visualData.LocationName,
                MultiplayerVisualEffectKind.Burst,
                player.Position + new Vector2(-16f, -64f)
            );
            return true;
        }

        private bool TryStartDiveSlapSession()
        {
            Farmer? player = Game1.player;
            if (player is null || !this.vanillaBridge.TryCreateDiveSession(player, Game1.activeClickableMenu, out DiveSlapSession? session) || session is null)
                return false;

            this.screenStates.Value.ActiveSession = session;
            StopFishingRodLoopingAudio();
            this.LockPlayerForDive(session);
            this.BeginPhase(session, DiveSlapState.Windup, WindupTicks, session.OriginalPlayerPosition, session.OriginalPlayerPosition);
            this.BroadcastDiveSnapshot(session);
            this.monitor.Log("Started dive slap session.", LogLevel.Trace);
            return true;
        }

        private bool TryPerformDiveSessionSlap()
        {
            if (this.ActiveSession is null || this.ActiveSession.State != DiveSlapState.Slapping)
                return false;

            this.ActiveSession.CurrentHits++;
            this.ActiveSession.SlapAnimationTicksRemaining = this.renderer.DiveHitTickDuration;
            this.renderer.PlayDiveSlap(this.ActiveSession, this.ActiveSession.TargetBobberPosition + new Vector2(0f, -8f));
            this.BroadcastVisualEffect(
                this.ActiveSession,
                MultiplayerVisualEffectKind.DiveSlapImpact,
                this.ActiveSession.TargetBobberPosition + new Vector2(0f, -8f)
            );

            if (this.ActiveSession.CurrentHits >= this.ActiveSession.RequiredHits)
                this.BeginResolveSuccess(this.ActiveSession);

            this.BroadcastDiveSnapshot(this.ActiveSession);
            return true;
        }

        private void BeginResolveSuccess(DiveSlapSession session)
        {
            ResetDiveSlapFishState(session);
            StopFishingRodLoopingAudio();
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
            StopFishingRodLoopingAudio();
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
            string playerName = GetPlayerDisplayName(session.Owner);
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
            this.BroadcastVisualEffect(
                session,
                MultiplayerVisualEffectKind.RetaliationImpact,
                session.FailRetaliationImpactPosition
            );
            this.ShowGlobalCornerTextbox(retaliationText);
        }

        private void ShowDiveSuccessMessage(DiveSlapSession session)
        {
            int elapsedTicks = Math.Max(1, session.TotalSlapTicks - session.RemainingSlapTicks);
            float elapsedSeconds = Math.Max(0.1f, elapsedTicks / 60f);
            string timeText = elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string playerName = GetPlayerDisplayName(session.Owner);
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
            this.ShowGlobalCornerTextbox(successText);
        }

        private void RecordCaughtFishSlap(ScreenState screenState, Farmer player, FishingRod rod)
        {
            this.SyncObservedCaughtFishSession(screenState, rod);

            if (screenState.ActiveCaughtFishSlapSummary is not null
                && !IsSameCaughtFishSession(screenState.ActiveCaughtFishSlapSummary, screenState, rod))
            {
                this.CompleteCaughtFishSlapSummary(screenState, showMessage: true);
            }

            uint currentTick = (uint)Game1.ticks;
            string fishDisplayName = ResolveCaughtFishDisplayName(rod);
            if (screenState.ActiveCaughtFishSlapSummary is null)
            {
                screenState.ActiveCaughtFishSlapSummary = new CaughtFishSlapSummary
                {
                    Rod = rod,
                    CaughtFishSessionId = screenState.CurrentCaughtFishSessionId,
                    FishDisplayName = fishDisplayName,
                    PlayerName = GetPlayerDisplayName(player),
                    FirstSlapTick = currentTick,
                    LastSlapTick = currentTick,
                    SlapCount = 1
                };
                return;
            }

            screenState.ActiveCaughtFishSlapSummary.Rod = rod;
            screenState.ActiveCaughtFishSlapSummary.CaughtFishSessionId = screenState.CurrentCaughtFishSessionId;
            screenState.ActiveCaughtFishSlapSummary.FishDisplayName = fishDisplayName;
            screenState.ActiveCaughtFishSlapSummary.PlayerName = GetPlayerDisplayName(player);
            screenState.ActiveCaughtFishSlapSummary.LastSlapTick = currentTick;
            screenState.ActiveCaughtFishSlapSummary.SlapCount++;
        }

        private void UpdateCaughtFishSlapSummary(ScreenState screenState)
        {
            if (screenState.ActiveCaughtFishSlapSummary is null)
                return;

            Farmer? player = Game1.player;
            if (player is not null
                && this.vanillaBridge.TryGetCaughtFishRod(player, out FishingRod? caughtFishRod)
                && caughtFishRod is not null
                && IsSameCaughtFishSession(screenState.ActiveCaughtFishSlapSummary, screenState, caughtFishRod))
            {
                return;
            }

            this.CompleteCaughtFishSlapSummary(screenState, showMessage: true);
        }

        private void CompleteCaughtFishSlapSummary(ScreenState screenState, bool showMessage)
        {
            if (screenState.ActiveCaughtFishSlapSummary is null)
                return;

            CaughtFishSlapSummary summary = screenState.ActiveCaughtFishSlapSummary;
            screenState.ActiveCaughtFishSlapSummary = null;
            if (showMessage)
                this.ShowCaughtFishSlapMessage(summary);
        }

        private void ShowCaughtFishSlapMessage(CaughtFishSlapSummary summary)
        {
            if (summary.SlapCount <= 0)
                return;

            uint endTick = Math.Max(summary.FirstSlapTick, summary.LastSlapTick);
            int elapsedTicks = Math.Max(1, (int)(endTick - summary.FirstSlapTick));
            float elapsedSeconds = Math.Max(0.1f, elapsedTicks / 60f);
            string timeText = elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string successText = this.helper.Translation
                .Get(
                    "hud.caught-slap-success",
                    new
                    {
                        player = summary.PlayerName,
                        time = timeText,
                        fish = summary.FishDisplayName,
                        slap_num = summary.SlapCount
                    }
                )
                .ToString();
            this.ShowGlobalCornerTextbox(successText);
        }

        private void SyncObservedCaughtFishSession(ScreenState screenState)
        {
            Farmer? player = Game1.player;
            if (player is null || !this.vanillaBridge.TryGetCaughtFishRod(player, out FishingRod? rod) || rod is null)
            {
                ResetObservedCaughtFishSession(screenState);
                return;
            }

            this.SyncObservedCaughtFishSession(screenState, rod);
        }

        private void SyncObservedCaughtFishSession(ScreenState screenState, FishingRod rod)
        {
            string qualifiedFishId = rod.whichFish?.QualifiedItemId ?? string.Empty;
            if (!ReferenceEquals(screenState.ObservedCaughtFishRod, rod)
                || !string.Equals(screenState.ObservedCaughtFishQualifiedItemId, qualifiedFishId, StringComparison.Ordinal)
                || screenState.ObservedCaughtFishSize != rod.fishSize
                || screenState.ObservedCaughtFishQuality != rod.fishQuality
                || screenState.ObservedCaughtFishBoss != rod.bossFish)
            {
                screenState.CurrentCaughtFishSessionId++;
                screenState.ObservedCaughtFishRod = rod;
                screenState.ObservedCaughtFishQualifiedItemId = qualifiedFishId;
                screenState.ObservedCaughtFishSize = rod.fishSize;
                screenState.ObservedCaughtFishQuality = rod.fishQuality;
                screenState.ObservedCaughtFishBoss = rod.bossFish;
            }
        }

        private static void ResetObservedCaughtFishSession(ScreenState screenState)
        {
            screenState.ObservedCaughtFishRod = null;
            screenState.ObservedCaughtFishQualifiedItemId = string.Empty;
            screenState.ObservedCaughtFishSize = 0;
            screenState.ObservedCaughtFishQuality = 0;
            screenState.ObservedCaughtFishBoss = false;
        }

        private static bool IsSameCaughtFishSession(CaughtFishSlapSummary summary, ScreenState screenState, FishingRod rod)
        {
            return ReferenceEquals(summary.Rod, rod)
                && summary.CaughtFishSessionId == screenState.CurrentCaughtFishSessionId;
        }

        private static string ResolveCaughtFishDisplayName(FishingRod rod)
        {
            if (rod.whichFish is null)
                return "???";

            var fishData = rod.whichFish.GetParsedOrErrorData();
            return fishData.DisplayName ?? rod.whichFish.QualifiedItemId ?? "???";
        }

        private static CaughtFishSlapVisualData CreateCaughtFishVisualData(Farmer player, FishingRod rod)
        {
            return new CaughtFishSlapVisualData
            {
                OwnerPlayerId = player.UniqueMultiplayerID,
                LocationName = player.currentLocation?.NameOrUniqueName ?? string.Empty,
                FacingDirection = player.FacingDirection,
                FishQualifiedItemId = rod.whichFish?.QualifiedItemId ?? string.Empty,
                FishDisplayName = ResolveCaughtFishDisplayName(rod),
                NumberOfFishCaught = Math.Max(1, rod.numberOfFishCaught),
                FishSize = rod.fishSize,
                BossFish = rod.bossFish,
                RecordSize = rod.recordSize
            };
        }

        private static string GetPlayerDisplayName(Farmer player)
        {
            return string.IsNullOrWhiteSpace(player.Name) ? "Player" : player.Name;
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
            Farmer player = session.Owner;
            player.Halt();
            player.canMove = false;
            player.freezePause = Math.Max(1, player.freezePause);
            player.faceDirection(session.CastFacingDirection);
        }

        private void RestorePlayerFromDive(DiveSlapSession session)
        {
            Farmer player = session.Owner;
            player.Position = session.OriginalPlayerPosition;
            player.FacingDirection = session.PreviousFacingDirection;
            player.Halt();

            if (session.OutcomeApplied)
            {
                // 成功/失败分支都已经走完原版收尾逻辑，这里必须强制解锁玩家。
                // 如果把进入 BobberBar 之前的锁定状态原样恢复，会再次把人卡死。
                player.forceCanMove();
                player.freezePause = 0;
                player.canMove = true;
            }
            else
            {
                player.canMove = session.PreviousCanMove;
                player.freezePause = session.PreviousFreezePause;
            }

            if (session.OwnerPlayerId == (Game1.player?.UniqueMultiplayerID ?? -1))
                this.renderer.ResetLocalPlayerPose(session.PreviousFacingDirection);
        }

        private void EndSession(ScreenState screenState)
        {
            if (screenState.ActiveSession is null)
                return;

            DiveSlapSession completedSession = screenState.ActiveSession;
            StopFishingRodLoopingAudio();
            ResetFishingRodPostDiveVisualState(completedSession.Owner, completedSession.Rod);
            this.RestorePlayerFromDive(completedSession);
            screenState.ActiveSession = null;
            this.ApplyDiveCostOnReturn(completedSession.Owner);
            this.BroadcastDiveStop(completedSession.OwnerPlayerId);
        }

        private void CancelSession(ScreenState screenState)
        {
            if (screenState.ActiveSession is null)
                return;

            StopFishingRodLoopingAudio();
            ResetFishingRodPostDiveVisualState(screenState.ActiveSession.Owner, screenState.ActiveSession.Rod);
            this.RestorePlayerFromDive(screenState.ActiveSession);
            long ownerPlayerId = screenState.ActiveSession.OwnerPlayerId;
            screenState.ActiveSession = null;
            this.BroadcastDiveStop(ownerPlayerId);
        }

        private void BroadcastDiveSnapshot(DiveSlapSession session)
        {
            if (!Context.IsMultiplayer)
                return;

            this.helper.Multiplayer.SendMessage(
                DiveSlapVisualSnapshot.FromState(session),
                MultiplayerMessageTypes.DiveSlapSync,
                new[] { this.modUniqueId }
            );
        }

        private void BroadcastDiveStop(long ownerPlayerId)
        {
            if (!Context.IsMultiplayer)
                return;

            this.helper.Multiplayer.SendMessage(
                new DiveSlapStopMessage { OwnerPlayerId = ownerPlayerId },
                MultiplayerMessageTypes.DiveSlapStop,
                new[] { this.modUniqueId }
            );
        }

        private void BroadcastCaughtFishSlap(CaughtFishSlapVisualData visualData)
        {
            if (!Context.IsMultiplayer)
                return;

            this.helper.Multiplayer.SendMessage(
                visualData,
                MultiplayerMessageTypes.CaughtFishSlap,
                new[] { this.modUniqueId }
            );
        }

        private void BroadcastVisualEffect(DiveSlapSession session, MultiplayerVisualEffectKind effectKind, Vector2 worldPosition)
        {
            this.BroadcastVisualEffect(session.OwnerPlayerId, session.LocationName, effectKind, worldPosition);
        }

        private void BroadcastVisualEffect(long ownerPlayerId, string locationName, MultiplayerVisualEffectKind effectKind, Vector2 worldPosition)
        {
            if (!Context.IsMultiplayer)
                return;

            this.helper.Multiplayer.SendMessage(
                new MultiplayerVisualEffectData
                {
                    OwnerPlayerId = ownerPlayerId,
                    LocationName = locationName,
                    EffectKind = effectKind,
                    WorldPosition = worldPosition
                },
                MultiplayerMessageTypes.VisualEffect,
                new[] { this.modUniqueId }
            );
        }

        private void ShowGlobalCornerTextbox(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            Game1.addHUDMessage(HUDMessage.ForCornerTextbox(text));
            if (!Context.IsMultiplayer)
                return;

            this.helper.Multiplayer.SendMessage(
                new GlobalHudMessageData { Text = text },
                MultiplayerMessageTypes.GlobalHudMessage,
                new[] { this.modUniqueId }
            );
        }

        private void TryPlayObservedDiveFishSplash(DiveSlapVisualSnapshot snapshot)
        {
            string currentLocationName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
            if (!IsSameLocation(currentLocationName, snapshot.LocationName)
                || !this.observedDiveStates.TryGetValue(snapshot.OwnerPlayerId, out DiveSlapVisualSnapshot? previousSnapshot)
                || !IsSameLocation(previousSnapshot.LocationName, snapshot.LocationName)
                || previousSnapshot.SlapFishOffsetY >= 0f
                || snapshot.SlapFishOffsetY < 0f)
            {
                return;
            }

            this.renderer.PlayObservedVisualEffect(
                new MultiplayerVisualEffectData
                {
                    OwnerPlayerId = snapshot.OwnerPlayerId,
                    LocationName = snapshot.LocationName,
                    EffectKind = MultiplayerVisualEffectKind.WaterSurfaceSplash,
                    WorldPosition = snapshot.SlapFishSurfacePosition
                }
            );
        }

        private bool TryGetDiveRenderState(Farmer farmer, out IDiveSlapRenderState? state)
        {
            long playerId = farmer.UniqueMultiplayerID;
            string farmerLocationName = farmer.currentLocation?.NameOrUniqueName ?? string.Empty;

            foreach (var pair in this.screenStates.GetActiveValues())
            {
                ScreenState screenState = pair.Value;
                if (screenState.ActiveSession is not null
                    && screenState.ActiveSession.OwnerPlayerId == playerId
                    && IsSameLocation(farmerLocationName, screenState.ActiveSession.LocationName))
                {
                    state = screenState.ActiveSession;
                    return true;
                }
            }

            if (this.observedDiveStates.TryGetValue(playerId, out DiveSlapVisualSnapshot? snapshot)
                && IsSameLocation(farmerLocationName, snapshot.LocationName))
            {
                state = snapshot;
                return true;
            }

            state = null;
            return false;
        }

        private static bool IsSameLocation(string left, string right)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        private static void StopFishingRodLoopingAudio()
        {
            StopFishingRodCue(FishingRodChargeSoundField);
            StopFishingRodCue(FishingRodReelSoundField);
            StopFishingRodCue(BobberBarReelSoundField);
            StopFishingRodCue(BobberBarUnReelSoundField);
        }

        private void LogMissingReflectionBindings()
        {
            List<string> missingBindings = new();
            AddMissingBindingName(missingBindings, FishingRodChargeSoundField, "FishingRod.chargeSound");
            AddMissingBindingName(missingBindings, FishingRodReelSoundField, "FishingRod.reelSound");
            AddMissingBindingName(missingBindings, FishingRodHadBobberField, "FishingRod.hadBobber");
            AddMissingBindingName(missingBindings, FishingRodPlayerAdjustedBobberField, "FishingRod._hasPlayerAdjustedBobber");
            AddMissingBindingName(missingBindings, FishingRodBobberBobField, "FishingRod.bobberBob");
            AddMissingBindingName(missingBindings, FishingRodBobberTimeAccumulatorField, "FishingRod.bobberTimeAccumulator");
            AddMissingBindingName(missingBindings, FishingRodTimePerBobberBobField, "FishingRod.timePerBobberBob");
            AddMissingBindingName(missingBindings, BobberBarReelSoundField, "BobberBar.reelSound");
            AddMissingBindingName(missingBindings, BobberBarUnReelSoundField, "BobberBar.unReelSound");

            if (missingBindings.Count == 0)
                return;

            this.monitor.Log(
                $"Missing internal field bindings: {string.Join(", ", missingBindings)}. Dive audio cleanup or bobber visual reset may be degraded on this game version.",
                LogLevel.Warn);
        }

        private static void AddMissingBindingName(List<string> missingBindings, FieldInfo? field, string bindingName)
        {
            if (field is null)
                missingBindings.Add(bindingName);
        }

        private static void ResetFishingRodPostDiveVisualState(Farmer player, FishingRod rod)
        {
            rod.bobber.Set(Vector2.Zero);
            rod.castedButBobberStillInAir = false;
            rod.pullingOutOfWater = false;
            player.armOffset = Vector2.Zero;

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

        private void ApplyDiveCostOnReturn(Farmer player)
        {
            float oldStamina = player.Stamina;

            // 低血量会直接猝死是玩法设计，不在这里做保底夹取。
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

        private void ClearTransientState()
        {
            this.observedDiveStates.Clear();
            this.renderer.ClearTransientState();
            this.screenStates.ResetAllScreens();
        }
    }
}
