// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.League.Messages;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using System;

namespace Metaplay.Core.League
{
    /// <summary>
    /// The phase of the <see cref="LeagueClient{TDivisionModel}"/>.
    /// </summary>
    public enum LeagueClientPhase
    {
        /// <summary>
        /// There is no active Metaplay connection.
        /// </summary>
        NoSession,

        /// <summary>
        /// The participant is not placed into any league division. This could be because the participant
        /// is not Eligible for Leagues or the placement is not yet completed.
        /// </summary>
        NoDivision,

        /// <summary>
        /// The participant is placed into a Division. The the data is not yet available on the client. The
        /// client is currently downloading the required data.
        /// </summary>
        LoadingDivision,

        /// <summary>
        /// The participant is placed into a Division. Note that the division may be in the Running Phase.
        /// If the Season has not started yet, the Division will be in Preview phase.
        /// </summary>
        DivisionActive
    }

    /// <summary>
    /// Contains the information of the just given Division rewards.
    /// </summary>
    public class DivisionRewardClaimResult
    {
        public DivisionIndex DivisionIndex;
        public IDivisionRewards GrantedRewards;

        public DivisionRewardClaimResult(DivisionIndex divisionIndex, IDivisionRewards grantedRewards)
        {
            DivisionIndex = divisionIndex;
            GrantedRewards = grantedRewards;
        }
    }

    /// <summary>
    /// A refusal reason for joining the leagues.
    /// </summary>
    [MetaSerializable]
    public enum LeagueJoinRefuseReason
    {
        /// <summary>
        /// Something unexpected happened.
        /// </summary>
        UnknownReason,
        /// <summary>
        /// The leagues feature is not enabled.
        /// </summary>
        LeagueNotEnabled,
        /// <summary>
        /// The league you are trying to join has not started yet.
        /// </summary>
        LeagueNotStarted,
        /// <summary>
        /// The league season migration is in progress.
        /// </summary>
        SeasonMigrationInProgress,
        /// <summary>
        /// The requirements for joining this league are not met.
        /// </summary>
        RequirementsNotMet,
        /// <summary>
        /// The participant is already in this league.
        /// </summary>
        AlreadyInLeague,
    }

    /// <summary>
    /// Client-side utility for managing League protocol.
    /// </summary>
    public class LeagueClient<TDivisionModel>
        : MultiplayerEntityClientBase<TDivisionModel, DivisionClientContext<TDivisionModel>>
        where TDivisionModel : class, IDivisionModel, IMultiplayerModel<TDivisionModel>
    {
        public override ClientSlot ClientSlot => ClientSlotCore.PlayerDivision;
        protected override string LogChannelName => "division";

        /// <summary>
        /// Current phase of the league connection. See values for details.
        /// </summary>
        public new LeagueClientPhase Phase
        {
            get
            {
                switch (base.Phase)
                {
                    case MultiplayerEntityClientPhase.NoSession: return LeagueClientPhase.NoSession;
                    case MultiplayerEntityClientPhase.NoEntity: return LeagueClientPhase.NoDivision;
                    case MultiplayerEntityClientPhase.LoadingEntity: return LeagueClientPhase.LoadingDivision;
                    case MultiplayerEntityClientPhase.EntityActive: return LeagueClientPhase.DivisionActive;
                    default:
                        throw new InvalidOperationException($"invalid phase {base.Phase}");
                }
            }
        }

        /// <summary>
        /// Currently active division, or <c>null</c> if there is no active division, i.e
        /// <see cref="Phase"/> is not <see cref="ClientPhase.DivisionActive"/>.
        /// </summary>
        public TDivisionModel Division => Context?.CommittedModel;

        /// <summary>
        /// The client context for the current division, or <c>null</c> if no active division.
        /// </summary>
        public new DivisionClientContext<TDivisionModel> Context => base.Context;

        /// <summary>
        /// Current phase of the league season in the <see cref="Division"/>. If there is no active
        /// division, this will be <see cref="DivisionSeasonPhase.NoDivision"/>.
        /// <para>
        /// If there is no current division yet, then the participant is either not Eligible for Leagues or
        /// the participant has not been Placed into the initial division. If the player is eligible, the
        /// UI could for example show the time until the next season starts to provide seamless joining
        /// experience. Next season's start time can be computed from Season Calendar in Game
        /// Config.
        /// </para>
        /// </summary>
        public DivisionSeasonPhase DivisionSeasonPhase { get; private set; }

        /// <summary>
        /// Invoked after <see cref="DivisionSeasonPhase"/> changes. This is invoked on Unity thread. If the season phase change
        /// is caused by the client phase changing, i.e. client entering a new division, <see cref="MultiplayerEntityClientBase{TModel, TContext}.PhaseChanged"/> is invoked first.
        /// </summary>
        public event Action DivisionSeasonPhaseChanged;

        Action<TDivisionModel> _applyClientListeners;

        public LeagueClient() : base()
        {
        }

        public override void Initialize(IMetaplaySubClientServices clientServices)
        {
            base.Initialize(clientServices);
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public override void OnSessionStart(SessionProtocol.SessionStartSuccess successMessage, ClientSessionStartResources sessionStartResources)
        {
            base.OnSessionStart(successMessage, sessionStartResources);
        }

        public override void OnSessionStop()
        {
            base.OnSessionStop();

            InvokeDivisionSeasonPhaseChanged();
        }

        public override void UpdateLogic(MetaTime timeNow)
        {
            base.UpdateLogic(timeNow);

            // Update phase in .Update() to avoid torn read within a frame
            DivisionSeasonPhase oldPhase = DivisionSeasonPhase;
            DivisionSeasonPhase = Division.ComputeSeasonPhaseAt(timeNow);
            if (DivisionSeasonPhase != oldPhase)
                InvokeDivisionSeasonPhaseChanged();
        }

        void InvokeDivisionSeasonPhaseChanged()
        {
            try
            {
                DivisionSeasonPhaseChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Warning("Failed to executed DivisionSeasonPhaseChanged: {Error}", ex.ToString());
            }
        }

        protected override DivisionClientContext<TDivisionModel> CreateActiveModelContext(EntityInitialState state, ISharedGameConfig gameConfig)
        {
            TDivisionModel model = (TDivisionModel)DefaultDeserializeModel(state.State, gameConfig);
            DivisionChannelContextData contextData = (DivisionChannelContextData)state.ContextData;

            model.RefreshScores();
            Context?.SetClientListeners(_applyClientListeners);

            return new DivisionClientContext<TDivisionModel>(DefaultInitArgs(model, state), contextData.ParticipantId);
        }

        protected override void OnActivatingModel()
        {
            DivisionSeasonPhase = Division.ComputeSeasonPhaseAt(MetaTime.Now);
        }

        protected override void OnActivatedModel()
        {
            InvokeDivisionSeasonPhaseChanged();
        }

        public void SetClientListeners(Action<TDivisionModel> applyFn)
        {
            _applyClientListeners = applyFn;
            Context?.SetClientListeners(applyFn);
        }
    }
}
