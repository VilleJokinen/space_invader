// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Model;

namespace Metaplay.Server.Matchmaking
{
    /// <summary>
    /// A base interface for all bucketing strategy labels.
    /// A label should not implement this interface directly, but instead implement
    /// either <see cref="IDistinctBucketLabel{T}"/> or <see cref="IRangedBucketLabel{T}"/>.
    /// </summary>
    [MetaSerializable]
    public interface IBucketLabel
    {
        /// <summary>
        /// Label to show in the dashboard.
        /// </summary>
        string DashboardLabel { get; }
    }

    /// <summary>
    /// Uses <see cref="IBucketLabel.GetHashCode"/> to combine a single hash code.
    /// </summary>
    public struct LabelHashBuilder
    {
        int _hash;

        public void Add(IBucketLabel label)
        {
            _hash = (_hash * 32) ^ label?.GetHashCode() ?? 0;
        }

        public int Build() => _hash;
    }

    public static class IBucketLabelExtensions
    {
        public static int HashLabels(this IEnumerable<IBucketLabel> labels)
        {
            LabelHashBuilder hashBuilder = default;
            foreach (IBucketLabel label in labels)
                hashBuilder.Add(label);
            return hashBuilder.Build();
        }
    }

    /// <summary>
    /// A label for distinct, non-comparable bucketing strategies.
    /// For example, arena selection, area (europe, asia, us) or
    /// faction.
    /// </summary>
    public interface IDistinctBucketLabel<T> : IBucketLabel, IEquatable<T>
        where T : IDistinctBucketLabel<T>
    { }

    public interface IOrderedBucketLabel : IBucketLabel, IComparable
    { }

    /// <summary>
    /// A label for a ranged bucketing strategy label, like
    /// MMR rating, player level, etc.
    /// These should be comparable and equatable to each other.
    /// </summary>
    public interface IRangedBucketLabel<T> : IOrderedBucketLabel, IEquatable<T>
        where T : IRangedBucketLabel<T>
    { }

    /// <summary>
    /// A base interface for a bucketing strategy state.
    /// Any state has to be unique to their type of bucketing strategy,
    /// even if some strategies could technically share the same kind of state.
    /// </summary>
    [MetaSerializable]
    public interface IAsyncMatchmakerBucketingStrategyState { }

    public enum BucketingStrategyType
    {
        /// <summary>
        /// When strategy type is unknown.
        /// </summary>
        Unknown,
        /// <summary>
        /// A sliding range like MMR.
        /// Close-by buckets are treated more favorably.
        /// </summary>
        Range,

        /// <summary>
        /// A distinct category, like a league or selected arena.
        /// No preference for bucket closeness.
        /// </summary>
        Distinct,
    }

    public interface IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery>
    {
        IAsyncMatchmakerBucketingStrategyState State { get; set; }

        /// <summary>
        /// If the state is set, this should be set too. The state type should be unique for each strategy.
        /// </summary>
        Type StateType { get; }

        /// <summary>
        /// The label type of this strategy.
        /// </summary>
        Type LabelType { get; }

        /// <summary>
        /// If false, matches can be made from nearby buckets as well, but the same bucket is
        /// preferred. If true, only buckets with the same label are considered.
        /// </summary>
        bool IsHardRequirement { get; }


        /// <summary>
        /// The <see cref="BucketingStrategyType"/> of this bucketing strategy.
        /// </summary>
        public BucketingStrategyType StrategyType { get; }

        /// <summary>
        /// Get a label for a player. These labels should be equatable to
        /// the labels generated for queries.
        /// </summary>
        public IBucketLabel GetBucketLabel(TMmPlayerModel model);

        /// <summary>
        /// Get a label for a query. These labels should be equatable to
        /// the labels generated for players.
        /// </summary>
        public IBucketLabel GetBucketLabel(TMatchmakerQuery query);

        IAsyncMatchmakerBucketingStrategyState InitializeNew(AsyncMatchmakerOptionsBase matchmakerOptions);
        void PostLoad(AsyncMatchmakerOptionsBase matchmakerOptions);
        void OnResetState(AsyncMatchmakerOptionsBase matchmakerOptions);
    }

