using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using FishSlapper.Gameplay;
using System;
using System.Collections.Generic;

namespace FishSlapper.Vanilla
{
    internal sealed class VanillaFishingBridge
    {
        private static readonly Vector2 DiveStrikeToFarmerOffsetRight = new(-44f, 0f);
        private static readonly Vector2 DiveStrikeToFarmerOffsetLeft = new(-20f, 0f);
        private static readonly Vector2 DiveStrikeToFarmerOffsetUp = new(-32f, 0f);
        private const float UpCastRightSideDeadZone = 16f;

        public bool TryGetCaughtFishRod(out FishingRod? rod)
        {
            rod = Game1.player?.CurrentTool as FishingRod;
            return rod is not null && rod.fishCaught;
        }

        public bool CanCreateDiveSession()
        {
            if (!Context.IsWorldReady
                || Game1.player.CurrentTool is not FishingRod rod
                || Game1.activeClickableMenu is not BobberBar)
            {
                return false;
            }

            if (rod.fishCaught || rod.pullingOutOfWater || rod.castedButBobberStillInAir)
                return false;

            Vector2 bobberPosition = rod.bobber.Get();
            return bobberPosition.LengthSquared() > 1f
                && !float.IsNaN(bobberPosition.X)
                && !float.IsNaN(bobberPosition.Y)
                && !float.IsInfinity(bobberPosition.X)
                && !float.IsInfinity(bobberPosition.Y);
        }

        public bool TryCreateDiveSession(out DiveSlapSession? session)
        {
            session = null;

            // CanCreateDiveSession 已验证 FishingRod/BobberBar 存在，
            // 此处的模式匹配仅用于 C# 类型窄化以获取局部变量。
            if (!this.CanCreateDiveSession()
                || Game1.player.CurrentTool is not FishingRod rod
                || Game1.activeClickableMenu is not BobberBar bobberBar)
                return false;

            Vector2 bobberPosition = rod.bobber.Get();
            Vector2 originalPlayerPosition = Game1.player.Position;
            int castFacingDirection = ResolveDiveCastFacingDirection(rod, originalPlayerPosition, bobberPosition);
            DiveDifficultyProfile difficultyProfile = ResolveDiveDifficultyProfile(bobberBar);
            string qualifiedFishId = bobberBar.whichFish;
            var fishMetadata = ItemRegistry.GetMetadata(qualifiedFishId);
            var fishData = fishMetadata.GetParsedOrErrorData();
            Vector2 slapFishSurfacePosition = bobberPosition + new Vector2(20f, 6f);
            Vector2 retaliationImpactPosition = GetFailRetaliationImpactPosition(bobberPosition, castFacingDirection);
            Vector2 retaliationStartPosition = retaliationImpactPosition + new Vector2(124f, 52f);
            Vector2 retaliationExitPosition = retaliationImpactPosition + new Vector2(-148f, 58f);

            session = new DiveSlapSession
            {
                Rod = rod,
                BobberBar = bobberBar,
                OriginalPlayerPosition = originalPlayerPosition,
                TargetBobberPosition = bobberPosition,
                CastFacingDirection = castFacingDirection,
                FacingRight = ResolveDiveStrikeSide(castFacingDirection, originalPlayerPosition, bobberPosition),
                PreviousFacingDirection = Game1.player.FacingDirection,
                PreviousCanMove = Game1.player.canMove,
                PreviousFreezePause = Game1.player.freezePause,
                RequiredHits = difficultyProfile.RequiredHits,
                TotalSlapTicks = difficultyProfile.DurationTicks,
                RemainingSlapTicks = difficultyProfile.DurationTicks,
                TargetFishQualifiedItemId = qualifiedFishId,
                TargetFishDisplayName = fishData.DisplayName ?? qualifiedFishId,
                SlapFishSurfacePosition = slapFishSurfacePosition,
                FailRetaliationStartPosition = retaliationStartPosition,
                FailRetaliationImpactPosition = retaliationImpactPosition,
                FailRetaliationExitPosition = retaliationExitPosition,
                FailRetaliationArcHeight = 92f,
                RenderPosition = originalPlayerPosition,
                PhaseStartPosition = originalPlayerPosition,
                PhaseTargetPosition = originalPlayerPosition
            };
            return true;
        }

        public Vector2 GetDiveRenderTarget(DiveSlapSession session)
        {
            // 把“鱼钩命中点”映射到 fake farmer 的手部位置。
            // 这里保留经验偏移量，方便后续继续按观感微调。
            Vector2 renderOffset = session.CastFacingDirection == 0
                ? DiveStrikeToFarmerOffsetUp
                : session.FacingRight
                    ? DiveStrikeToFarmerOffsetRight
                    : DiveStrikeToFarmerOffsetLeft;
            return session.TargetBobberPosition + renderOffset;
        }

