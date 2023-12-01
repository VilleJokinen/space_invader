// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Cloud.Tests
{
    // Tests regarding various features of MetaActivables. Expressed as tests on "test offers" (TestOfferInfo etc.).
    // \note There are some clunky extra player.Tick(null) calls in some places due to the fact that
    //       GameTick gets called *before* CurrentTick has been bumped.

    //[TestFixture(ActivableTestsFlags.None)]
    [TestFixture(ActivableTestsFlags.NoOpTryAdjustActivations)]
    public class ActivableTests
    {
        [Flags]
        public enum ActivableTestsFlags
        {
            None = 0,

            /// <summary>
            /// Call <see cref="MetaActivableSet{TId, TInfo, TActivableState}.TryAdjustEachActivation"/>
            /// on each tick, asserting that no adjustment is done (i.e. that the method returns 0).
            /// Meant to test that if there are no config changes, adjustment does nothing.
            /// </summary>
            NoOpTryAdjustActivations = 1 << 0,
        }

        ActivableTestsFlags _flags;

        public ActivableTests(ActivableTestsFlags flags)
        {
            _flags = flags;
        }

        #region Test segments, offers, and config

        [MetaSerializableDerived(99999)]
        public class TestPlayerPropertyIdGold : TypedPlayerPropertyId<int>
        {
            public override int GetTypedValueForPlayer(IPlayerModelBase player) => ((ActivableTestPlayerModel)player).NumGold;
            public override string DisplayName => "Test Gold";
        }

        [MetaSerializable]
        public class TestPlayerSegmentInfo : PlayerSegmentInfoBase
        {
            public TestPlayerSegmentInfo(){ }
            public TestPlayerSegmentInfo(PlayerSegmentId segmentId, PlayerCondition playerCondition)
                : base(segmentId, playerCondition, displayName: "test name", description: "test description")
            {
            }
        }

        [MetaSerializable]
        public class TestOfferId : StringId<TestOfferId> { }

        public class TestOfferInfo : IMetaActivableConfigData<TestOfferId>
        {
            public TestOfferId          OfferId         { get; private set; }
            public MetaActivableParams  ActivableParams { get; private set; }

            public TestOfferId ActivableId => OfferId;

            public string DisplayName => "dummy name";
            public string Description => "dummy description";
            public string DisplayShortInfo => null;
            public TestOfferId ConfigKey => OfferId;

            public TestOfferInfo(){ }
            public TestOfferInfo(TestOfferId offerId, MetaActivableParams activableParams)
            {
                OfferId = offerId ?? throw new ArgumentNullException(nameof(offerId));
                ActivableParams = activableParams ?? throw new ArgumentNullException(nameof(activableParams));
            }
        }

        [MetaSerializableDerived(3)]
        public class TestOfferPrecursorCondition : MetaActivablePrecursorCondition<TestOfferId>
        {
            TestOfferPrecursorCondition() { }
            public TestOfferPrecursorCondition(TestOfferId id, bool consumed, MetaDuration delay) : base(id, consumed, delay) { }

            protected override IMetaActivableSet<TestOfferId> GetActivableSet(IPlayerModelBase player)
                => ((ActivableTestPlayerModel)player).TestOffers;
        }

        public class ActivableTestGameConfig : SharedGameConfigBase, IGameConfigDataResolver
        {
            public OrderedDictionary<PlayerSegmentId, TestPlayerSegmentInfo> Segments { get; private set; }
            public OrderedDictionary<TestOfferId, TestOfferInfo> TestOffers { get; private set; }

            public ActivableTestGameConfig()
            {
                Segments = new List<TestPlayerSegmentInfo>
                {
                    new TestPlayerSegmentInfo(
                        PlayerSegmentId.FromString("LowGold"),
                        new PlayerSegmentBasicCondition(
                            new List<PlayerPropertyRequirement>{ PlayerPropertyRequirement.ParseFromStrings(new TestPlayerPropertyIdGold(), null, "100") },
                            requireAnySegment: null,
                            requireAllSegments: null)),
                    new TestPlayerSegmentInfo(
                        PlayerSegmentId.FromString("HighGold"),
                        new PlayerSegmentBasicCondition(
                            new List<PlayerPropertyRequirement>{ PlayerPropertyRequirement.ParseFromStrings(new TestPlayerPropertyIdGold(), "1000", null) },
                            requireAnySegment: null,
                            requireAllSegments: null))
                }.ToOrderedDictionary(info => info.SegmentId);

                MetaScheduleBase testUtcSchedule = new MetaRecurringCalendarSchedule(
                    timeMode:   MetaScheduleTimeMode.Utc,
                    start:      new MetaCalendarDateTime(2021, 6, 8, 10, 30, 0),
                    duration:   new MetaCalendarPeriod{ Minutes = 10 },
                    endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                    preview:    new MetaCalendarPeriod{ Minutes = 1 },
                    review:     new MetaCalendarPeriod{ Minutes = 3 },
                    recurrence: new MetaCalendarPeriod{ Hours = 1 },
                    numRepeats: 3);

                MetaScheduleBase testLocalSchedule = new MetaRecurringCalendarSchedule(
                    timeMode:   MetaScheduleTimeMode.Local,
                    start:      new MetaCalendarDateTime(2021, 6, 8, 10, 30, 0),
                    duration:   new MetaCalendarPeriod{ Minutes = 10 },
                    endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                    preview:    new MetaCalendarPeriod{ Minutes = 1 },
                    review:     new MetaCalendarPeriod{ Minutes = 3 },
                    recurrence: new MetaCalendarPeriod{ Hours = 1 },
                    numRepeats: 3);

                TestOffers = new List<TestOfferInfo>
                {
                    // Offer targeting a segment
                    new TestOfferInfo(
                        TestOfferId.FromString("BasicLowGold"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   new List<MetaRef<PlayerSegmentInfoBase>>{ MetaRef<PlayerSegmentInfoBase>.FromItem(Segments[PlayerSegmentId.FromString("LowGold")]) },
                            additionalConditions:       null,
                            lifetime:                   MetaActivableLifetimeSpec.Forever.Instance,
                            isTransient:                false,
                            schedule:                   null,
                            maxActivations:             null,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   null,
                            cooldown:                   MetaActivableCooldownSpec.Fixed.Zero,
                            allowActivationAdjustment:  true)),

                    // Offer with activation and consumption limits
                    new TestOfferInfo(
                        TestOfferId.FromString("Limits"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       null,
                            lifetime:                   new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(5)),
                            isTransient:                false,
                            schedule:                   null,
                            maxActivations:             6,
                            maxTotalConsumes:           8,
                            maxConsumesPerActivation:   2,
                            cooldown:                   MetaActivableCooldownSpec.Fixed.Zero,
                            allowActivationAdjustment:  true)),

                    // Offer with fixed lifetime and cooldown
                    new TestOfferInfo(
                        TestOfferId.FromString("LifetimeAndCooldown"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       null,
                            lifetime:                   new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(5)),
                            isTransient:                false,
                            schedule:                   null,
                            maxActivations:             3,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   1,
                            cooldown:                   new MetaActivableCooldownSpec.Fixed(MetaDuration.FromMinutes(3)),
                            allowActivationAdjustment:  true)),

                    // Depdendency offer in precursor test
                    new TestOfferInfo(
                        TestOfferId.FromString("Precursor"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       null,
                            lifetime:                   new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(5)),
                            isTransient:                false,
                            schedule:                   null,
                            maxActivations:             1,
                            maxTotalConsumes:           1,
                            maxConsumesPerActivation:   null,
                            cooldown:                   MetaActivableCooldownSpec.Fixed.Zero,
                            allowActivationAdjustment:  true)),

                    // Positive depdendent offer in precursor test
                    new TestOfferInfo(
                        TestOfferId.FromString("PositivelyDependentOnPrecursor"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       new List<PlayerCondition>{ new TestOfferPrecursorCondition(TestOfferId.FromString("Precursor"), consumed: true, delay: MetaDuration.FromMinutes(1)) },
                            lifetime:                   new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(5)),
                            isTransient:                false,
                            schedule:                   null,
                            maxActivations:             1,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   null,
                            cooldown:                   MetaActivableCooldownSpec.Fixed.Zero,
                            allowActivationAdjustment:  true)),

                    // Negative depdendent offer in precursor test
                    new TestOfferInfo(
                        TestOfferId.FromString("NegativelyDependentOnPrecursor"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       new List<PlayerCondition>{ new TestOfferPrecursorCondition(TestOfferId.FromString("Precursor"), consumed: false, delay: MetaDuration.FromMinutes(1)) },
                            lifetime:                   new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(5)),
                            isTransient:                false,
                            schedule:                   null,
                            maxActivations:             1,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   null,
                            cooldown:                   MetaActivableCooldownSpec.Fixed.Zero,
                            allowActivationAdjustment:  true)),

                    // Offer with utc schedule
                    new TestOfferInfo(
                        TestOfferId.FromString("ScheduleBasicUtc"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       null,
                            lifetime:                   MetaActivableLifetimeSpec.ScheduleBased.Instance,
                            isTransient:                false,
                            schedule:                   testUtcSchedule,
                            maxActivations:             null,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   null,
                            cooldown:                   MetaActivableCooldownSpec.ScheduleBased.Instance,
                            allowActivationAdjustment:  true)),

                    // Offer with utc schedule and consumption limit
                    new TestOfferInfo(
                        TestOfferId.FromString("ScheduleWithConsumptionUtc"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       null,
                            lifetime:                   MetaActivableLifetimeSpec.ScheduleBased.Instance,
                            isTransient:                false,
                            schedule:                   testUtcSchedule,
                            maxActivations:             null,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   1,
                            cooldown:                   MetaActivableCooldownSpec.ScheduleBased.Instance,
                            allowActivationAdjustment:  true)),

                    // Offer with utc schedule and fixed (non-schedule-based) lifetime
                    new TestOfferInfo(
                        TestOfferId.FromString("ScheduleWithExpirationUtc"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       null,
                            lifetime:                   new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(4)),
                            isTransient:                false,
                            schedule:                   testUtcSchedule,
                            maxActivations:             null,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   null,
                            cooldown:                   MetaActivableCooldownSpec.ScheduleBased.Instance,
                            allowActivationAdjustment:  true)),

                    // Offer with local schedule
                    new TestOfferInfo(
                        TestOfferId.FromString("ScheduleBasicLocal"),
                        new MetaActivableParams(
                            isEnabled:                  true,
                            segments:                   null,
                            additionalConditions:       null,
                            lifetime:                   MetaActivableLifetimeSpec.ScheduleBased.Instance,
                            isTransient:                false,
                            schedule:                   testLocalSchedule,
                            maxActivations:             null,
                            maxTotalConsumes:           null,
                            maxConsumesPerActivation:   null,
                            cooldown:                   MetaActivableCooldownSpec.ScheduleBased.Instance,
                            allowActivationAdjustment:  true)),

                }.ToOrderedDictionary(info => info.OfferId);

                OnConfigEntriesPopulated(isBuildingConfigs: true);
            }

            object IGameConfigDataResolver.TryResolveReference(Type type, object configKey)
            {
                if (typeof(PlayerSegmentInfoBase).IsAssignableFrom(type))
                    return Segments.GetValueOrDefault((PlayerSegmentId)configKey);
                else if (typeof(TestOfferInfo).IsAssignableFrom(type))
                    return TestOffers.GetValueOrDefault((TestOfferId)configKey);
                else
                    return null;
            }
        }

        static MetaActivableParams ChangedParams(MetaActivableParams original, string targetMemberName, object targetNewValue)
        {
            MetaActivableParams clone = new MetaActivableParams();

            foreach (MemberInfo member in typeof(MetaActivableParams).EnumerateInstanceDataMembersInUnspecifiedOrder())
            {
                object memberNewValue = member.Name == targetMemberName
                                        ? targetNewValue
                                        : member.GetDataMemberGetValueOnDeclaringType()(original);

                member.GetDataMemberSetValueOnDeclaringType()(clone, memberNewValue);
            }

            return clone;
        }

        static void ChangeOfferParams(ActivableTestGameConfig config, TestOfferId offerId, string targetMemberName, object targetNewValue)
        {
            config.TestOffers[offerId] = new TestOfferInfo(offerId, ChangedParams(config.TestOffers[offerId].ActivableParams, targetMemberName, targetNewValue));
        }

        static void ChangeLifetime(ActivableTestGameConfig config, TestOfferId offerId, MetaActivableLifetimeSpec newLifetime)
        {
            ChangeOfferParams(config, offerId, nameof(MetaActivableParams.Lifetime), newLifetime);
        }

        static void ChangeCooldown(ActivableTestGameConfig config, TestOfferId offerId, MetaActivableCooldownSpec newCooldown)
        {
            ChangeOfferParams(config, offerId, nameof(MetaActivableParams.Cooldown), newCooldown);
        }

        #endregion

        #region Test offer models

        public Action<TestOfferModel> _testOfferOnStartedActivation;
        public Action<TestOfferModel> _testOfferFinalize;

        [MetaSerializable]
        public class TestOfferModel : MetaActivableState<TestOfferId, TestOfferInfo>
        {
            [MetaMember(1)] public sealed override TestOfferId ActivableId { get; protected set; }

            TestOfferModel(){ }
            public TestOfferModel(TestOfferInfo info)
                : base(info)
            {
            }

            protected override object TryGetActivableId() => null;

            [IgnoreDataMember] ActivableTests _testRuntimeState = null;

            public void SetupTestRuntimeState(ActivableTests testRuntimeState)
            {
                _testRuntimeState = testRuntimeState;
            }

            protected override void OnStartedActivation(IPlayerModelBase player)
            {
                _testRuntimeState._testOfferOnStartedActivation?.Invoke(this);
            }

            protected override void Finalize(IPlayerModelBase player)
            {
                _testRuntimeState._testOfferFinalize?.Invoke(this);
            }
        }

        [MetaActivableCategoryMetadata("TestOffer", "", "")]
        [MetaActivableKindMetadata("TestOffer", "", "", categoryId: "TestOffer")]
        public static class TestOfferActivableMetadata
        {
        }

        [MetaSerializable]
        [MetaAllowNoSerializedMembers]
        [MetaActivableSet("TestOffer")]
        public class PlayerTestOffersModel : MetaActivableSet<TestOfferId, TestOfferInfo, TestOfferModel>
        {
            protected override TestOfferModel CreateActivableState(TestOfferInfo info, IPlayerModelBase player)
            {
                TestOfferModel offer = new TestOfferModel(info);
                offer.SetupTestRuntimeState(_testRuntimeState);
                return offer;
            }

            ActivableTests _testRuntimeState = null;

            public void SetupTestRuntimeState(ActivableTests testRuntimeState)
            {
                foreach (TestOfferModel offer in _activableStates.Values)
                    offer.SetupTestRuntimeState(testRuntimeState);
                _testRuntimeState = testRuntimeState;
            }
        }

        #endregion

        #region Test player model

        [MetaSerializableDerived(9999)]
        [SupportedSchemaVersions(1, 1)]
        [MetaReservedMembers(100, 200)]
        public class ActivableTestPlayerModel : PlayerModelBase<
              ActivableTestPlayerModel
            , PlayerStatisticsCore
