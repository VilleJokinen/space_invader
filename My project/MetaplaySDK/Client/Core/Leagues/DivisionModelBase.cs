// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;


namespace Metaplay.Core.League
{
    /// <summary>
    /// The current phase of a League Season in a single Division.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaSerializable]
    public enum DivisionSeasonPhase
    {
        /// <summary>
        /// There is no division. The client is not participating in the League.
        /// </summary>
        NoDivision = 0,

        /// <summary>
        /// This division instance's season has not yet started.
        /// </summary>
        Preview,

        /// <summary>
        /// This division instance's season has been started, and is has not ended yet.
        /// </summary>
        Ongoing,

        /// <summary>
        /// This division instance's season has been ended, and the results are being computed but are not yet available.
        /// </summary>
        Resolving,

        /// <summary>
        /// This division instance's season has been ended and the results are in.
        /// </summary>
        Concluded,
    }

    /// <summary>
    /// Default implementation of <see cref="IModelRuntimeData" /> for <see cref="DivisionModelBase{TModel}"/>
    /// </summary>
    public class DivisionModelRuntimeDataBase<TDivisionModel> : MultiplayerModelRuntimeDataBase<TDivisionModel>
        where TDivisionModel : IDivisionModel
    {
        readonly IDivisionModelServerListenerCore ServerListenerCore;
        readonly IDivisionModelClientListenerCore ClientListenerCore;
        readonly AnalyticsEventHandler<IDivisionModel, DivisionEventBase> AnalyticsEventHandler;

        public DivisionModelRuntimeDataBase(TDivisionModel instance) : base(instance)
        {
            this.ServerListenerCore    = instance.ServerListenerCore;
            this.ClientListenerCore    = instance.ClientListenerCore;
            this.AnalyticsEventHandler = instance.AnalyticsEventHandler;
        }

        public override void CopyResolversTo(TDivisionModel instance)
        {
            base.CopyResolversTo(instance);
        }

        public override void CopySideEffectListenersTo(TDivisionModel instance)
        {
            base.CopySideEffectListenersTo(instance);

            instance.SetServerListenerCore(this.ServerListenerCore);
            instance.SetClientListenerCore(this.ClientListenerCore);
            instance.AnalyticsEventHandler = this.AnalyticsEventHandler;
        }
    }

    /// <summary>
    /// Default base type for Division Model Participants.
    /// </summary>
    /// <typeparam name="TDivisionScore">The concrete type defining the unit for a participant's total score.</typeparam>
    [MetaReservedMembers(100, 200)]
    public abstract class DivisionParticipantStateBase<TDivisionScore> : IDivisionParticipantState
        where TDivisionScore : IDivisionScore, new()
    {
        /// <inheritdoc />
        public int      ParticipantIndex { get; set; }

        /// <inheritdoc />
        public EntityId ParticipantId    { get; set; }

        /// <inheritdoc cref="IDivisionParticipantState.DivisionScore"/>
        public TDivisionScore                                 DivisionScore             { get; set; }
        public int                                            SortOrderIndex            { get; set; }

        [MetaMember(101), ServerOnly] public int              AvatarDataEpoch           { get; set; }

        [MetaMember(102), ServerOnly] public IDivisionRewards ResolvedDivisionRewards { get; set; }

        public abstract                      string           ParticipantInfo         { get; }

        [IgnoreDataMember]
        IDivisionScore IDivisionParticipantState.DivisionScore
        {
            get => DivisionScore;
            set => DivisionScore = (TDivisionScore)value;
        }

        protected DivisionParticipantStateBase() { }
        protected DivisionParticipantStateBase(EntityId participantId)
        {
            ParticipantId = participantId;
            DivisionScore = new TDivisionScore();
        }
    }