    /// <summary>
    /// <para>
    /// A base class for any matchmaker bucketing strategy that does not
    /// require a state object. The <typeparamref name="TLabel"/> must implement
    /// either <see cref="IDistinctBucketLabel{T}"/> or <see cref="IRangedBucketLabel{T}"/>
    /// for the matchmaking to work correctly.
    /// </para>
    /// <para>
    /// If a bucketing strategy is set as a hard requirement (with <see cref="IsHardRequirement"/>),
    /// only players that have the same label can be matched together.
    /// </para>
    /// </summary>
    public abstract class AsyncMatchmakerBucketingStrategyBase<TLabel, TMmPlayerModel, TMatchmakerQuery> :
        IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery>
        where TLabel : IBucketLabel
        where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
        where TMatchmakerQuery : class, IAsyncMatchmakerQuery, new()
    {
        public IAsyncMatchmakerBucketingStrategyState State { get; set; }

        /// <summary>
        /// If the state is set, this should be set too. The state type be unique for each strategy.
        /// </summary>
        public virtual Type StateType => null;

        /// <inheritdoc />
        public Type LabelType => typeof(TLabel);

        /// <inheritdoc cref="IAsyncMatchmakerBucketingStrategy{TMmPlayerModel,TMatchmakerQuery}.IsHardRequirement"/>
        public abstract bool IsHardRequirement { get; }

        /// <summary>
        /// The <see cref="BucketingStrategyType"/> of this bucketing strategy.
        /// </summary>
        public BucketingStrategyType StrategyType { get; } = DetermineStrategyType();

        IBucketLabel IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery>.GetBucketLabel(TMmPlayerModel model)
            => GetBucketLabel(model);
        IBucketLabel IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery>.GetBucketLabel(TMatchmakerQuery query)
            => GetBucketLabel(query);

        /// <inheritdoc cref="IAsyncMatchmakerBucketingStrategy{TMmPlayerModel,TMatchmakerQuery}.GetBucketLabel(TMmPlayerModel)" />
        public abstract TLabel GetBucketLabel(TMmPlayerModel model);

        /// <inheritdoc cref="IAsyncMatchmakerBucketingStrategy{TMmPlayerModel,TMatchmakerQuery}.GetBucketLabel(TMatchmakerQuery)" />
        public abstract TLabel GetBucketLabel(TMatchmakerQuery query);

        public virtual IAsyncMatchmakerBucketingStrategyState InitializeNew(AsyncMatchmakerOptionsBase matchmakerOptions) => null;

        /// <summary>
        /// Called after the matchmaker state has been restored from persisted
        /// or a new one has been initialized.
        /// </summary>
        public virtual void PostLoad(AsyncMatchmakerOptionsBase matchmakerOptions) {}

        /// <summary>
        /// Called when the matchmaker state is reset.
        /// </summary>
        public virtual void OnResetState(AsyncMatchmakerOptionsBase matchmakerOptions) {}

        static BucketingStrategyType DetermineStrategyType()
        {
            System.Type labelType = typeof(TLabel);
            if (labelType.GetInterfaces().Any(
                    x => x.IsGenericType && x.GetGenericTypeDefinition() ==
                        typeof(IRangedBucketLabel<>)))
                return BucketingStrategyType.Range;
            if (labelType.GetInterfaces().Any(
                    x => x.IsGenericType && x.GetGenericTypeDefinition() ==
                        typeof(IDistinctBucketLabel<>)))
                return BucketingStrategyType.Distinct;

            return BucketingStrategyType.Unknown;
        }
    }

    /// <summary>
    /// A base class for <see cref="IAsyncMatchmakerBucketingStrategy{TMmPlayerModel,TMatchmakerQuery}"/>
    /// which takes in a State parameter. The state can be used to persist any
    /// needed state for this bucketing strategy.
    /// </summary>
    public abstract class AsyncMatchmakerBucketingStrategyBase<TLabel, TStrategyState, TMmPlayerModel, TMatchmakerQuery> :
        AsyncMatchmakerBucketingStrategyBase<TLabel, TMmPlayerModel, TMatchmakerQuery>
        where TLabel : IBucketLabel
        where TStrategyState : class, IAsyncMatchmakerBucketingStrategyState, new()
        where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
        where TMatchmakerQuery : class, IAsyncMatchmakerQuery, new()
    {
        public new TStrategyState State
        {
            get => (TStrategyState)base.State;
            set => base.State = value;
        }
        public override Type StateType => typeof(TStrategyState);

        public override IAsyncMatchmakerBucketingStrategyState InitializeNew(AsyncMatchmakerOptionsBase matchmakerOptions)
        {
            return new TStrategyState();
        }
    }

    /// <summary>
    /// An interface for bucketing strategies that want to somehow
    /// rebalance themselves. The SDK base matchmaker calls these methods
    /// for all registered bucketing strategies that implement
    /// this interface.
    /// </summary>
    /// <typeparam name="TMmPlayerModel"></typeparam>
    public interface IAutoRebalancingBucketingStrategy<TMmPlayerModel>
        where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
    {
        /// <summary>
        /// Called when collecting a sample from a player.
        /// </summary>
        void CollectSample(TMmPlayerModel model);

        /// <summary>
        /// Called when a rebalancing operation happens.
        /// All rebalancing should ideally happen inside this method, and not
        /// during sample collection.
        /// </summary>
        bool OnRebalance(AsyncMatchmakerOptionsBase options);
    }
}