#if !METAPLAY_DISABLE_GUILDS
            , PlayerGuildStateCore
#endif
            >
        {
            public new ActivableTestGameConfig GameConfig => GetGameConfig<ActivableTestGameConfig>();

            public override IModelRuntimeData<IPlayerModelBase> GetRuntimeData()
            {
                throw new NotImplementedException();
            }

            protected override int GetTicksPerSecond() => 3;

            public override EntityId PlayerId { get; set; }
            public override string PlayerName { get; set; }
            public override int PlayerLevel { get; set; }

            [MetaMember(110)] public int NumGold;

            [MetaMember(120)] public PlayerTestOffersModel TestOffers { get; private set; } = new PlayerTestOffersModel();

            ActivableTests _testRuntimeState;
            public void SetupTestRuntimeState(ActivableTests testRuntimeState)
            {
                _testRuntimeState = testRuntimeState;
                TestOffers.SetupTestRuntimeState(testRuntimeState);
            }

            [MetaMember(130)] public List<TestOfferId> TestRelevantOffers = null; // Used per test case to specify the relevant offers. Only these will be updated in GameTick, as a perf improvement.
            [MetaMember(131)] public bool NoOpTryAdjustActivations = false;

            protected override void GameTick(IChecksumContext checksumCtx)
            {
                if (NoOpTryAdjustActivations)
                {
                    int numAdjusted = TryAdjustActivableActivations();
                    // Nothing should get adjusted unless there's been a config change.
                    // These tests do config-change-dependent adjustments explicitly
                    // in ClonePlayerModel, so at this point the adjustment should have
                    // already happened, and thus no further adjustment should get done.
                    Assert.Zero(numAdjusted);
                }

                IEnumerable<TestOfferInfo> testRelevantOfferInfos = TestRelevantOffers.Select(id => GameConfig.TestOffers[id]);

                TestOffers.TryFinalizeEach(testRelevantOfferInfos, player: this);
                TestOffers.TryStartActivationForEach(testRelevantOfferInfos, player: this);
            }

            protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name) { }
            protected override void GameOnRestoredFromPersistedState(MetaDuration elapsedTime){ }
            protected override void GameFastForwardTime(MetaDuration elapsedTime){ }

            protected override void GameOnSessionStarted(){ }
            protected override void GameOnInitialLogin(){ }
            protected override void GameImportAfterReset(ActivableTestPlayerModel source){ }
            public override void OnClaimedInAppProduct(InAppPurchaseEvent ev, InAppProductInfoBase productInfoBase, out ResolvedPurchaseContentBase resolvedContent){ resolvedContent = null; }
        }

        #endregion

        #region Helpers

        ActivableTestPlayerModel CreatePlayerModel(MetaTime startTime, ActivableTestGameConfig gameConfig = null)
        {
            ActivableTestPlayerModel player = PlayerModelUtil.CreateNewPlayerModel<ActivableTestPlayerModel>(
                startTime,
                gameConfig ?? new ActivableTestGameConfig(),
                playerId: EntityId.None,
                name: null);
            player.NoOpTryAdjustActivations = (_flags & ActivableTestsFlags.NoOpTryAdjustActivations) != 0;

            player.LogicVersion = 1;
            player.SetupTestRuntimeState(this);

            Assert.AreEqual(startTime, player.CurrentTime);

            return player;
        }

        ActivableTestPlayerModel CreatePlayerModel(ActivableTestGameConfig gameConfig = null)
        {
            MetaTime startTime = MetaTime.FromDateTime(new DateTime(2021, 5, 30, 19, 57, 17, DateTimeKind.Utc));
            return CreatePlayerModel(startTime, gameConfig);
        }

        ActivableTestPlayerModel ClonePlayerModel(ActivableTestPlayerModel original, ActivableTestGameConfig gameConfig)
        {
            ActivableTestPlayerModel cloned = MetaSerializationUtil.CloneModel(original, resolver: gameConfig);
            cloned.SetGameConfig(gameConfig);
            cloned.LogicVersion = 1;
            cloned.SetupTestRuntimeState(this);
            cloned.TryAdjustActivableActivations();

            return cloned;
        }

        static void CheckIsActive(bool shouldBeActive, ActivableTestPlayerModel player, TestOfferInfo offerInfo)
        {
            Assert.AreEqual(shouldBeActive, player.TestOffers.IsActive(offerInfo, player));
            Assert.AreEqual(shouldBeActive, player.TestOffers.GetActiveStates(player).Select(offer => offer.ActivableId).Contains(offerInfo.OfferId));

            player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus);
            Assert.AreEqual(shouldBeActive, visibleStatus is MetaActivableVisibleStatus.Active || visibleStatus is MetaActivableVisibleStatus.EndingSoon);

            if (shouldBeActive)
            {
                Assert.False(player.TestOffers.CanStartActivation(offerInfo, player));
                Assert.False(player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));
            }
        }

        static void CheckIsInCooldown(bool shouldBeInCooldown, ActivableTestPlayerModel player, TestOfferInfo offerInfo)
        {
            Assert.AreEqual(shouldBeInCooldown, player.TestOffers.TryGetState(offerInfo)?.IsInCooldown(player.CurrentTime) ?? false);

            if (shouldBeInCooldown)
            {
                Assert.False(player.TestOffers.IsActive(offerInfo, player));
                Assert.False(player.TestOffers.GetActiveStates(player).Select(offer => offer.ActivableId).Contains(offerInfo.OfferId));

                Assert.False(player.TestOffers.CanStartActivation(offerInfo, player));
            }
        }

        static void AdvanceTimeTo(ActivableTestPlayerModel player, MetaTime targetTime)
        {
            MetaDebug.Assert(targetTime >= player.CurrentTime, "Cannot advance time to the past");
            while (player.CurrentTime < targetTime)
                player.Tick(null);
        }

        static void AdvanceTimeBy(ActivableTestPlayerModel player, MetaDuration duration)
        {
            AdvanceTimeTo(player, player.CurrentTime + duration);
        }

        static void FastForwardTimeTo(ActivableTestPlayerModel player, MetaTime targetTime)
        {
            MetaDuration elapsed = targetTime - player.CurrentTime;
            player.ResetTime(targetTime);
            player.OnFastForwardTime(elapsed);
            MetaDebug.Assert(player.CurrentTime == targetTime, "FastForwardTimeTo didn't reach the target time exactly");
        }

        static MetaDuration GetRandomUtcOffset(RandomPCG rnd)
        {
            MetaDuration min;
            MetaDuration max;

            switch (rnd.NextInt(2))
            {
                case 0:     min = MetaDuration.FromHours(-1); max = MetaDuration.FromHours(1);      break;
                default:    min = MetaDuration.FromHours(-18); max = MetaDuration.FromHours(18);    break;
            }

            return min + MetaDuration.FromMilliseconds(rnd.NextInt((int)((max-min).Milliseconds+1)));
        }

        #endregion

        // Test basic segment-dependent activation. Positive case (conditions fulfilled).
        [Test]
        public void BasicActivationConditionsFulfilled()
        {
            ActivableTestPlayerModel player = CreatePlayerModel();
            player.NumGold = 100;

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("BasicLowGold")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            CheckIsActive(false, player, offerInfo);
            player.Tick(null);
            CheckIsActive(true, player, offerInfo);
        }

        // Test basic segment-dependent activation. Negative case (conditions not fulfilled).
        [Test]
        public void BasicActivationConditionsNotFulfilled()
        {
            ActivableTestPlayerModel player = CreatePlayerModel();
            player.NumGold = 101;

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("BasicLowGold")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            CheckIsActive(false, player, offerInfo);
            player.Tick(null);
            CheckIsActive(false, player, offerInfo);
        }

        // Test consumption limits, such that the total consumption limit
        // is reached at the same time as the per-activation limit for the
        // last activation.
        [Test]
        public void LimitTotalConsumptionEven()
        {
            ActivableTestPlayerModel player = CreatePlayerModel();

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("Limits")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            CheckIsActive(false, player, offerInfo);

            // Reach 8 TotalNumConsumed during 4 activations with 2 consumptions on each.
            for (int i = 0; i < 4; i++)
            {
                player.Tick(null);
                CheckIsActive(true, player, offerInfo);
                Assert.True(player.TestOffers.TryConsume(offerInfo, player));
                CheckIsActive(true, player, offerInfo);
                Assert.True(player.TestOffers.TryConsume(offerInfo, player));
                CheckIsActive(false, player, offerInfo);
            }

            // Check that the offer no longer becomes active and cannot be consumed.
            player.Tick(null);
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryConsume(offerInfo, player));

            // Check expected activation and consumption counts.
            TestOfferModel offer = player.TestOffers.TryGetState(offerInfo);
            Assert.AreEqual(4, offer.NumActivated);
            Assert.AreEqual(8, offer.TotalNumConsumed);
            Assert.AreEqual(2, offer.LatestActivation.Value.NumConsumed);
            Assert.True(offer.TotalLimitsAreReached());
        }

        // Test consumption limits, such that the total consumption limit
        // is reached at the same time as the first out of two per-activation
        // consumptions during the last activation.
        [Test]
        public void LimitTotalConsumptionOdd()
        {
            ActivableTestPlayerModel player = CreatePlayerModel();

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("Limits")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            CheckIsActive(false, player, offerInfo);

            // Consume once during first activation, then let expire.
            player.Tick(null);
            CheckIsActive(true, player, offerInfo);
            Assert.True(player.TestOffers.TryConsume(offerInfo, player));
            CheckIsActive(true, player, offerInfo);
            AdvanceTimeBy(player, MetaDuration.FromMinutes(5));

            // Consume 6 times during 3 activations with 2 consumptions on each, reaching 7 TotalNumConsumed.
            for (int i = 0; i < 3; i++)
            {
                player.Tick(null);
                CheckIsActive(true, player, offerInfo);
                Assert.True(player.TestOffers.TryConsume(offerInfo, player));
                CheckIsActive(true, player, offerInfo);
                Assert.True(player.TestOffers.TryConsume(offerInfo, player));
                CheckIsActive(false, player, offerInfo);
            }

            // Reach 8 TotalNumConsumed by consuming once during final activation.
            // Check the offer becomes inactive (because of total consumption limit)
            // even though consumed-per-activation limit isn't reached.
            player.Tick(null);
            CheckIsActive(true, player, offerInfo);
            Assert.True(player.TestOffers.TryConsume(offerInfo, player));
            CheckIsActive(false, player, offerInfo);

            // Check that the offer no longer becomes active and cannot be consumed.
            player.Tick(null);
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryConsume(offerInfo, player));

            // Check expected activation and consumption counts.
            TestOfferModel offer = player.TestOffers.TryGetState(offerInfo);
            Assert.AreEqual(5, offer.NumActivated);
            Assert.AreEqual(8, offer.TotalNumConsumed);
            Assert.AreEqual(1, offer.LatestActivation.Value.NumConsumed);
            Assert.True(offer.TotalLimitsAreReached());
        }

        // Test activation count limit.
        [Test]
        public void LimitActivations()
        {
            ActivableTestPlayerModel player = CreatePlayerModel();

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("Limits")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            CheckIsActive(false, player, offerInfo);

            // Activate the offer 6 times, letting it expire each time.
            for (int i = 0; i < 6; i++)
            {
                player.Tick(null);
                CheckIsActive(true, player, offerInfo);
                AdvanceTimeBy(player, MetaDuration.FromMinutes(5));
            }

            // Check that the offer no longer becomes active and cannot be consumed.
            player.Tick(null);
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryConsume(offerInfo, player));

            // Check expected activation and consumption counts.
            TestOfferModel offer = player.TestOffers.TryGetState(offerInfo);
            Assert.AreEqual(6, offer.NumActivated);
            Assert.AreEqual(0, offer.TotalNumConsumed);
            Assert.AreEqual(0, offer.LatestActivation.Value.NumConsumed);
            Assert.True(offer.TotalLimitsAreReached());
        }

        // Test fixed-time expiration and cooldown.
        // Also doubles as a test for the OnStartedActivation and Finalize
        // hooks of MetaActivableState, when enabled by the parameter.
        [TestCase(false)]
        [TestCase(true)]
        public void LifetimeAndCooldown(bool testActivationAndFinalizationHooks)
        {
            ActivableTestPlayerModel player = CreatePlayerModel();

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("LifetimeAndCooldown")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            CheckIsActive(false, player, offerInfo);

            string hookInfo = "";

            _testOfferOnStartedActivation = offer =>
            {
                if (offer == player.TestOffers.TryGetState(offerInfo))
                    hookInfo += "A";
            };

            _testOfferFinalize = offer =>
            {
                if (offer == player.TestOffers.TryGetState(offerInfo))
                    hookInfo += "F";
            };

            void CheckHookInfo(string expected)
            {
                if (testActivationAndFinalizationHooks)
                    Assert.AreEqual(expected, hookInfo);
            }

            MetaTime startTime = player.CurrentTime;

            // Activate the offer, and check it remains active for its lifetime (5 minutes).
            CheckHookInfo("");
            player.Tick(null);
            CheckHookInfo("A");
            CheckIsActive(true, player, offerInfo);
            for (int i = 1; i <= 4; i++)
            {
                AdvanceTimeTo(player, startTime + MetaDuration.FromMinutes(i));
                CheckIsActive(true, player, offerInfo);
            }
            AdvanceTimeTo(player, startTime + MetaDuration.FromMinutes(5) - MetaDuration.FromSeconds(1));
            CheckIsActive(true, player, offerInfo);
            // Advance to the end of the lifetime, and check it's inactive and in cooldown.
            CheckHookInfo("A");
            AdvanceTimeTo(player, startTime + MetaDuration.FromMinutes(5));
            CheckHookInfo("A");
            CheckIsActive(false, player, offerInfo);
            Assert.True(player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));
            player.Tick(null);
            CheckHookInfo("AF");
            CheckIsActive(false, player, offerInfo);
            Assert.True(player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));
            player.Tick(null);
            CheckHookInfo("AF"); // did not re-finalize

            // Check it remains in cooldown for its cooldown duration (3 minutes).
            for (int i = 1; i <= 3; i++)
            {
                AdvanceTimeTo(player, startTime + MetaDuration.FromMinutes(5 + i));
                CheckIsActive(false, player, offerInfo);
                if (i < 3) // \note Cooldown ends after exactly 3 minutes.
                    Assert.True(player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));
            }

            // Tick once, and check that activates the offer.
            player.Tick(null);
            CheckHookInfo("AFA");
            CheckIsActive(true, player, offerInfo);
            // Consume the offer, and check it is in cooldown again.
            Assert.True(player.TestOffers.TryConsume(offerInfo, player));
            CheckHookInfo("AFA");
            CheckIsActive(false, player, offerInfo);
            Assert.True(player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));
            CheckHookInfo("AFA");
            player.TestOffers.TryFinalizeEach(player.GameConfig.TestOffers.Values, player);
            CheckHookInfo("AFAF");
            player.TestOffers.TryFinalizeEach(player.GameConfig.TestOffers.Values, player);
            CheckHookInfo("AFAF"); // did not re-finalize

            // Check it remains in cooldown for its cooldown duration (3 minutes).
            MetaTime secondCooldownStartTime = player.CurrentTime;
            for (int i = 1; i <= 3; i++)
            {
                AdvanceTimeTo(player, secondCooldownStartTime + MetaDuration.FromMinutes(i));
                CheckIsActive(false, player, offerInfo);
                if (i < 3)
                    Assert.True(player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));
            }

            // Test one more activation.
            player.Tick(null);
            CheckHookInfo("AFAFA");
            CheckIsActive(true, player, offerInfo);
            AdvanceTimeBy(player, MetaDuration.FromMinutes(5));
            CheckHookInfo("AFAFAF");
            CheckIsActive(false, player, offerInfo);
            Assert.True(player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));

            // That was the final activation (activation limit is 3).
            // Advance past the cooldown, and check it no longer activates, nor ends up in cooldown.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            CheckHookInfo("AFAFAF");
            CheckIsActive(false, player, offerInfo);
            Assert.True(!player.TestOffers.TryGetState(offerInfo).IsInCooldown(player.CurrentTime));

            // Check expected activation and consumption counts.
            TestOfferModel offer = player.TestOffers.TryGetState(offerInfo);
            Assert.AreEqual(3, offer.NumActivated);
            Assert.AreEqual(1, offer.TotalNumConsumed);
            Assert.AreEqual(0, offer.LatestActivation.Value.NumConsumed);
            Assert.True(offer.TotalLimitsAreReached());
        }

        // Test positive precursor dependency.
        [Test]
        public void PrecursorPositiveDependency()
        {
            ActivableTestPlayerModel player = CreatePlayerModel();

            TestOfferInfo precursorInfo = player.GameConfig.TestOffers[TestOfferId.FromString("Precursor")];
            TestOfferInfo positiveInfo  = player.GameConfig.TestOffers[TestOfferId.FromString("PositivelyDependentOnPrecursor")];
            TestOfferInfo negativeInfo  = player.GameConfig.TestOffers[TestOfferId.FromString("NegativelyDependentOnPrecursor")];
            player.TestRelevantOffers = new List<TestOfferId>{ precursorInfo.OfferId, positiveInfo.OfferId, negativeInfo.OfferId };

            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Activate the dependency (precursor)
            player.Tick(null);
            CheckIsActive(true, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);
            AdvanceTimeBy(player, MetaDuration.FromMinutes(1));
            CheckIsActive(true, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Consume the dependency.
            // Check that neither dependent activates too soon.
            Assert.True(player.TestOffers.TryConsume(precursorInfo, player));
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);
            AdvanceTimeBy(player, MetaDuration.FromMinutes(1));
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Tick once past the 1 minute, and check the positive dependent activates.
            player.Tick(null);
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(true, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Expire the positive dependent. None of the relevant offers should be active.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(5));
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Check activation counts after advancing far into future.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(30));
            Assert.AreEqual(1, player.TestOffers.TryGetState(precursorInfo).NumActivated);
            Assert.AreEqual(1, player.TestOffers.TryGetState(positiveInfo).NumActivated);
            Assert.Null(player.TestOffers.TryGetState(negativeInfo));
        }

        // Test negative precursor dependency.
        [Test]
        public void PrecursorNegativeDependency()
        {
            ActivableTestPlayerModel player = CreatePlayerModel();

            TestOfferInfo precursorInfo = player.GameConfig.TestOffers[TestOfferId.FromString("Precursor")];
            TestOfferInfo positiveInfo  = player.GameConfig.TestOffers[TestOfferId.FromString("PositivelyDependentOnPrecursor")];
            TestOfferInfo negativeInfo  = player.GameConfig.TestOffers[TestOfferId.FromString("NegativelyDependentOnPrecursor")];
            player.TestRelevantOffers = new List<TestOfferId>{ precursorInfo.OfferId, positiveInfo.OfferId, negativeInfo.OfferId };

            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Activate the dependency (precursor)
            player.Tick(null);
            CheckIsActive(true, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);
            AdvanceTimeBy(player, MetaDuration.FromMinutes(1));
            CheckIsActive(true, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Expire the dependency.
            // Check that neither dependent activates too soon.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);
            AdvanceTimeBy(player, MetaDuration.FromSeconds(59));
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Advance a bit, and check the negative dependent activates.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(1));
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(true, player, negativeInfo);

            // Expire the negative dependent. None of the relevant offers should be active.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(5));
            CheckIsActive(false, player, precursorInfo);
            CheckIsActive(false, player, positiveInfo);
            CheckIsActive(false, player, negativeInfo);

            // Check activation counts after advancing far into future.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(30));
            Assert.AreEqual(1, player.TestOffers.TryGetState(precursorInfo).NumActivated);
            Assert.Null(player.TestOffers.TryGetState(positiveInfo));
            Assert.AreEqual(1, player.TestOffers.TryGetState(negativeInfo).NumActivated);
        }

        // Test the various phases of a basic offer with a utc schedule.
        // Optionally, player's local utc offset is randomized at various points.
        // Local utc offset should have no effect since the schedule is in utc.
        [TestCase(false)]
        [TestCase(true)]
        public void ScheduleBasicUtc(bool enableLocalUtcOffsetRandomization)
        {
            ActivableTestPlayerModel player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("ScheduleBasicUtc")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            // Helper for randomizing player's local utc offset.

            RandomPCG utcOffsetRnd = RandomPCG.CreateFromSeed(1);
            void TweakLocalUtcOffset()
            {
                if (enableLocalUtcOffsetRandomization)
                    player.TimeZoneInfo = new PlayerTimeZoneInfo(GetRandomUtcOffset(utcOffsetRnd));
            }

            // Test the 3 hourly repeats of the schedule.
            for (int hour = 10; hour < 13; hour++)
            {
                // Check the offer isn't visible until the preview.
                TweakLocalUtcOffset();
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 28, 59)));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Advance into the preview, and check it is as expected.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 29, 0)));
                player.Tick(null);
                CheckIsActive(false, player, offerInfo);
                {
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.InPreview>(visibleStatus);
                    MetaActivableVisibleStatus.InPreview preview = (MetaActivableVisibleStatus.InPreview)visibleStatus;
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        preview.ScheduleEnabledRange);
                }

                // Test various points in the active phase.
                for (int sec = 0; sec < 8*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(true, player, offerInfo);
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.Active>(visibleStatus);
                    MetaActivableVisibleStatus.Active active = (MetaActivableVisibleStatus.Active)visibleStatus;
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), active.ActivationStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 38, 0)), active.EndingSoonStartsAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)), active.ActivationEndsAt);
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        active.ScheduleEnabledRange);
                }

                // Test various points in the "ending soon" phase.
                for (int sec = 0; sec < 2*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 38, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(true, player, offerInfo);
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.EndingSoon>(visibleStatus);
                    MetaActivableVisibleStatus.EndingSoon endingSoon = (MetaActivableVisibleStatus.EndingSoon)visibleStatus;
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), endingSoon.ActivationStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 38, 0)), endingSoon.EndingSoonStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)), endingSoon.ActivationEndsAt);
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        endingSoon.ScheduleEnabledRange);
                }

                // Test various points in the review phase.
                for (int sec = 0; sec < 3*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(false, player, offerInfo);
                    {
                        Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                        Assert.IsInstanceOf<MetaActivableVisibleStatus.InReview>(visibleStatus);
                        MetaActivableVisibleStatus.InReview review = (MetaActivableVisibleStatus.InReview)visibleStatus;
                        Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), review.ActivationStartedAt);
                        Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)), review.ActivationEndedAt);
                        Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 43, 0)), review.VisibilityEndsAt);
                        Assert.AreEqual(
                            new MetaTimeRange(
                                MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                                MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                            review.ScheduleEnabledRange);
                    }
                }

                // Test past the review phase. The offer should no longer be visible.
                TweakLocalUtcOffset();
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 43, 0, 0)));
                player.Tick(null);
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
            }

            // Test a point in time where the offer would be active if the schedule had more repeats.
            // Check the offer isn't visible.
            TweakLocalUtcOffset();
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 13, 31, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

            // Sanity check activation count
            Assert.AreEqual(3, player.TestOffers.TryGetState(offerInfo).NumActivated);
        }

        // Test the various phases of an offer with a utc schedule and a consumption limit.
        // Optionally, player's local utc offset is randomized at various points.
        // Local utc offset should have no effect since the schedule is in utc.
        [TestCase(false)]
        [TestCase(true)]
        public void ScheduleWithConsumptionUtc(bool enableLocalUtcOffsetRandomization)
        {
            ActivableTestPlayerModel player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("ScheduleWithConsumptionUtc")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            // Helper for randomizing player's local utc offset.

            RandomPCG utcOffsetRnd = RandomPCG.CreateFromSeed(1);
            void TweakLocalUtcOffset()
            {
                if (enableLocalUtcOffsetRandomization)
                    player.TimeZoneInfo = new PlayerTimeZoneInfo(GetRandomUtcOffset(utcOffsetRnd));
            }

            // Test the 3 hourly repeats of the schedule.
            for (int hour = 10; hour < 13; hour++)
            {
                // Check the offer isn't visible until the preview.
                TweakLocalUtcOffset();
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 28, 59)));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Advance into the preview, and check it is as expected.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 29, 0)));
                player.Tick(null);
                CheckIsActive(false, player, offerInfo);
                {
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.InPreview>(visibleStatus);
                    MetaActivableVisibleStatus.InPreview preview = (MetaActivableVisibleStatus.InPreview)visibleStatus;
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        preview.ScheduleEnabledRange);

                    Assert.False(player.TestOffers.TryConsume(offerInfo, player)); // Check the offer can't be consumed yet.
                }

                // Test various points in the active phase, but not all the way to the end. We'll want to consume the offer while it is active.
                for (int sec = 0; sec < 4*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(true, player, offerInfo);
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.Active>(visibleStatus);
                    MetaActivableVisibleStatus.Active active = (MetaActivableVisibleStatus.Active)visibleStatus;
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), active.ActivationStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 38, 0)), active.EndingSoonStartsAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)), active.ActivationEndsAt);
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        active.ScheduleEnabledRange);
                }

                // Consume the offer, reaching the per-activation limit.
                // Check the offer immediately becomes non-visible; review phase doesn't take effect if the offer was deactivated due to consumption.
                Assert.True(player.TestOffers.TryConsume(offerInfo, player));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Check the offer remains non-visible for a good while.
                for (int sec = 0; sec < 10*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 34, 0)) + MetaDuration.FromSeconds(sec));
                    CheckIsActive(false, player, offerInfo);
                    Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                }
            }

            // Test a point in time where the offer would be active if the schedule had more repeats.
            // Check the offer isn't visible.
            TweakLocalUtcOffset();
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 13, 31, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

            // Sanity check activation count
            Assert.AreEqual(3, player.TestOffers.TryGetState(offerInfo).NumActivated);
        }

        // Test the various phases of an offer with a utc schedule and a fixed lifetime.
        // Optionally, player's local utc offset is randomized at various points.
        // Local utc offset should have no effect since the schedule is in utc.
        [TestCase(false)]
        [TestCase(true)]
        public void ScheduleWithExpirationUtc(bool enableLocalUtcOffsetRandomization)
        {
            ActivableTestPlayerModel player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("ScheduleWithExpirationUtc")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            // Helper for randomizing player's local utc offset.

            RandomPCG utcOffsetRnd = RandomPCG.CreateFromSeed(1);
            void TweakLocalUtcOffset()
            {
                if (enableLocalUtcOffsetRandomization)
                    player.TimeZoneInfo = new PlayerTimeZoneInfo(GetRandomUtcOffset(utcOffsetRnd));
            }

            // Test the 3 hourly repeats of the schedule.
            for (int hour = 10; hour < 13; hour++)
            {
                // Check the offer isn't visible until the preview.
                TweakLocalUtcOffset();
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 28, 59)));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Advance into the preview, and check it is as expected.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 29, 0)));
                player.Tick(null);
                CheckIsActive(false, player, offerInfo);
                {
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.InPreview>(visibleStatus);
                    MetaActivableVisibleStatus.InPreview preview = (MetaActivableVisibleStatus.InPreview)visibleStatus;
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        preview.ScheduleEnabledRange);
                }

                // Test various points in the active phase.
                for (int sec = 0; sec < 4*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(true, player, offerInfo);
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.Active>(visibleStatus);
                    MetaActivableVisibleStatus.Active active = (MetaActivableVisibleStatus.Active)visibleStatus;
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), active.ActivationStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 34, 0)), active.EndingSoonStartsAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 34, 0)), active.ActivationEndsAt);
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        active.ScheduleEnabledRange);
                }

                // Advance to expire the offer; it expires before reaching the end of the schedule occasion.
                // Check the offer immediately becomes non-visible; review phase doesn't take effect if the offer was deactivated due to expiration.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 34, 0)));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Check the offer remains non-visible for a good while.
                for (int sec = 0; sec < 10*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 34, 0)) + MetaDuration.FromSeconds(sec));
                    CheckIsActive(false, player, offerInfo);
                    Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                }
            }

            // Test a point in time where the offer would be active if the schedule had more repeats.
            // Check the offer isn't visible.
            TweakLocalUtcOffset();
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 13, 31, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

            // Sanity check activation count
            Assert.AreEqual(3, player.TestOffers.TryGetState(offerInfo).NumActivated);
        }

        // Test the various phases of an offer with a utc schedule and a fixed lifetime,
        // such that the fixed lifetime extends beyond the end of the schedule occasion.
        // Optionally, player's local utc offset is randomized at various points.
        // Local utc offset should have no effect since the schedule is in utc.
        [TestCase(false)]
        [TestCase(true)]
        public void ScheduleWithOverflowingExpirationUtc(bool enableLocalUtcOffsetRandomization)
        {
            ActivableTestPlayerModel player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("ScheduleWithExpirationUtc")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            // Helpers for randomizing player's local utc offset.

            RandomPCG utcOffsetRnd = RandomPCG.CreateFromSeed(1);
            void TweakLocalUtcOffset()
            {
                if (enableLocalUtcOffsetRandomization)
                    player.TimeZoneInfo = new PlayerTimeZoneInfo(GetRandomUtcOffset(utcOffsetRnd));
            }

            // Test the 3 hourly repeats of the schedule.
            for (int hour = 10; hour < 13; hour++)
            {
                // Check the offer isn't visible until the preview.
                TweakLocalUtcOffset();
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 28, 59)));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Advance into the preview, and check it is as expected.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 29, 0)));
                player.Tick(null);
                CheckIsActive(false, player, offerInfo);
                {
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.InPreview>(visibleStatus);
                    MetaActivableVisibleStatus.InPreview preview = (MetaActivableVisibleStatus.InPreview)visibleStatus;
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        preview.ScheduleEnabledRange);
                }

                // Fast-forward time far enough into the active phase, so that the fixed lifetime
                // will extend beyond the end of the schedule occasion.
                FastForwardTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 37, 0)));

                // Test various points in the active phase.
                for (int sec = 0; sec < 4*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 37, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(true, player, offerInfo);
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.Active>(visibleStatus);
                    MetaActivableVisibleStatus.Active active = (MetaActivableVisibleStatus.Active)visibleStatus;
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 37, 0)), active.ActivationStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 41, 0)), active.EndingSoonStartsAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 41, 0)), active.ActivationEndsAt);
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        active.ScheduleEnabledRange);
                }

                // Advance to expire the offer; it expires before reaching the end of the schedule occasion.
                // Check the offer immediately becomes non-visible; review phase doesn't take effect if the offer was deactivated due to expiration.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 41, 0)));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Check the offer remains non-visible for a good while.
                for (int sec = 0; sec < 10*60; sec += 15)
                {
                    TweakLocalUtcOffset();
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 41, 0)) + MetaDuration.FromSeconds(sec));
                    CheckIsActive(false, player, offerInfo);
                    Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                }
            }

            // Test a point in time where the offer would be active if the schedule had more repeats.
            // Check the offer isn't visible.
            TweakLocalUtcOffset();
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 13, 31, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

            // Sanity check activation count
            Assert.AreEqual(3, player.TestOffers.TryGetState(offerInfo).NumActivated);
        }

        // Test the various phases of a basic offer with a local-time schedule.
        // Optionally, player's local utc offset is randomized at various points.
        // Changing local utc offset should not affect the phases of an existing activation.
        [TestCase(false)]
        [TestCase(true)]
        public void ScheduleBasicLocal(bool enableLocalUtcOffsetChanges)
        {
            ActivableTestPlayerModel player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("ScheduleBasicLocal")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            RandomPCG utcOffsetRnd = RandomPCG.CreateFromSeed(1);

            // Test the 3 hourly repeats of the schedule.
            foreach ((int baseHour, int testIndex) in new int[]{ 5, 6, 7 }.ZipWithIndex())
            {
                // When local utc offset changes are enabled, vary the offset,
                // and vary the expected starting utc hour accordingly.
                int hour;
                if (enableLocalUtcOffsetChanges)
                {
                    hour = baseHour + testIndex;
                    player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5 - testIndex));
                }
                else
                    hour = baseHour;

                MetaDuration baseUtcOffset = player.TimeZoneInfo.CurrentUtcOffset;

                // Check the offer isn't visible until the preview.
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 28, 59)));
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

                // Advance into the preview, and check it is as expected.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 29, 0)));
                player.Tick(null);
                CheckIsActive(false, player, offerInfo);
                {
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.InPreview>(visibleStatus);
                    MetaActivableVisibleStatus.InPreview preview = (MetaActivableVisibleStatus.InPreview)visibleStatus;
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        preview.ScheduleEnabledRange);
                }
                // Check that changing the local utc offset changes the preview phase.
                if (enableLocalUtcOffsetChanges)
                {
                    player.TimeZoneInfo = new PlayerTimeZoneInfo(baseUtcOffset + MetaDuration.FromSeconds(17));
                    player.Tick(null);
                    CheckIsActive(false, player, offerInfo);

                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.InPreview>(visibleStatus);
                    MetaActivableVisibleStatus.InPreview preview = (MetaActivableVisibleStatus.InPreview)visibleStatus;
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 29, 60-17)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 39, 60-17))),
                        preview.ScheduleEnabledRange);

                    player.TimeZoneInfo = new PlayerTimeZoneInfo(baseUtcOffset);
                }

                // Test various points in the active phase.
                for (int sec = 0; sec < 8*60; sec += 15)
                {
                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);

                    // If enabled, randomize player's local utc offset. It should not affect the active phase.
                    if (enableLocalUtcOffsetChanges)
                        player.TimeZoneInfo = new PlayerTimeZoneInfo(GetRandomUtcOffset(utcOffsetRnd));

                    CheckIsActive(true, player, offerInfo);
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.Active>(visibleStatus);
                    MetaActivableVisibleStatus.Active active = (MetaActivableVisibleStatus.Active)visibleStatus;
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), active.ActivationStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 38, 0)), active.EndingSoonStartsAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)), active.ActivationEndsAt);
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        active.ScheduleEnabledRange);
                }

                // Test various points in the "ending soon" phase.
                for (int sec = 0; sec < 2*60; sec += 15)
                {
                    // If enabled, randomize player's local utc offset. It should not affect the "ending soon" phase.
                    if (enableLocalUtcOffsetChanges)
                        player.TimeZoneInfo = new PlayerTimeZoneInfo(GetRandomUtcOffset(utcOffsetRnd));

                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 38, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(true, player, offerInfo);
                    Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                    Assert.IsInstanceOf<MetaActivableVisibleStatus.EndingSoon>(visibleStatus);
                    MetaActivableVisibleStatus.EndingSoon endingSoon = (MetaActivableVisibleStatus.EndingSoon)visibleStatus;
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), endingSoon.ActivationStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 38, 0)), endingSoon.EndingSoonStartedAt);
                    Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)), endingSoon.ActivationEndsAt);
                    Assert.AreEqual(
                        new MetaTimeRange(
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                            MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                        endingSoon.ScheduleEnabledRange);
                }

                // Test various points in the review phase.
                for (int sec = 0; sec < 3*60; sec += 15)
                {
                    // If enabled, randomize player's local utc offset. It should not affect the review phase.
                    if (enableLocalUtcOffsetChanges)
                        player.TimeZoneInfo = new PlayerTimeZoneInfo(GetRandomUtcOffset(utcOffsetRnd));

                    AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)) + MetaDuration.FromSeconds(sec));
                    player.Tick(null);
                    CheckIsActive(false, player, offerInfo);
                    {
                        Assert.True(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out MetaActivableVisibleStatus visibleStatus));
                        Assert.IsInstanceOf<MetaActivableVisibleStatus.InReview>(visibleStatus);
                        MetaActivableVisibleStatus.InReview review = (MetaActivableVisibleStatus.InReview)visibleStatus;
                        Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)), review.ActivationStartedAt);
                        Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0)), review.ActivationEndedAt);
                        Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 43, 0)), review.VisibilityEndsAt);
                        Assert.AreEqual(
                            new MetaTimeRange(
                                MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 30, 0)),
                                MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 40, 0))),
                            review.ScheduleEnabledRange);
                    }
                }

                player.TimeZoneInfo = new PlayerTimeZoneInfo(baseUtcOffset);

                // Test past the review phase. The offer should no longer be visible.
                AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, hour, 43, 0, 0)));
                player.Tick(null);
                CheckIsActive(false, player, offerInfo);
                Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
            }

            // Test a point in time where the offer would be active if the schedule had more repeats.
            // Check the offer isn't visible.

            // When local utc offset changes are enabled, vary the offset,
            // and vary the expected would-be starting utc hour accordingly.
            int finalHour;
            if (enableLocalUtcOffsetChanges)
            {
                finalHour = 8 + 3;
                player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5 - 3));
            }
            else
                finalHour = 8;

            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, finalHour, 31, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

            // Sanity check activation count
            Assert.AreEqual(3, player.TestOffers.TryGetState(offerInfo).NumActivated);
        }

        // Test schedule-offset-blocking with a basic offer with a local schedule.
        [Test]
        public void ScheduleLocalOffsetBlocking()
        {
            ActivableTestPlayerModel player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));

            TestOfferInfo offerInfo = player.GameConfig.TestOffers[TestOfferId.FromString("ScheduleBasicLocal")];
            player.TestRelevantOffers = new List<TestOfferId>{ offerInfo.OfferId };

            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));

            // Advance to first activate the offer.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 31, 0)));
            CheckIsActive(true, player, offerInfo);

            // Advance to end the first activation of the offer.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 50, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
            Assert.AreEqual(1, player.TestOffers.TryGetState(offerInfo).NumActivated);

            // Decrease the player's local utc offset by 1 hour.
            // Sanity check that it doesn't affect the visibility.
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(4));
            player.Tick(null);
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
            Assert.AreEqual(1, player.TestOffers.TryGetState(offerInfo).NumActivated);

            // Advance into the first occasion of the schedule with the now-modified local utc offset.
            // Check that the offer nevertheless isn't visible, because it is being schedule-offset-blocked.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 6, 31, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.False(player.TestOffers.TryGetVisibleStatus(offerInfo, player, out _));
            Assert.True(offerInfo.ActivableParams.Schedule.IsEnabledAt(player.GetCurrentLocalTime()));
            Assert.True(player.TestOffers.TryGetState(offerInfo).IsScheduleOffsetBlocked(player.GetCurrentLocalTime()));
            Assert.AreEqual(1, player.TestOffers.TryGetState(offerInfo).NumActivated);

            // Advance into the second activation of the offer.
            // Since the player's local utc offset was decreased by 1 hour, advancing
            // 2 hours past the first activation should activate it again.
            // Check that's the case.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 7, 31, 0)));
            CheckIsActive(true, player, offerInfo);
            Assert.AreEqual(2, player.TestOffers.TryGetState(offerInfo).NumActivated);

            // Advance well into the future, and check the offer is no longer active,
            // and has been activated a total of 3 times (the repeat limit).
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 31, 0)));
            CheckIsActive(false, player, offerInfo);
            Assert.AreEqual(3, player.TestOffers.TryGetState(offerInfo).NumActivated);
        }

        // Test that removing an offer from the config doesn't break player deserialization,
        // and re-introducing the removed offer to the config brings the old offer state
        // back in the player state.
        [Test]
        public void TestConfigRemoval()
        {
            TestOfferId offerXId = TestOfferId.FromString("Limits");
            TestOfferId offerYId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player with two offers active

            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerXId, offerYId };
            Assert.AreEqual(new List<TestOfferId>{}, player.TestOffers.GetActiveStates(player));
            // Advance time to activate both offers.
            // Consume one of the offers; later we'll check that this state has been retained over the config removal.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            player.TestOffers.TryConsume(player.GameConfig.TestOffers[offerXId], player);
            // Both offers must be active now.
            // The consumed offer must reflect that it has been consumed.
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerXId]);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerYId]);
            Assert.AreEqual(new List<TestOfferId>{ offerXId, offerYId }, player.TestOffers.GetActiveStates(player).Select(offer => offer.ActivableId));
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerXId]).TotalNumConsumed);

            // Clone the player, using a config with one of the two offers missing

            ActivableTestGameConfig configWithRemovedOffer = new ActivableTestGameConfig();
            configWithRemovedOffer.TestOffers.Remove(offerXId);
            player = ClonePlayerModel(player, configWithRemovedOffer);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerYId]);
            Assert.AreEqual(new List<TestOfferId>{ offerYId }, player.TestOffers.GetActiveStates(player).Select(offer => offer.ActivableId));
            Assert.Null(player.TestOffers.TryGetState(new ActivableTestGameConfig().TestOffers[offerXId])); // \note A bit of a cheat to give a non-existent offer info

            // Re-clone the cloned player, using a config that again has the offer that was missing in the first cloning.

            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerXId]);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerYId]);
            Assert.AreEqual(new List<TestOfferId>{ offerYId, offerXId }, player.TestOffers.GetActiveStates(player).Select(offer => offer.ActivableId));
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerXId]).TotalNumConsumed);
        }

        // Decrease configured lifetime during activation, but not enough to end the activation.
        [Test]
        public void TestConfigChangeDecreaseLifetimeDuringActivation()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, but don't let it end yet.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(3));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Decrease the configured lifetime, but by
            // a small enough amount that it's still active.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeLifetime(configWithChangedOffer, offerId, new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(4)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance past the activation, according to the new shorter lifetime. Cooldown should start.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(90));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance past the cooldown. Offer should re-activate
            AdvanceTimeBy(player, MetaDuration.FromMinutes(3));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured lifetime during activation, causing activation to end and cooldown to take effect.
        [Test]
        public void TestConfigChangeDecreaseLifetimeDuringActivationIntoCooldown()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, but don't let it end yet.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Decrease the configured lifetime; the offer should then
            // be in cooldown despite time not having been advanced.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeLifetime(configWithChangedOffer, offerId, new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(2)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance past the cooldown. Offer should re-activate.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(2));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured lifetime during activation, causing activation to end and cooldown to be skipped.
        [Test]
        public void TestConfigChangeDecreaseLifetimeDuringActivationPastCooldown()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, but don't let it end yet.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Decrease the configured lifetime; the offer should then
            // be inactive (and not in cooldown) despite time not having been advanced.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeLifetime(configWithChangedOffer, offerId, new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromSeconds(30)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            // Offer is inactive, and not in cooldown either.
            // Since the player hasn't been ticked since the config change, a new activation hasn't been started yet.
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            CheckIsInCooldown(false, player, player.GameConfig.TestOffers[offerId]);
            // Sanity: Activate by ticking once.
            Assert.True(player.TestOffers.CanStartActivation(player.GameConfig.TestOffers[offerId], player));
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured lifetime during cooldown. This should not affect the ongoing cooldown,
        // because we're already past the lifetime of the latest activation. In particular, decreasing
        // the lifetime during cooldown won't bring us back to the activation.
        [Test]
        public void TestConfigChangeDecreaseLifetimeDuringCooldown()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, and past the activation into cooldown.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(6));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is in cooldown. Decrease the configured lifetime; offer should still be in cooldown,
            // because changing the lifetime during cooldown doesn't affect current cooldown.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeLifetime(configWithChangedOffer, offerId, new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(2)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance to near the end of (but still within) the cooldown.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(60 + 59));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance past the cooldown. New activation should start.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(2));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured cooldown during activation. The cooldown after the activation
        // should reflect the new configuration.
        [Test]
        public void TestConfigChangeDecreaseCooldownDuringLifetime()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, but don't let it end yet.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Decrease the configured cooldown; offer should still be active.
            // Then, check that the cooldown after the activation has the new configured duration.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeCooldown(configWithChangedOffer, offerId, new MetaActivableCooldownSpec.Fixed(MetaDuration.FromMinutes(1)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance into the cooldown.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(90));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance enough to end the new configured cooldown, but not enough to end the old configured cooldown.
            // The cooldown ends (and new activation starts) because the offer adheres to the new configured cooldown.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(1));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured cooldown during cooldown, but not enough to end the cooldown.
        [Test]
        public void TestConfigChangeDecreaseCooldownDuringCooldown()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, and into the cooldown.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(6));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is in cooldown. Decrease the configured cooldown by a small enough amount
            // that the cooldown doesn't end.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeCooldown(configWithChangedOffer, offerId, new MetaActivableCooldownSpec.Fixed(MetaDuration.FromMinutes(2)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance past the cooldown, according to the new shorter cooldown.
            // New activation should start.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(91));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured cooldown during cooldown, causing the cooldown to end.
        [Test]
        public void TestConfigChangeDecreaseCooldownDuringCooldownPastCooldown()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, and into the cooldown.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(7));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is in cooldown. Decrease the configured cooldown enough to end the cooldown.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeCooldown(configWithChangedOffer, offerId, new MetaActivableCooldownSpec.Fixed(MetaDuration.FromMinutes(1)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            // Offer is inactive, and not in cooldown either.
            // Since the player hasn't been ticked since the config change, a new activation hasn't been started yet.
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            CheckIsInCooldown(false, player, player.GameConfig.TestOffers[offerId]);
            // Sanity: Activate by ticking once.
            Assert.True(player.TestOffers.CanStartActivation(player.GameConfig.TestOffers[offerId], player));
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Increase configured lifetime during activation, and check that the change is reflected
        // in the current activation.
        [Test]
        public void TestConfigChangeIncreaseLifetimeDuringActivation()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Increase the lifetime. The offer should stay active.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeLifetime(configWithChangedOffer, offerId, new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(10)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance enough that it would end the activation if the old configured lifetime was still in effect.
            // The activation doesn't end, because the new configured lifetime is in effect.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(2));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance enough to end the activation. Cooldown starts.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(5));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance enough to end the cooldown (configuration not changed). New activation starts.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(2*60 + 1));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Increase configured lifetime during cooldown. This should not affect the ongoing cooldown,
        // because we're already past the lifetime of the latest activation. In particular, increasing
        // the lifetime during cooldown won't end the cooldown.
        [Test]
        public void TestConfigChangeIncreaseLifetimeDuringCooldown()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, and into the cooldown.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(6));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is in cooldown. Increase the configured lifetime; the offer stays in cooldown.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeLifetime(configWithChangedOffer, offerId, new MetaActivableLifetimeSpec.Fixed(MetaDuration.FromMinutes(10)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance past the cooldown. New activation starts.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(3));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            // Advance enough that the old configured lifetime would be exceeded, but the new one won't.
            // Offer is using the new lifetime, so activation won't end.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(8));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Increase configured cooldown during activation. The cooldown after the activation
        // should reflect the new configuration.
        [Test]
        public void TestConfigChangeIncreaseCooldownDuringActivation()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(4));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Increase the cooldown. Offer stays active.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeCooldown(configWithChangedOffer, offerId, new MetaActivableCooldownSpec.Fixed(MetaDuration.FromMinutes(10)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance to end the activation, and enough that the old configured cooldown would be
            // exceeded, but the new one won't. Thus the offer will still be in cooldown.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(5));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            // Advance past the new configured cooldown. Offer re-activates.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(6*60 + 1));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Increase configured cooldown during cooldown. The cooldown should adjust to reflect the new configuration.
        [Test]
        public void TestConfigChangeIncreaseCooldownDuringCooldown()
        {
            TestOfferId offerId = TestOfferId.FromString("LifetimeAndCooldown");

            ActivableTestPlayerModel player;

            // Create player. Advance to activate the offer, and into the cooldown.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            AdvanceTimeBy(player, MetaDuration.FromMinutes(6));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is in cooldown. Increase the cooldown. Offer stays in cooldown.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeCooldown(configWithChangedOffer, offerId, new MetaActivableCooldownSpec.Fixed(MetaDuration.FromMinutes(10)));
            player = ClonePlayerModel(player, configWithChangedOffer);
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance enough that the old configured cooldown would be exceeded, but the new one won't.
            // Thus the offer will still be in cooldown.
            AdvanceTimeBy(player, MetaDuration.FromMinutes(5));
            CheckIsInCooldown(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            // Advance past the new configured cooldown. Offer re-activates.
            AdvanceTimeBy(player, MetaDuration.FromSeconds(4*60 + 1));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured maximum activation count, during an activation.
        // Ongoing activation should end if it is already past the new activation count.
        [Test]
        public void TestConfigChangeDecreaseMaxActivations()
        {
            TestOfferId offerId = TestOfferId.FromString("Limits");

            ActivableTestPlayerModel player;

            // Create player. Advance enough to start the third activation.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeBy(player, MetaDuration.FromMinutes(11));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is active. Decrease the max activation count to 3 (current NumActivated).
            // Offer stays active, because it's still just within the new limit.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.MaxActivations), 3);
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Offer is active. Decrease the max activation count to 2 (just under current NumActivated).
            // Offer deactivates, because it's above the new limit.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.MaxActivations), 2);
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured total consumption limit, during an activation.
        // Ongoing activation should end if new total consumption limit is reached.
        [Test]
        public void TestConfigChangeDecreaseMaxTotalConsumes()
        {
            TestOfferId offerId = TestOfferId.FromString("Limits");

            ActivableTestPlayerModel player;

            // Create player. Consume offer 3 times.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            for (int i = 0; i < 3; i++)
            {
                player.Tick(null);
                CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
                Assert.True(player.TestOffers.TryConsume(player.GameConfig.TestOffers[offerId], player));
            }
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).TotalNumConsumed);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is active. Decrease the total consumption limit to 4 (just above current TotalNumConsumed).
            // Offer stays active, because new total consumption limit is not reached.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.MaxTotalConsumes), 4);
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Offer is active. Decrease the total consumption limit to 3 (current TotalNumConsumed).
            // Offer deactivates, because new total consumption limit is exactly reached.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.MaxTotalConsumes), 3);
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.False(player.TestOffers.CanStartActivation(player.GameConfig.TestOffers[offerId], player));
            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).TotalNumConsumed);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Decrease configured per-activation consumption limit, during an activation.
        // Ongoing activation should end if new per-activation consumption limit is reached.
        [Test]
        public void TestConfigChangeDecreaseMaxConsumesPerActivation()
        {
            TestOfferId offerId = TestOfferId.FromString("Limits");

            ActivableTestPlayerModel player;

            // Create player. Consume offer once.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.True(player.TestOffers.TryConsume(player.GameConfig.TestOffers[offerId], player));

            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).TotalNumConsumed);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).LatestActivation.Value.NumConsumed);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is active. Clone player once, without changing limits. Should stay active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Decrease per-activation consumption limit to 1 (current activation's NumConsumed).
            // Offer deactivates, because new per-activation consumption limit is exactly reached.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.MaxConsumesPerActivation), 1);
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).TotalNumConsumed);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).LatestActivation.Value.NumConsumed);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Disable an offer via configuration, while the offer is active. Offer should deactivate.
        [Test]
        public void TestConfigChangeDisable()
        {
            TestOfferId offerId = TestOfferId.FromString("Limits");

            ActivableTestPlayerModel player;

            // Create player. Tick to activate offer.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Sanity: Clone without changing config should leave the offer active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active Change offer's configured IsEnabled to false.
            // Offer deactivates.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.IsEnabled), false);
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            // Sanity: ticking does not activate the offer again, because it's disabled.
            player.Tick(null);
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.False(player.TestOffers.CanStartActivation(player.GameConfig.TestOffers[offerId], player));

            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            Assert.AreEqual(0, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).TotalNumConsumed);
        }

        // Change configured utc schedule. Ongoing activation should get adjusted accordingly.
        [Test]
        public void TestConfigChangeScheduleChangeUtc()
        {
            TestOfferId offerId = TestOfferId.FromString("ScheduleBasicUtc");

            ActivableTestPlayerModel player;

            // Create player. Advance into a schedule occasion to activate the offer.
            player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Sanity: Clone without changing config should leave the offer active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Change the configured schedule to start a few minutes later than in the old config.
            // The offer should deactivate, because it is no longer within a schedule occasion.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Utc,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 35, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 10 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is inactive. Advance into the new configured schedule. Offer activates.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 36, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is active. Change the configured schedule to end at a later time.
            // The offer stays active, and its end time is adjusted according to the new configuration.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Utc,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 32, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 17 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance to the just before and just after the end of the new occasion, and check the config change was respected.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 48, 59, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 49, 1, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            // Offer is inactive. Change the schedule to have an ongoing occasion.
            // The offer should stay inactive until ticking is done.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Utc,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 32, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 20 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Change configured schedule by moving it into the past
        // beyond where an activation previously expired.
        // There used to be a bug where this would erroneously
        // cause the code to think the activable is in review.
        [Test]
        public void TestConfigChangeScheduleToPastBeyondActivation()
        {
            TestOfferId offerId = TestOfferId.FromString("ScheduleBasicUtc");

            ActivableTestPlayerModel player;

            // Create player. Advance into a schedule occasion to activate the offer.
            player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Advance further to expire the offer.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 45, 0, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            // Change the configured schedule to far into the past.
            // Nothing happens to the activation, as it's already expired;
            // but in particular, the activable should not be considered to be in review.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Utc,
                        start:      new MetaCalendarDateTime(2020, 6, 8, 10, 30, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 10 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.True(!player.TestOffers.TryGetVisibleStatus(player.GameConfig.TestOffers[offerId], player, out MetaActivableVisibleStatus visibleStatus));
            Assert.IsNull(visibleStatus);

            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Change configured local schedule. Ongoing activation should get adjusted accordingly.
        [Test]
        public void TestConfigChangeScheduleChangeLocal()
        {
            TestOfferId offerId = TestOfferId.FromString("ScheduleBasicLocal");

            ActivableTestPlayerModel player;

            // Create player. Advance into a schedule occasion to activate the offer.
            player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Sanity: Clone without changing config should leave the offer active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Change the configured schedule to start a few minutes later than in the old config.
            // The offer should deactivate, because it is no longer within a schedule occasion.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Local,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 35, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 10 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is inactive. Advance into the new configured schedule. Offer activates.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 36, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is active. Change the configured schedule to end at a later time.
            // The offer stays active, and its end time is adjusted according to the new configuration.
            // At the same time, change the player's utc offset; this change should not affect the current
            // activation.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Local,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 32, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 17 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                // Note: Also changing player's utc offset.
                player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(1));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            // Advance to the just before and just after the end of the new occasion, and check the config change was respected.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 48, 59, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 49, 1, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is inactive. Subsequent activations use player's new utc offset.
            // But first occasion according to the new utc offset is schedule-offset-blocked so it won't get activated.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 9, 29, 0, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.True(player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).IsScheduleOffsetBlocked(player.GetCurrentLocalTime()));
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            // Second occasion according to the new utc offset will get activated.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 29, 0, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 33, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 48, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 49, 1, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is inactive. Change the schedule to have an ongoing occasion.
            // The offer should stay inactive until ticking is done.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Local,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 32, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 20 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(4, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Change configured schedule from local to utc time mode, with a positive utc offset on the player.
        // That is, activation's effective utc offset decreases (from positive to zero).
        // Ongoing activation should get adjusted accordingly.
        [Test]
        public void TestConfigChangeScheduleChangeLocalPositiveToUtc()
        {
            TestOfferId offerId = TestOfferId.FromString("ScheduleBasicLocal");

            ActivableTestPlayerModel player;

            // Create player. Advance into a schedule occasion to activate the offer.
            player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(5));
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 5, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Sanity: Clone without changing config should leave the offer active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Change the configured schedule to have utc instead of local time mode.
            // The offer should deactivate, because it is no longer within a schedule occasion.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Utc,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 30, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 10 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is inactive. Advance to the first occasion of the new configured utc schedule.
            // But it is schedule-offset-blocked at the start, so the offer won't activate at first.
            // The offer will activate a bit further into the occasion, because the schedule-offset-block
            // ends mid-occasion because the activation was force-ended due to the config change.
            // \note This is a bit of an obscure quirk that shows up when schedule-offset-blocking interacts
            //       with activation adjustments caused by config changes that modify the time mode.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 30, 50, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.True(player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).IsScheduleOffsetBlocked(player.GetCurrentLocalTime()));
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 35, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Check subsequent occasions. Those are not schedule-offset-blocked.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 11, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 12, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(4, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            // Check past the last occasion. Offer won't be active.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 13, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(4, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Change configured schedule from local to utc time mode, with a negative utc offset on the player.
        // That is, activation's effective utc offset increases (from negative to zero).
        // Ongoing activation should get adjusted accordingly.
        [Test]
        public void TestConfigChangeScheduleChangeLocalNegativeToUtc()
        {
            TestOfferId offerId = TestOfferId.FromString("ScheduleBasicLocal");

            ActivableTestPlayerModel player;

            // Create player. Advance into a schedule occasion to activate the offer.
            player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 11, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(-1));
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 11, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Sanity: Clone without changing config should leave the offer active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Change the configured schedule to have utc instead of local time mode.
            // The offer in this case stays active, because the it happens to be in an occasion
            // of the new schedule.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Utc,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 30, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 10 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Check next occasion.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 12, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Check past the last occasion. Offer won't be active.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 13, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Change configured schedule from utc to local time mode, with a positive utc offset on the player.
        // That is, activation's effective utc offset increases (from zero to positive).
        // Ongoing activation should get adjusted accordingly.
        [Test]
        public void TestConfigChangeScheduleChangeUtcToLocalPositive()
        {
            TestOfferId offerId = TestOfferId.FromString("ScheduleBasicUtc");

            ActivableTestPlayerModel player;

            // Create player. Advance into a schedule occasion to activate the offer.
            player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(1));
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Sanity: Clone without changing config should leave the offer active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Change the configured schedule to have local instead of utc time mode.
            // The offer in this case stays active, because the it happens to be in an occasion
            // of the new schedule.
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Local,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 30, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 10 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Check next occasion.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 11, 35, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Check past the last occasion. Offer won't be active.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 12, 35, 0, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Change configured schedule from utc to local time mode, with a negative utc offset on the player.
        // That is, activation's effective utc offset decreases (from zero to negative).
        // Ongoing activation should get adjusted accordingly.
        [Test]
        [Ignore("This is broken. Should fix, even if a bit of an edge case.")]
        public void TestConfigChangeScheduleChangeUtcToLocalNegative()
        {
            TestOfferId offerId = TestOfferId.FromString("ScheduleBasicUtc");

            ActivableTestPlayerModel player;

            // Create player. Advance into a schedule occasion to activate the offer.
            player = CreatePlayerModel(MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 0, 0, DateTimeKind.Utc)));
            player.TimeZoneInfo = new PlayerTimeZoneInfo(currentUtcOffset: MetaDuration.FromHours(-5));
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Sanity: Clone without changing config should leave the offer active.
            player = ClonePlayerModel(player, new ActivableTestGameConfig());
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Offer is active. Change the configured schedule to have local instead of utc time mode.
            // The offer should deactivate, because it is no longer within a schedule occasion.
            // \note MetaActivableState is broken here. It treats the activation-local utc offsets
            //       wrong in this case when config changes.
            // \todo [nuutti] Fix
            {
                ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
                ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Schedule), new MetaRecurringCalendarSchedule(
                        timeMode:   MetaScheduleTimeMode.Local,
                        start:      new MetaCalendarDateTime(2021, 6, 8, 10, 30, 0),
                        duration:   new MetaCalendarPeriod{ Minutes = 10 },
                        endingSoon: new MetaCalendarPeriod{ Minutes = 2 },
                        preview:    new MetaCalendarPeriod{ Minutes = 1 },
                        review:     new MetaCalendarPeriod{ Minutes = 3 },
                        recurrence: new MetaCalendarPeriod{ Hours = 1 },
                        numRepeats: 3));
                player = ClonePlayerModel(player, configWithChangedOffer);
            }
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Offer is inactive. Advance to the first occasion of the new configured utc schedule.
            // But it is schedule-offset-blocked at the start, so the offer won't activate at first.
            // The offer will activate a bit further into the occasion, because the schedule-offset-block
            // ends mid-occasion because the activation was force-ended due to the config change.
            // \note This is a bit of an obscure quirk that shows up when schedule-offset-blocking interacts
            //       with activation adjustments caused by config changes that modify the time mode.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 15, 30, 50, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            Assert.True(player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).IsScheduleOffsetBlocked(player.GetCurrentLocalTime()));
            Assert.AreEqual(1, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 15, 35, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(2, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);

            // Check subsequent occasions. Those are not schedule-offset-blocked.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 16, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(3, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 17, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            Assert.AreEqual(4, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
            // Check past the last occasion. Offer won't be active.
            AdvanceTimeTo(player, MetaTime.FromDateTime(new DateTime(2021, 6, 8, 18, 31, 0, DateTimeKind.Utc)));
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);

            Assert.AreEqual(4, player.TestOffers.TryGetState(player.GameConfig.TestOffers[offerId]).NumActivated);
        }

        // Change offer's configured segments so that they no longer match the player.
        // Ongoing activation should *not* be affected.
        [Test]
        public void TestConfigChangeSegment()
        {
            TestOfferId offerId = TestOfferId.FromString("BasicLowGold");

            ActivableTestPlayerModel player;

            // Create player matching the offer's segment.
            player = CreatePlayerModel();
            player.TestRelevantOffers = new List<TestOfferId>{ offerId };
            player.NumGold = 100;

            // Activate offer
            CheckIsActive(false, player, player.GameConfig.TestOffers[offerId]);
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);

            // Change offer's configured segments so that it no longer matches the player.
            // Offer should remain active, as changing segments does not affect activation adjustment.
            ActivableTestGameConfig configWithChangedOffer = new ActivableTestGameConfig();
            ChangeOfferParams(configWithChangedOffer, offerId, nameof(MetaActivableParams.Segments), new List<MetaRef<PlayerSegmentInfoBase>>{ MetaRef<PlayerSegmentInfoBase>.FromItem(configWithChangedOffer.Segments[PlayerSegmentId.FromString("HighGold")]) });
            player = ClonePlayerModel(player, configWithChangedOffer);
            Assert.False(player.GameConfig.TestOffers[offerId].ActivableParams.ConditionsAreFulfilled(player));
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
            player.Tick(null);
            CheckIsActive(true, player, player.GameConfig.TestOffers[offerId]);
        }
    }
}