        public bool ShouldFreezeBobberBarUpdate(BobberBar bobberBar, DiveSlapSession? session)
        {
            return session is not null
                && ReferenceEquals(session.BobberBar, bobberBar)
                && session.State is DiveSlapState.Windup
                    or DiveSlapState.Diving
                    or DiveSlapState.Slapping
                    or DiveSlapState.ResolveSuccessPauseBefore
                    or DiveSlapState.ResolveSuccess
                    or DiveSlapState.ResolveFailPauseBefore
                    or DiveSlapState.ResolveFail
                    or DiveSlapState.ResolveFailPauseAfter;
        }

        public void ApplySuccess(DiveSlapSession session)
        {
            BobberBar bobberBar = session.BobberBar;
            FishingRod rod = session.Rod;
            // 跳水成功固定取消 perfect，不再提供配置开关。
            bool wasPerfect = false;
            int resolvedFishQuality = ResolveFishQuality(bobberBar.fishQuality, wasPerfect);

            if (!bobberBar.fromFishPond && bobberBar.whichFish.StartsWith("(O)", StringComparison.Ordinal))
                AwardFishingExperience(bobberBar, wasPerfect);

            rod.lastUser = Game1.player;
            rod.originalFacingDirection = Game1.player.FacingDirection;
            rod.whichFish = ItemRegistry.GetMetadata(bobberBar.whichFish);
            rod.fishSize = bobberBar.fishSize;
            rod.fishQuality = resolvedFishQuality;
            rod.treasureCaught = false;
            rod.fromFishPond = bobberBar.fromFishPond;
            rod.setFlagOnCatch = string.IsNullOrEmpty(bobberBar.setFlagOnCatch) ? null : bobberBar.setFlagOnCatch;
            rod.numberOfFishCaught = ResolveNumberOfFishCaught(rod, bobberBar);
            rod.bossFish = bobberBar.bossFish;
            rod.fishCaught = false;
            rod.pullingOutOfWater = false;
            rod.isFishing = false;
            rod.isReeling = false;

            // 成功后直接补完原版需要的渔获数据，然后调用收尾方法，
            // 但绕过 pullingOutOfWater，那段原版收杆动作会和“人已回岸”冲突。
            bobberBar.handledFishResult = true;
            if (ReferenceEquals(Game1.activeClickableMenu, bobberBar))
            {
                bobberBar.emergencyShutDown();
                Game1.activeClickableMenu = null;
            }

            Game1.playSound("jingle1");
            rod.playerCaughtFishEndFunction(bobberBar.bossFish);
            if (!Game1.isFestival())
                rod.doneHoldingFish(Game1.player, false);
        }

        public void ApplyFailure(DiveSlapSession session)
        {
            if (ReferenceEquals(Game1.activeClickableMenu, session.BobberBar))
                session.BobberBar.emergencyShutDown();

            Game1.playSound("fishEscape");
            Game1.activeClickableMenu = null;
            session.Rod.doneFishing(Game1.player, true);
        }

        private static int ResolveFishQuality(int baseFishQuality, bool wasPerfect)
        {
            if (baseFishQuality >= 2 && wasPerfect)
                return 4;

            if (baseFishQuality >= 1 && wasPerfect)
                return 2;

            return Math.Max(0, baseFishQuality);
        }

        private static int ResolveNumberOfFishCaught(FishingRod rod, BobberBar bobberBar)
        {
            int numCaught = 1;
            string? baitId = rod.GetBait()?.QualifiedItemId;

            if (!bobberBar.bossFish && string.Equals(baitId, "(O)774", StringComparison.Ordinal))
                numCaught = Game1.random.NextDouble() < 0.25 + Game1.player.DailyLuck / 2.0 ? 1 : 2;

            if (bobberBar.challengeBaitFishes > 0)
                numCaught = bobberBar.challengeBaitFishes;

            return Math.Max(1, numCaught);
        }

        private static void AwardFishingExperience(BobberBar bobberBar, bool wasPerfect)
        {
            int experience = Math.Max(1, (bobberBar.fishQuality + 1) * 3 + (int)bobberBar.difficulty / 3);
            if (bobberBar.treasureCaught)
                experience += (int)(experience * 1.2f);

            if (wasPerfect)
                experience += (int)(experience * 1.4f);

            if (bobberBar.bossFish)
                experience *= 5;

            Game1.player.gainExperience(Farmer.fishingSkill, experience);
        }

