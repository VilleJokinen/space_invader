// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using System.Collections.Generic;

namespace Metaplay.Core.League
{
    /// <summary>
    /// Allows the <see cref="IDivisionServerModel"/> to dispatch actions inside the Tick method.
    /// </summary>
    public interface IServerActionDispatcher
    {
        /// <summary>
        /// Execute the specified action on the actor.
        /// </summary>
        void ExecuteAction(ModelAction action);
    }

    [MetaSerializable]
    [LeaguesEnabledCondition]
    public interface IDivisionServerModel
    {
        OrderedDictionary<int, EntityId> ParticipantIndexToEntityId { get; }

        /// <summary>
        /// Tick the server model. This should not change any state in the main model,
        /// but is allowed to change the state of the server model. Any changes to the main model
        /// should be done via the action dispatcher.
        /// </summary>
        /// <param name="readOnlyModel">The parent <see cref="IDivisionModel"/> of this server model. Do not modify.</param>
        void OnModelServerTick(IDivisionModel readOnlyModel, IServerActionDispatcher actionDispatcher);

        /// <summary>
        /// Called after model time was fast forwarded by specified amount of time. Used when
        /// time has progressed between when the actor was last active and now. This method can
        /// change the state of both the main model and the server model, since the model has not
        /// been sent to the client yet.
        /// </summary>
        /// <param name="model">The parent <see cref="IDivisionModel"/> of this server model.</param>
        /// <param name="elapsedTime">The amount of time that was fast forwarded</param>
        void OnFastForwardModel(IDivisionModel model, MetaDuration elapsedTime);
    }

    /// <summary>
    /// Untyped subset of <see cref="IDivisionModel{TModel}"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    public interface IDivisionModel : IMultiplayerModel
    {
        AnalyticsEventHandler<IDivisionModel, DivisionEventBase>                AnalyticsEventHandler { get; set; }
        ContextWrappingAnalyticsEventHandler<IDivisionModel, DivisionEventBase> EventStream           { get; }
        IDivisionModelServerListenerCore                                        ServerListenerCore    { get; }
        IDivisionModelClientListenerCore                                        ClientListenerCore    { get; }

        /// <summary>
        /// Server-only data of the model. <c>null</c> on Client.
        /// </summary>
        IDivisionServerModel                                                    ServerModel           { get; set; }

        #if NETCOREAPP
        /// <summary>
        /// <para>
        /// Returns the participant index of the specified participant entity id.
        /// Will not work in the client, so this is compiled out to prevent accidental use.
        /// </para>
        /// <para>
        /// Will return -1 if the participant is not found.
        /// </para>
        /// </summary>
        int GetParticipantIndexById(EntityId participantId);
        #endif

        /// <summary>
        /// Internal API.
        /// </summary>
        void SetServerListenerCore(IDivisionModelServerListenerCore listener);
        /// <summary>
        /// Internal API.
        /// </summary>
        void SetClientListenerCore(IDivisionModelClientListenerCore listener);

        /// <summary>
        /// Identifies this Division instance by its league, season, rank and division instance numbers.
        /// </summary>
        DivisionIndex   DivisionIndex   { get; set; }

        /// <summary>
        /// The point in time when the season starts.
        /// </summary>
        MetaTime        StartsAt        { get; set; }

        /// <summary>
        /// The point in time when the season ends.
        /// </summary>
        MetaTime        EndsAt          { get; set; }

        /// <summary>
        /// The point in time at which the league starts warning that the current season ends soon.
        /// </summary>
        MetaTime        EndingSoonStartsAt { get; set; }

        /// <summary>
        /// True when all scores and rewards have been computed after the season has ended.
        /// </summary>
        bool            IsConcluded     { get; set; }

        /// <summary>
        /// The index for the next participant to be added to this division.
        /// </summary>
        int NextParticipantIdx { get; set; }

        /// <summary>
        /// Returns true and the participant state if the participant is a participant in this division. Otherwise returns false, and a null state.
        /// </summary>
        bool TryGetParticipant(int participantIndex, out IDivisionParticipantState participant);

        /// <summary>
        /// Enumerates participants in a consistent order.
        /// </summary>
        IEnumerable<int> EnumerateParticipants();

        /// <summary>
        /// Remove an existing participant from this division.
        /// </summary>
        void RemoveParticipant(int participantIndex);

        /// <summary>
        /// (Re)Computes scores and sort order. This is called when model is loaded from
        /// persisted just after assigning GameConfig, when any Contribution of any participant
        /// changes, and when any participant is added or removed from the division.
        /// </summary>
        void RefreshScores();
    }

    /// <summary>
    /// The base interface Division Models. The <typeparamref name="TDivisionModel"/> parameter should be
    /// the type for the concrete division class itself. For example <c>class MyDivision : IDivisionModel&lt;MyDivision&gt;</c>.
    /// </summary>
    /// <typeparam name="TDivisionModel">The concrete model type.</typeparam>
    public interface IDivisionModel<TDivisionModel> : IDivisionModel, IMultiplayerModel<TDivisionModel>
        where TDivisionModel : IMultiplayerModel<TDivisionModel>
    {
    }

    /// <summary>
    /// Necessary state of all Participants in Divisions.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaSerializable]
    public interface IDivisionParticipantState
    {
        /// <summary>
        /// The identifier of the participant in the division. This is only unique within a single division.
        /// </summary>
        int                         ParticipantIndex { get; set; }

        /// <summary>
        /// The EntityId of the participant. This is not the identifier for the participant, and may not be available on the client.
        /// </summary>
        EntityId                    ParticipantId   { get; set; }

        /// <summary>
        /// The computed Score of this participant.
        /// </summary>
        IDivisionScore              DivisionScore   { get; set; }

        /// <summary>
        /// The order index of this participant in the score order. The participant with the highest score has the index
        /// 0, and the next highest has the index 1 and so on. There are no gaps or shared places. If the score of two participants
        /// is equal, an arbitrary order is chosen. Computed value.
        /// </summary>
        int                         SortOrderIndex  { get; set; }

        /// <summary>
        /// Epoch number of avatar updates for this participant. Internal to the SDK.
        /// </summary>
        int                         AvatarDataEpoch { get; set; }

        /// <summary>
        /// The resolved rewards of this participant.
        /// Not defined until season is Concluded. Null means no rewards.
        /// Server-only to prevent snooping by other players.
        /// </summary>
        IDivisionRewards            ResolvedDivisionRewards { get; set; }

        /// <summary>
        /// Info string for this participant to show in the dashboard.
        /// </summary>
        string                      ParticipantInfo { get; }
    }

    public static class DivisionExtensions
    {
        /// <summary>
        /// Computes the <see cref="DivisionSeasonPhase"/> of this division at the given point in time. If <paramref name="division"/> is <c>null</c>,
        /// returns <see cref="DivisionSeasonPhase.NoDivision"/>.
        /// </summary>
        public static DivisionSeasonPhase ComputeSeasonPhaseAt(this IDivisionModel division, MetaTime time)
        {
            if (division == null)
                return DivisionSeasonPhase.NoDivision;
            else if (division.IsConcluded)
                return DivisionSeasonPhase.Concluded;
            else if (time >= division.EndsAt)
                return DivisionSeasonPhase.Resolving;
            else if (time >= division.StartsAt)
                return DivisionSeasonPhase.Ongoing;
            else
                return DivisionSeasonPhase.Preview;
        }
    }
}