    /// <summary>
    /// Base type for Division Models.
    /// </summary>
    /// <typeparam name="TModel">The concrete Model type, i.e. the inheriting type itself.</typeparam>
    /// <typeparam name="TParticipantState">The concrete type containing per-participant data.</typeparam>
    /// <typeparam name="TDivisionScore">The concrete type defining the unit for a participant's total score.</typeparam>
    [LeaguesEnabledCondition]
    [MetaReservedMembers(300, 400)]
    public abstract class DivisionModelBase<TModel, TParticipantState, TDivisionScore> : MultiplayerModelBase<TModel>, IDivisionModel<TModel>
        where TModel : DivisionModelBase<TModel, TParticipantState, TDivisionScore>
        where TParticipantState : IDivisionParticipantState
        where TDivisionScore : IDivisionScore
    {
        class ScoreComparer : IComparer<int>
        {
            readonly DivisionModelBase<TModel, TParticipantState, TDivisionScore> _model;
            public ScoreComparer(DivisionModelBase<TModel, TParticipantState, TDivisionScore> model)
            {
                _model = model;
            }

            public int Compare(int x, int y)
            {
                // \note: Descending order (best first), so flip the sign
                // On tie, force order based on Id
                int order = _model.CompareScore(x, y);
                if (order != 0)
                    return -order;
                return -x.CompareTo(y);
            }
        }

        [IgnoreDataMember] public IDivisionModelServerListenerCore ServerListenerCore => GetServerListenerCore();
        [IgnoreDataMember] public IDivisionModelClientListenerCore ClientListenerCore => GetClientListenerCore();
        IDivisionModelServerListenerCore IDivisionModel.ServerListenerCore => GetServerListenerCore();
        IDivisionModelClientListenerCore IDivisionModel.ClientListenerCore => GetClientListenerCore();
        public abstract void SetServerListenerCore(IDivisionModelServerListenerCore listener);
        public abstract void SetClientListenerCore(IDivisionModelClientListenerCore listener);
        protected abstract IDivisionModelServerListenerCore GetServerListenerCore();
        protected abstract IDivisionModelClientListenerCore GetClientListenerCore();

        [IgnoreDataMember] public AnalyticsEventHandler<IDivisionModel, DivisionEventBase> AnalyticsEventHandler { get; set; } = AnalyticsEventHandler<IDivisionModel, DivisionEventBase>.NopHandler;

        [IgnoreDataMember]
        public ContextWrappingAnalyticsEventHandler<IDivisionModel, DivisionEventBase> EventStream
            => new ContextWrappingAnalyticsEventHandler<IDivisionModel, DivisionEventBase>(context: this, handler: AnalyticsEventHandler);


        [MetaMember(303)] public DivisionIndex                             DivisionIndex      { get; set; }
        [MetaMember(309)] public OrderedDictionary<int, TParticipantState> Participants       { get; set; } = new OrderedDictionary<int, TParticipantState>();
        [MetaMember(305)] public MetaTime                                  StartsAt           { get; set; }
        [MetaMember(306)] public MetaTime                                  EndsAt             { get; set; }
        [MetaMember(308)] public MetaTime                                  EndingSoonStartsAt { get; set; }
        [MetaMember(307)] public bool                                      IsConcluded        { get; set; }

        /// <inheritdoc />
        [MetaMember(311)] public int NextParticipantIdx { get; set; } = 1;

        [MetaMember(310), ServerOnly] public IDivisionServerModel ServerModel { get; set; }
        [Obsolete("Use Participants instead.")]
        [MetaMember(304)] public OrderedDictionary<EntityId, TParticipantState> LegacyParticipants { get; set; }

        #if NETCOREAPP
        /// <inheritdoc />
        public int GetParticipantIndexById(EntityId participantId)
        {
            return ServerModel.ParticipantIndexToEntityId.FirstOrDefault(
                x => x.Value == participantId, new KeyValuePair<int, EntityId>(-1, EntityId.None)).Key;
        }
        #endif

        protected DivisionModelBase() { }

        [MetaOnDeserialized]
        void SetExistingParticipantIds()
        {
            // Set the ParticipantId during deserialization. Unlike Score, this cannot require
            // GameConfigs and can always be done early.
            foreach ((int participantIndex, TParticipantState participant) in Participants)
            {
                participant.ParticipantIndex = participantIndex;
                if(ServerModel != null)
                    participant.ParticipantId = ServerModel.ParticipantIndexToEntityId.GetValueOrDefault(participantIndex, EntityId.None);
            }
        }

        /// <summary>
        /// Computes the total score for a participant. See <see cref="IDivisionScore"/> for
        /// considerations.
        /// </summary>
        public abstract TDivisionScore ComputeScore(int participantIndex);

        /// <summary>
        /// Ordering function for the scores. This is used for the sort order of the division scores.
        /// When this is called, the participant scores have been updated and they do not need to be
        /// recomputed for the comparison. By default, uses the <see cref="IDivisionScore.CompareTo"/>
        /// <para>
        /// Returns less than 0, if <paramref name="lhs"/> is <i>worse</i> than <paramref name="rhs"/>.<br/>
        /// Returns greater than 0, if <paramref name="lhs"/> is <i>better</i> than <paramref name="rhs"/>.<br/>
        /// Otherwise, returns 0.
        /// </para>
        /// </summary>
        public virtual int CompareScore(int lhs, int rhs)
        {
            IDivisionScore lhsScoreItem = Participants[lhs].DivisionScore;
            IDivisionScore rhsScoreItem = Participants[rhs].DivisionScore;
            return lhsScoreItem.CompareTo(rhsScoreItem);
        }

        /// <summary>
        /// Removes the participant from the Division, or alternatively marks the participant as inactive. Default implementation removes
        /// the participant from <see cref="Participants"/> and from the server model on the server.
        /// </summary>
        public virtual void RemoveParticipant(int participantIndex)
        {
            Participants.Remove(participantIndex);
            ServerModel?.ParticipantIndexToEntityId.Remove(participantIndex);
        }

        void IDivisionModel.RefreshScores()
        {
            // Restore participant scores
            foreach ((int participant, TParticipantState state) in Participants)
                state.DivisionScore = ComputeScore(participant);

            // Update score sort orders
            foreach ((int key, int index) in Participants.Keys.OrderBy(x => x, new ScoreComparer(this)).Select((x, i) => (x, i)))
                Participants[key].SortOrderIndex = index;
        }

        public override IModelRuntimeData<TModel> GetRuntimeData() => new DivisionModelRuntimeDataBase<TModel>((TModel)this);

        bool IDivisionModel.TryGetParticipant(int participantIndex, out IDivisionParticipantState participant)
        {
            bool success = Participants.TryGetValue(participantIndex, out TParticipantState typedParticipant);
            participant = typedParticipant;
            return success;
        }

        IEnumerable<int> IDivisionModel.EnumerateParticipants() => Participants.Keys;
    }
}