        private static DiveDifficultyProfile ResolveDiveDifficultyProfile(BobberBar bobberBar)
        {
            int requiredHits;
            float durationSeconds;

            if (bobberBar.bossFish || bobberBar.difficulty >= 105f)
            {
                requiredHits = 9;
                durationSeconds = 2f;
            }
            else if (bobberBar.difficulty < 40f)
            {
                requiredHits = 5;
                durationSeconds = 3f;
            }
            else if (bobberBar.difficulty < 70f)
            {
                requiredHits = 6;
                durationSeconds = 2.7f;
            }
            else if (bobberBar.difficulty < 90f)
            {
                requiredHits = 7;
                durationSeconds = 2.5f;
            }
            else
            {
                requiredHits = 8;
                durationSeconds = 2.2f;
            }

            if (TryGetFishBehavior(bobberBar.whichFish, out DiveFishBehavior behavior))
            {
                switch (behavior)
                {
                    case DiveFishBehavior.Mixed:
                        requiredHits += 1;
                        durationSeconds -= 0.2f;
                        break;

                    case DiveFishBehavior.Sinker:
                        requiredHits += 1;
                        break;

                    case DiveFishBehavior.Dart:
                        requiredHits += 1;
                        durationSeconds -= 0.2f;
                        break;
                }
            }

            int durationTicks = (int)MathF.Round(durationSeconds * 60f, MidpointRounding.AwayFromZero);
            return new DiveDifficultyProfile(
                Math.Max(1, requiredHits),
                Math.Max(1, durationTicks)
            );
        }

        private static bool TryGetFishBehavior(string qualifiedFishId, out DiveFishBehavior behavior)
        {
            behavior = DiveFishBehavior.Unknown;

            string itemId = ExtractFishItemId(qualifiedFishId);
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            Dictionary<string, string> fishData = Game1.content.Load<Dictionary<string, string>>("Data\\Fish");
            if (!fishData.TryGetValue(itemId, out string? rawData) || string.IsNullOrWhiteSpace(rawData))
                return false;

            string[] fields = rawData.Split('/');
            if (fields.Length < 3)
                return false;

            behavior = ParseFishBehavior(fields[2]);
            return behavior != DiveFishBehavior.Unknown;
        }

        private static string ExtractFishItemId(string qualifiedFishId)
        {
            const string objectPrefix = "(O)";
            return qualifiedFishId.StartsWith(objectPrefix, StringComparison.Ordinal)
                ? qualifiedFishId[objectPrefix.Length..]
                : qualifiedFishId;
        }

        private static DiveFishBehavior ParseFishBehavior(string rawBehavior)
        {
            return rawBehavior.Trim().ToLowerInvariant() switch
            {
                "mixed" => DiveFishBehavior.Mixed,
                "sinker" => DiveFishBehavior.Sinker,
                "dart" => DiveFishBehavior.Dart,
                "smooth" => DiveFishBehavior.Smooth,
                "floater" => DiveFishBehavior.Floater,
                _ => DiveFishBehavior.Unknown
            };
        }

        private static Vector2 GetFailRetaliationImpactPosition(Vector2 bobberPosition, int castFacingDirection)
        {
            Vector2 renderOffset = castFacingDirection == 0
                ? DiveStrikeToFarmerOffsetUp
                : castFacingDirection == 3
                    ? DiveStrikeToFarmerOffsetLeft
                    : DiveStrikeToFarmerOffsetRight;
            Vector2 diveRenderPosition = bobberPosition + renderOffset;
            return diveRenderPosition + new Vector2(34f, -38f);
        }

        private static int ResolveDiveCastFacingDirection(FishingRod rod, Vector2 originalPlayerPosition, Vector2 bobberPosition)
        {
            // 优先使用 FishingRod 自己记录的抛竿方向。
            // 如果某些情况下拿不到，再逐级退化到原朝向/玩家朝向/鱼钩相对位置。
            int direction = rod.CastDirection;
            if (IsFacingDirection(direction))
                return direction;

            direction = rod.originalFacingDirection;
            if (IsFacingDirection(direction))
                return direction;

            direction = Game1.player.FacingDirection;
            if (IsFacingDirection(direction))
                return direction;

            Vector2 delta = bobberPosition - originalPlayerPosition;
            if (Math.Abs(delta.Y) >= Math.Abs(delta.X))
                return delta.Y >= 0f ? 2 : 0;

            return delta.X >= 0f ? 1 : 3;
        }

        private static bool IsFacingDirection(int direction)
        {
            return direction is >= 0 and <= 3;
        }

        private static bool ResolveDiveStrikeSide(int castFacingDirection, Vector2 originalPlayerPosition, Vector2 bobberPosition)
        {
            if (castFacingDirection == 1)
                return true;

            if (castFacingDirection == 3)
                return false;

            float deltaX = bobberPosition.X - originalPlayerPosition.X;
            if (castFacingDirection == 0 && Math.Abs(deltaX) < UpCastRightSideDeadZone)
                return true;

            return deltaX >= 0f;
        }

        private enum DiveFishBehavior
        {
            Unknown,
            Smooth,
            Mixed,
            Dart,
            Floater,
            Sinker
        }

        private readonly record struct DiveDifficultyProfile(int RequiredHits, int DurationTicks);
    }
}
