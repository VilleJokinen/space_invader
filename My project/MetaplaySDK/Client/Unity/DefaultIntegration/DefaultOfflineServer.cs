// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Localization;
using Metaplay.Core.Memory;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Network;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using Metaplay.Unity.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    public class MetaplayOfflineOptions
    {
        /// <summary>
        /// Should player state be persisted in Offline Mode? If enabled, the player state is persisted
        /// in <see cref="MetaplaySDK.PersistentDataPath"> and loaded when starting the client in Offline
        /// Mode.
        /// </summary>
        public bool PersistState = true;
    }

    [MetaSerializable]
    public class DefaultPersistedOfflineState
    {
        [MetaSerializable]
        public struct PersistedEntityState
        {
            [MetaMember(1)] public MetaSerialized<IMultiplayerModel>    Model       { get; private set; }
            [MetaMember(2)] public ClientSlot                           Slot        { get; private set; }
            [MetaMember(3)] public EntityId?                            MemberId    { get; private set; }

            public PersistedEntityState(MetaSerialized<IMultiplayerModel> model, ClientSlot slot, EntityId? memberId)
            {
                Model = model;
                Slot = slot;
                MemberId = memberId;
            }
        }

        [MetaMember(1)] public MetaTime                         PersistedAt         { get; private set; }
        [MetaMember(2)] public int                              PlayerSchemaVersion { get; private set; }
        [MetaMember(3)] public MetaSerialized<IPlayerModelBase> PlayerModel         { get; private set; }
        [MetaMember(4)] public List<PersistedEntityState>       Entities            { get; private set; }

        DefaultPersistedOfflineState() { }
        public DefaultPersistedOfflineState(MetaTime persistedAt, int playerSchemaVersion, MetaSerialized<IPlayerModelBase> playerModel, List<PersistedEntityState> entities)
        {
            PersistedAt         = persistedAt;
            PlayerSchemaVersion = playerSchemaVersion;
            PlayerModel         = playerModel;
            Entities            = entities;
        }
    }

    public class DefaultOfflineServer
        : IOfflineServer
        , IPlayerModelServerListenerCore
    {
        public static readonly EntityId     OfflinePlayerId     = EntityId.Create(EntityKindCore.Player, 2);

        protected LogChannel                _log                = MetaplaySDK.Logs.CreateChannel("offline");

        OfflineServerTransport              _transport;
        protected int                       _logicVersion       = MetaplayCore.Options.SupportedLogicVersions.MaxVersion;
        protected MetaplayOfflineOptions    _offlineOptions;
        protected ISharedGameConfig         _gameConfig;
        OrderedDictionary<LanguageId, ContentHash> _localizationVersions;
        IClientPlayerModelJournal           _playerJournal;

        protected IPlayerModelBase          PlayerModel         => _playerJournal?.StagedModel ?? null;

        class JournalPositionedAction
        {
            public JournalPosition  Position;
            public Action           Invoke;

            public JournalPositionedAction (JournalPosition position, Action invoke)
            {
                Position    = position;
                Invoke      = invoke;
            }
        }

        Queue<JournalPositionedAction> _postponedActions = new Queue<JournalPositionedAction>();
        SessionStartState? _sessionStartState;
        Exception _sessionStartFailure;

        public virtual async Task InitializeAsync(MetaplayOfflineOptions offlineOptions)
        {
            _offlineOptions = offlineOptions;
            _gameConfig = await LoadBuiltinGameConfigAsync();

            _localizationVersions = new OrderedDictionary<LanguageId, ContentHash>();
            if (MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                _localizationVersions = BuiltinLanguageRepository.GetBuiltinLanguages();
        }

        public static string GetPersistedStatePath()
        {
            return Path.Combine(MetaplaySDK.PersistentDataPath, "MetaplayOfflineState.mpb");
        }

        public virtual void TryPersistState()
        {
            // Persist state (if enabled)
            if (_offlineOptions.PersistState && PlayerModel != null)
            {
                string filePath = GetPersistedStatePath();
                SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator(PlayerModel.GetType());
                int schemaVersion = migrator.SupportedSchemaVersions.MaxVersion;
                _log.Info("Persisting offline state to {FilePath} (schema v{SchemaVersion})", filePath, schemaVersion);

                List<DefaultPersistedOfflineState.PersistedEntityState> entities = new List<DefaultPersistedOfflineState.PersistedEntityState>();
                foreach (ChannelEntity entity in _channelEntities.Values)
                    entities.Add(new DefaultPersistedOfflineState.PersistedEntityState(new MetaSerialized<IMultiplayerModel>(entity.Model, MetaSerializationFlags.Persisted, _logicVersion), entity.Slot, entity.MemberId));

                DefaultPersistedOfflineState persistedState = new DefaultPersistedOfflineState(
                    persistedAt:            MetaTime.Now,
                    playerSchemaVersion:    schemaVersion,
                    playerModel:            new MetaSerialized<IPlayerModelBase>(PlayerModel, MetaSerializationFlags.Persisted, _logicVersion),
                    entities:               entities);

                // Write to file atomically
                byte[] bytes = MetaSerialization.SerializeTagged(persistedState, MetaSerializationFlags.Persisted, _logicVersion);
                AtomicBlobStore.TryWriteBlob(filePath, bytes);
            }
        }

        public void SetPlayerJournal(IClientPlayerModelJournal playerJournal)
        {
            _playerJournal = playerJournal;
            PlayerModel.ServerListenerCore = this;
            SetSelfAsPlayerListener(PlayerModel);
        }

        /// <inheritdoc />
        public virtual void SetListeners(MetaplayClientStore clientStore) { }

        protected virtual void SetSelfAsPlayerListener(IPlayerModelBase playerBase) { }

        void IOfflineServer.RegisterTransport(OfflineServerTransport transport)
        {
            _transport = transport;
        }

        protected struct SessionStartState
        {
            public struct EntityState
            {
                public readonly IMultiplayerModel    Model;
                public readonly ClientSlot           Slot;
                public readonly EntityId?            MemberId;

                public EntityState(IMultiplayerModel model, ClientSlot slot, EntityId? memberId)
                {
                    Model = model;
                    Slot = slot;
                    MemberId = memberId;
                }
            }

            public bool                 IsFirstLogin;
            public IPlayerModelBase     PlayerModel;
            public List<EntityState>    Entities;

            public SessionStartState(bool isFirstLogin, IPlayerModelBase playerModel, List<EntityState> entities)
            {
                IsFirstLogin = isFirstLogin;
                PlayerModel = playerModel;
                Entities = entities;
            }
        }

        protected virtual SessionStartState CreatePlayerModelForSession(ISharedGameConfig gameConfig)
        {
            MetaTime now = MetaTime.Now;
            MetaDuration elapsedAsPersisted = MetaDuration.Zero;
            bool didLoadPersisted = false;
            IPlayerModelBase playerModel = null;
            List<DefaultPersistedOfflineState.PersistedEntityState> entityStates = null;

            // If OfflineOptions.PersistState is true, try to restore player state from file
            if (_offlineOptions.PersistState)
            {
                string filePath = GetPersistedStatePath();
                byte[] bytes = AtomicBlobStore.TryReadBlob(filePath);
                if (bytes != null)
                {
                    // Deserialize DefaultPersistedOfflineState
                    DefaultPersistedOfflineState persisted;
                    try
                    {
                        persisted = MetaSerialization.DeserializeTagged<DefaultPersistedOfflineState>(bytes, MetaSerializationFlags.Persisted, resolver: gameConfig, _logicVersion);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Failed to deserialize DefaultPersistedOfflineState from file {FileName}: {Exception}", filePath, ex);
                        throw;
                    }

                    // If schema version within supported range, deserialize PlayerModel
                    Type playerModelType = IntegrationRegistry.GetSingleIntegrationType<IPlayerModelBase>();
                    SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator(playerModelType);
                    if (persisted.PlayerSchemaVersion >= migrator.SupportedSchemaVersions.MinVersion && persisted.PlayerSchemaVersion <= migrator.SupportedSchemaVersions.MaxVersion)
                    {
                        // Deserialize PlayerModel
                        try
                        {
                            playerModel = persisted.PlayerModel.Deserialize(resolver: gameConfig, _logicVersion);
                            _log.Info("Restored PlayerModel (schema v{SchemaVersion}) from file {FileName}", persisted.PlayerSchemaVersion, filePath);
                            entityStates = persisted.Entities ?? new List<DefaultPersistedOfflineState.PersistedEntityState>();
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Failed to deserialize PlayerModel from persisted offline state from file {FileName} (schema v{SchemaVersion}): {Exception}", filePath, persisted.PlayerSchemaVersion, ex);
                            throw;
                        }

                        // Assign runtime properties
                        playerModel.LogicVersion = _logicVersion;
                        playerModel.GameConfig = gameConfig;
                        playerModel.Log = _log;
                        playerModel.AnalyticsEventHandler = Core.Analytics.AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>.NopHandler;
                        playerModel.ResetTime(now);

                        // Execute schema migrations
                        _log.Info("Migrating from schema v{FromSchemaVersion} to v{ToSchemaVersion}", persisted.PlayerSchemaVersion, migrator.SupportedSchemaVersions.MaxVersion);
                        migrator.RunMigrations(playerModel, persisted.PlayerSchemaVersion);

                        // Compute elapsed time (clamp negative values to zero)
                        // \todo [petri] accumulate offline time & keep track of negative durations to help detect cheaters
                        _log.Info("Elapsed time since state was persisted: {ElapsedAsPersisted}", elapsedAsPersisted);
                        elapsedAsPersisted = new MetaDuration(Math.Max(0, elapsedAsPersisted.Milliseconds));

                        didLoadPersisted = true;
                    }
                    else
                        _log.Info("Persisted PlayerSchemaVersion {SchemaVersion} outside supported range ({SupportedSchemaVersions})", persisted.PlayerSchemaVersion, migrator.SupportedSchemaVersions);
                }
            }

            // Player state wasn't restored from disk, create a new one
            if (!didLoadPersisted)
            {
                string name = $"Guest {new Random().Next(100000)}";
                playerModel = PlayerModelUtil.CreateNewPlayerModel(MetaTime.Now, gameConfig, OfflinePlayerId, name);
                entityStates = new List<DefaultPersistedOfflineState.PersistedEntityState>();
            }

            // Assign runtime properties
            playerModel.LogicVersion = _logicVersion;
            playerModel.GameConfig = gameConfig;
            playerModel.Log = _log;
            playerModel.AnalyticsEventHandler = Core.Analytics.AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>.NopHandler;

            // Update model to current time
            playerModel.OnRestoredFromPersistedState(now, elapsedAsPersisted);

            List<SessionStartState.EntityState> entities = new List<SessionStartState.EntityState>();
            foreach (DefaultPersistedOfflineState.PersistedEntityState entityState in entityStates)
            {
                try
                {
                    IMultiplayerModel model = entityState.Model.Deserialize(resolver: gameConfig, _logicVersion);

                    model.LogicVersion  = _logicVersion;
                    model.GameConfig    = gameConfig;
                    model.Log           = CreateLogForEntity(model);

                    if (didLoadPersisted)
                    {
                        model.ResetTime(now);
                        model.OnFastForwardTime(elapsedAsPersisted);
                    }

                    model.ResetTime(now);

                    entities.Add(new SessionStartState.EntityState(model, entityState.Slot, entityState.MemberId));
                }
                catch (MetaSerializationException ex)
                {
                    _log.Error("Failed to deserialize persisted MultiplayerModel offline state for slot {ClientSlot} from file {FileName}: {Exception}", entityState.Slot, GetPersistedStatePath(), ex);
                    throw;
                }
            }

            return new SessionStartState(isFirstLogin: !didLoadPersisted, playerModel, entities);
        }

        protected virtual void OnSessionStart(IPlayerModelBase model)
        {
        }

        void IOfflineServer.HandleMessage(MetaMessage msg)
        {
            _log.Debug("Received {Message}", PrettyPrint.Compact(msg));
            switch (msg)
            {
                case Handshake.ClientHello login:
                {
                    SendToClient(new Handshake.ClientHelloAccepted(
                        serverOptions: new Handshake.ServerOptions(
                            pushUploadPercentageSessionStartFailedIncidentReport:   0,
                            enableWireCompression:                                  false,
                            deletionRequestSafetyDelay:                             MetaDuration.FromDays(7),
                            gameServerGooglePlaySignInOAuthClientId:                null,
                            immutableXLinkApiUrl:                                   "https://link.sandbox.x.immutable.com",
                            gameEnvironment:                                        "offline")
                        ));
                    break;
                }

                case Handshake.CreateGuestAccountRequest _:
                    throw new InvalidOperationException("Offline server cannot create accounts. Offline credentials are generated by credential manager.");

                case Handshake.LoginRequest login:
                {
                    SendToClient(new Handshake.LoginSuccessResponse(loggedInPlayerId: OfflinePlayerId));

                    // Prepare game state
                    try
                    {
                        _sessionStartState = CreatePlayerModelForSession(_gameConfig);
                        _sessionStartFailure = null;
                    }
                    catch (Exception ex)
                    {
                        _sessionStartState = null;
                        _sessionStartFailure = ex;
                    }
                    break;
                }

                case SessionProtocol.SessionStartRequest sessionStart:
                {
                    if (_sessionStartFailure != null)
                    {
                        SessionProtocol.SessionStartFailure.ReasonCode reasonCode;
                        switch (_sessionStartFailure)
                        {
                            case MetaSerializationException _:  reasonCode = SessionProtocol.SessionStartFailure.ReasonCode.PlayerDeserializationFailure;  break;
                            default:                            reasonCode = SessionProtocol.SessionStartFailure.ReasonCode.InternalError;                 break;
                        }

                        SendToClient(new SessionProtocol.SessionStartFailure(
                            queryId:                sessionStart.QueryId,
                            reason:                 reasonCode,
                            debugOnlyErrorMessage:  _sessionStartFailure.ToString()));
                        return;
                    }

                    MetaDebug.Assert(_sessionStartState != null, "must login before session start attempt");

                    SessionProtocol.SessionResourceCorrection resourceCorrection = GetResourceCorrection(sessionStart, _sessionStartState.Value);
                    if (resourceCorrection.HasAnyCorrection())
                    {
                        SendToClient(new SessionProtocol.SessionStartResourceCorrection(
                            queryId:                sessionStart.QueryId,
                            resourceCorrection:     resourceCorrection));
                        return;
                    }

                    // Create dummy session
                    EntityId sessionId = EntityId.Create(EntityKindCore.Session, OfflinePlayerId.Value);
                    SessionToken sessionToken = new SessionToken(123456789);

                    // Create player model
                    IPlayerModelBase model = _sessionStartState.Value.PlayerModel;

                    // Update client-reported time zone info to the Model, but first ensure it's valid
                    PlayerTimeZoneInfo newTimeZone  = sessionStart.TimeZoneInfo.GetCorrected();
                    bool               isFirstLogin = model.Stats.TotalLogins == 0;

                    model.UpdateTimeZone(newTimeZone, isFirstLogin);

                    // Set per-session state (dummy)
                    model.SessionDeviceGuid = "fakedevice";
                    model.SessionToken = sessionToken;

                    // Switch to the client-proposed language if the server doesn't have any better knowledge of it (i.e. the first login).
                    // After first login, server sticks to the language (unless client explicitly changes the language).
                    if (model.LanguageSelectionSource == LanguageSelectionSource.AccountCreationAutomatic)
                    {
                        LanguageInfo initLangInfo = model.GameConfig.Languages.GetValueOrDefault(sessionStart.ResourceProposal.ClientActiveLanguage);
                        if (initLangInfo != null)
                        {
                            model.Language = initLangInfo.LanguageId;
                            model.LanguageSelectionSource = LanguageSelectionSource.UserDeviceAutomatic;
                        }
                    }

                    if (_sessionStartState.Value.IsFirstLogin)
                        model.OnInitialLogin();
                    model.OnSessionStarted();

                    OnSessionStart(model);

                    // Note: Enable the following line to log the initial PlayerModel into the console upon "connecting" to the mock server
                    //_log.Info("Initial player state: {InitialPlayerModel}", PrettyPrint.Verbose(model));

                    // Entity states
                    List<EntityInitialState> initialEntities = new List<EntityInitialState>();
                    foreach (SessionStartState.EntityState entityState in _sessionStartState.Value.Entities)
                    {
                        (EntityInitialState initialState, ChannelEntity channelEntity) = CreateMultiplayerEntityInitialState(entityState.Slot, entityState.Model, entityState.MemberId);
                        initialEntities.Add(initialState);
                    }

                    // Send state to client
                    SendToClient(new SessionProtocol.SessionStartSuccess(
                        queryId:                    sessionStart.QueryId,
                        logicVersion:               _logicVersion,
                        sessionToken:               sessionToken,
                        scheduledMaintenanceMode:   null,
                        playerId:                   OfflinePlayerId,
                        playerState:                new SessionProtocol.InitialPlayerState(
                            playerModel:                new MetaSerialized<IPlayerModelBase>(model, MetaSerializationFlags.SendOverNetwork, _logicVersion),
                            currentOperation:           1), // After the Tick()
                        entityStates:               initialEntities,
                        localizationVersions:       _localizationVersions,
                        activeExperiments:          new List<EntityActiveExperiment>(),
                        developerMaintenanceBypass: false,
                        gamePayload:                null,
                        correctedDeviceGuid:        null,
                        resumptionToken:            new byte[] { 1, 2, 3 }
                        ));

                    _sessionStartState = null;
                    break;
                }

                case SessionAcknowledgementMessage acknowledgement:
                    // Do nothing, OfflineServer doesn't keep a queue
                    break;

                case PlayerFlushActions flush:
                {
                    var operations = flush.Operations.Deserialize(PlayerModel.GameConfig, _logicVersion);
                    if (operations.Count > 0)
                    {
                        var lastOp = operations[operations.Count - 1];
                        JournalPosition endPosition = JournalPosition.FromTickOperationStep(lastOp.StartTick, lastOp.OperationIndex, lastOp.NumSteps);
                        SendToClient(new PlayerAckActions(endPosition));
                        FlushPostponedActions(endPosition);
                    }
                    break;
                }

                case MetaRequestMessage request:

                    switch (request.Payload)
                    {
                        case SocialAuthenticateRequest authRequest:
                        {
                            SocialAuthenticationClaimBase claim = authRequest.Claim;
                            _log.Info("Accepting social authentication for {Platform}", claim.Platform);

                            // Store social authentication info into player state
                            // \note: we could extract the social ID from the claim, but that is complex. Just use fake id here.
                            AuthenticationKey authKey = new AuthenticationKey(claim.Platform, "offline-fake-social-id");
                            ExecutePlayerServerAction(new PlayerAttachAuthentication(authKey, new PlayerAuthEntryBase.Default(attachedAt: MetaTime.Now)));

                            // Respond with success
                            MetaSerialized<IPlayerModelBase> noExistingPlayer = new MetaSerialized<IPlayerModelBase>(value: null, MetaSerializationFlags.SendOverNetwork, logicVersion: null);

                            MetaResponse response = new SocialAuthenticateResult(claim.Platform, SocialAuthenticateResult.ResultCode.Success, conflictingPlayerId: EntityId.None, conflictingPlayer: noExistingPlayer, conflictResolutionId: 0, debugOnlyErrorMessage: null);
                            SendToClient(new SessionMetaResponseMessage() { RequestId = request.Id, Payload = response });
                            break;
                        }

                        case ImmutableXLoginChallengeRequest imxChallengeRequest:
                        {
                            MetaResponse response = new ImmutableXLoginChallengeResponse(message: "Login is not supported in OfflineMode. This is a dummy request to allow you test the login flow.", "Test login from offline mode", OfflinePlayerId, MetaTime.Now);
                            SendToClient(new SessionMetaResponseMessage() { RequestId = request.Id, Payload = response });
                            break;
                        }

                        default:
                            _log.Warning("Received unknown request {Request}", PrettyPrint.Compact(request.Payload));
                            break;
                    }
                    break;

                case PlayerScheduleDeletionRequest _:
                    ExecutePlayerServerAction(new PlayerSetIsScheduledForDeletionAt(MetaTime.Now + MetaDuration.FromDays(7), scheduledBy: PlayerDeletionStatus.ScheduledByUser));
                    break;

                case PlayerCancelScheduledDeletionRequest _:
                    ExecutePlayerServerAction(new PlayerSetUnscheduledForDeletion());
                    break;

                case ClientLifecycleHintPausing _:
                case ClientLifecycleHintUnpausing _:
                case ClientLifecycleHintUnpaused _:
                    // ignore app lifecycle hints
                    break;

                case EntityClientToServerEnvelope envelope:
                {
                    if (!_channelEntities.TryGetValue(envelope.ChannelId, out ChannelEntity entity))
                    {
                        _log.Warning("Received entity message on unknown channel {ChannelId}", envelope.ChannelId);
                    }
                    else
                    {
                        MetaMessage contents = envelope.Message.Deserialize(entity.Model.GetDataResolver(), _logicVersion);
                        HandleEntityMessage(entity, contents);
                    }
                    break;
                }

                case EntityActivated activated:
                {
                    if (!_channelEntities.TryGetValue(activated.ChannelId, out ChannelEntity entity))
                    {
                        _log.Warning("Received EntityActivated on unknown channel {ChannelId}", activated.ChannelId);
                    }
                    else
                    {
                        HandleEntityActivatedOnClient(entity);
                    }
                    break;
                }

                default:
                    HandleCustomMessage(msg);
                    break;
            }
        }

        protected virtual void HandleCustomMessage(MetaMessage msg)
        {
            _log.Warning("Received unknown message {Message}", PrettyPrint.Compact(msg));
        }

        public virtual void Update()
        {
            _transport?.Update();

            foreach (ChannelEntity entity in _channelEntities.Values)
                UpdateChannelEntity(entity);
        }

        protected async Task<ISharedGameConfig> LoadBuiltinGameConfigAsync()
        {
            string          configPath = GetBuiltinGameConfigArchivePath();
            ConfigArchive configArchive;
            try
            {
                byte[] readAllBytes = await FileUtil.ReadAllBytesAsync(configPath);
                configArchive = ConfigArchive.FromBytes(readAllBytes);
            }
            catch (FileNotFoundException)
            {
                throw new InvalidOperationException($"Could not find config for OfflineServer mode at {configPath}. Please make sure the config has been built and are present in the correct directory.");
            }

            GameConfigArchive = configArchive;
            return GameConfigUtil.ImportSharedConfig(configArchive);
        }

        public ConfigArchive GameConfigArchive { get; private set; }
        protected virtual string GetBuiltinGameConfigDirectoryPath() => UnityEngine.Application.streamingAssetsPath;
        protected virtual string GetBuiltinGameConfigArchivePath() => Path.Combine(GetBuiltinGameConfigDirectoryPath(), "SharedGameConfig.mpa");

        SessionProtocol.SessionResourceCorrection GetResourceCorrection(SessionProtocol.SessionStartRequest sessionStart, SessionStartState startState)
        {
            SessionProtocol.SessionResourceCorrection correction = new SessionProtocol.SessionResourceCorrection();

            // \hack: In offline, connection will use the builtin anyway
            if (sessionStart.ResourceProposal.ConfigVersions.GetValueOrDefault(ClientSlotCore.Player) != GameConfigArchive.Version)
            {
                correction.ConfigUpdates[ClientSlotCore.Player] = new SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo(
                    sharedGameConfigVersion: GameConfigArchive.Version,
                    urlSuffix: null);
            }

            foreach (SessionStartState.EntityState entity in startState.Entities)
            {
                if (sessionStart.ResourceProposal.ConfigVersions.GetValueOrDefault(entity.Slot) != GameConfigArchive.Version)
                {
                    correction.ConfigUpdates[entity.Slot] = new SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo(
                        sharedGameConfigVersion: GameConfigArchive.Version,
                        urlSuffix: null);
                }
            }

            if (MetaplayCore.Options.FeatureFlags.EnableLocalizations)
            {
                ContentHash correctLocalizationVersion = _localizationVersions[sessionStart.ResourceProposal.ClientActiveLanguage];
                if (sessionStart.ResourceProposal.ClientLocalizationVersion != correctLocalizationVersion)
                {
                    correction.LanguageUpdate = new SessionProtocol.SessionResourceCorrection.LanguageUpdateInfo(
                        activeLanguage:         sessionStart.ResourceProposal.ClientActiveLanguage,
                        localizationVersion:    correctLocalizationVersion
                        );
                }
            }

            return correction;
        }

        protected uint ComputeModelChecksum(IModel model)
        {
            using (SegmentedIOBuffer tempBuffer = new SegmentedIOBuffer(segmentSize: 4096))
            {
                return JournalUtil.ComputeChecksum(tempBuffer, model);
            }
        }

        protected void SendToClient(MetaMessage message)
        {
            _transport?.SendToClient(message);
        }

        protected void ExecutePlayerServerAction(PlayerActionBase action)
        {
            // Execute action only on client-side, as state is shared and we don't want to double-execute
            SendToClient(new PlayerExecuteUnsynchronizedServerAction(new MetaSerialized<PlayerActionBase>(action, MetaSerializationFlags.SendOverNetwork, _logicVersion), trackingId: 0));

            OnExecutedPlayerServerAction(action);
        }

        protected virtual void OnExecutedPlayerServerAction(PlayerActionBase action) { }

        protected void EnqueueServerAction(PlayerActionBase action)
        {
            MetaSerialized<PlayerActionBase> serializedAction = new MetaSerialized<PlayerActionBase>(action, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
            SendToClient(new PlayerEnqueueSynchronizedServerAction(serializedAction, trackingId: 123));
        }

        protected void PostponeAction(Action reaction)
        {
            _postponedActions.Enqueue(new JournalPositionedAction(_playerJournal.StagedPosition, reaction));
        }

        void FlushPostponedActions(JournalPosition untilPosition)
        {
            while (_postponedActions.Count > 0 && _postponedActions.Peek().Position < untilPosition)
            {
                JournalPositionedAction action = _postponedActions.Dequeue();
                action.Invoke();
            }
        }

        #region Entity management

        /// <summary>
        /// Server-side state of an entity and its channel.
        /// </summary>
        public class ChannelEntity
        {
            public int                  Channel;
            public IMultiplayerModel    Model;
            public int                  Operation;
            public ClientSlot           Slot;
            public EntityId?            MemberId;

            public MetaDuration         TickUpdateInterval = MetaDuration.FromSeconds(5);
            public MetaTime             LastTickAt;

            public ChannelEntity(int channel, IMultiplayerModel model, int operation, ClientSlot slot, EntityId? memberId)
            {
                Channel = channel;
                Model = model;
                Operation = operation;
                Slot = slot;
                MemberId = memberId;
            }
        }

        int _nextChannelId = 1;
        OrderedDictionary<int, ChannelEntity> _channelEntities = new OrderedDictionary<int, ChannelEntity>();

        /// <summary>
        /// Constructs a new <see cref="IMultiplayerModel"/> and initializes its default fields.
        /// </summary>
        protected T CreateMultiplayerModel<T>(EntityKind kind) where T : IMultiplayerModel, new()
        {
            T model = new T();
            model.EntityId          = EntityId.CreateRandom(kind);
            model.CreatedAt         = MetaTime.Now;
            model.LogicVersion      = _logicVersion;
            model.GameConfig        = PlayerModel.GameConfig;
            model.Log               = CreateLogForEntity(model);
            model.ResetTime(MetaTime.Now);
            return model;
        }

        /// <summary>
        /// Creates the default log channel for an entity model.
        /// </summary>
        protected virtual LogChannel CreateLogForEntity(IModel model)
        {
            return MetaplaySDK.Logs.CreateChannel(name: model.GetType().Name);
        }

        /// <summary>
        /// Adds a new <see cref="IMultiplayerModel"/> fake-entity into the session with a new channel. Returns the <see cref="ChannelEntity"/> handle
        /// which may be used to configure fake-entity behavior.
        /// </summary>
        protected ChannelEntity AddNewMultiplayerEntity(ClientSlot slot, IMultiplayerModel model, EntityId? memberId = null)
        {
            (EntityInitialState initialState, ChannelEntity channelEntity) = CreateMultiplayerEntityInitialState(slot, model, memberId);

            SendToClient(new EntitySwitchedMessage(oldChannelId: -1, newState: initialState));

            return channelEntity;
        }

        (EntityInitialState, ChannelEntity) CreateMultiplayerEntityInitialState(ClientSlot slot, IMultiplayerModel model, EntityId? memberId = null)
        {
            MetaSerialized<IMultiplayerModel> publicData = new MetaSerialized<IMultiplayerModel>(model, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            MetaSerialized<MultiplayerMemberPrivateStateBase> memberData  = default;
            if (memberId != null)
            {
                MultiplayerMemberPrivateStateBase memberDataState = model.GetMemberPrivateState(memberId.Value);
                if (memberDataState != null)
                    memberData = new MetaSerialized<MultiplayerMemberPrivateStateBase>(memberDataState, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            }

            int operation = 1;
            int channelId = _nextChannelId++;
            EntitySerializedState state = new EntitySerializedState(
                publicState:                publicData,
                memberPrivateState:         memberData,
                currentOperation:           operation,
                logicVersion:               _logicVersion,
                sharedGameConfigVersion:    GameConfigArchive.Version,
                sharedConfigPatchesVersion: ContentHash.None,
                activeExperiments:          Array.Empty<EntityActiveExperiment>());
            EntityInitialState initialState = new EntityInitialState(
                state:          state,
                channelId:      channelId,
                contextData:    CreateChannelContextData(slot, memberId));

            ChannelEntity channelEntity = new ChannelEntity(initialState.ChannelId, model, initialState.State.CurrentOperation, slot, memberId);
            _channelEntities.Add(initialState.ChannelId, channelEntity);

            return (initialState, channelEntity);
        }

        ChannelContextDataBase CreateChannelContextData(ClientSlot slot, EntityId? memberId)
        {
            return new ChannelContextDataBase.Default(slot);
        }

        /// <summary>
        /// Removes existing entity from the session.
        /// </summary>
        protected void RemoveMultiplayerEntity(ChannelEntity channelEntity)
        {
            if (!_channelEntities.TryGetValue(channelEntity.Channel, out ChannelEntity trackedEntity))
                throw new InvalidOperationException("invalid channel entity removal attempt. No such channel.");
            if (!ReferenceEquals(channelEntity, trackedEntity))
                throw new InvalidOperationException("invalid channel entity removal attempt. Channel overlap.");

            _channelEntities.Remove(channelEntity.Channel);
            SendToClient(new EntitySwitchedMessage(oldChannelId: channelEntity.Channel, newState: null));
        }

        /// <summary>
        /// Retrieves the existing entity with the given slot, or <c>null</c> if no such entity is active in the session.
        /// </summary>
        protected ChannelEntity TryGetEntityBySlot(ClientSlot slot)
        {
            foreach (ChannelEntity channelEntity in _channelEntities.Values)
            {
                if (channelEntity.Slot == slot)
                    return channelEntity;
            }
            return null;
        }

        /// <summary>
        /// Called on each Update call. Updates the logic of an entity. By default, runs entity Ticks periodically.
        /// </summary>
        protected virtual void UpdateChannelEntity(ChannelEntity manager)
        {
            if (MetaTime.Now - manager.LastTickAt > manager.TickUpdateInterval)
                UpdateEntityTicks(manager);
        }

        /// <summary>
        /// Handler for client-originating channel messages. Default implementation handles builtin messages.
        /// </summary>
        protected virtual void HandleEntityMessage(ChannelEntity manager, MetaMessage message)
        {
            // Handle actions by default
            if (message is EntityEnqueueActionsRequest enqueueRequest)
            {
                UpdateEntityTicks(manager);
                foreach (ModelAction action in enqueueRequest.Actions)
                    ExecuteEntityAction(manager, action);
                return;
            }

            _log.Warning("Unhandled entity message {Message}", PrettyPrint.Compact(message));
        }

        /// <summary>
        /// Called when entity becomes active on client side. This is not called for entities that were already
        /// active during session start.
        /// </summary>
        protected virtual void HandleEntityActivatedOnClient(ChannelEntity manager)
        {
        }

        EntityTimelineUpdateMessage TryGetEntityTickUpdate(ChannelEntity manager)
        {
            MetaTime now            = MetaTime.Now;
            long lastTotalTicks     = manager.Model.CurrentTick;
            long currentTotalTicks  = ModelUtil.TotalNumTicksElapsedAt(now, manager.Model.TimeAtFirstTick, manager.Model.TicksPerSecond);
            long newTicks           = currentTotalTicks - lastTotalTicks;

            manager.LastTickAt = now;

            if (newTicks == 0)
                return null;

            long maxNumPendingTicks = ModelUtil.FloorTicksPerDuration(MetaDuration.FromSeconds(10), manager.Model.TicksPerSecond);
            if (newTicks > maxNumPendingTicks)
            {
                newTicks = maxNumPendingTicks;
                manager.Model.Log.Warning("Model is late too many ticks: {NumTicks}. Executing only 10 seconds worth this frame.", newTicks);
            }

            List<ModelAction>   operations  = new List<ModelAction>();
            int                 firstTick   = manager.Model.CurrentTick + 1;

            manager.Operation = 1;

            for (int tick = 0; tick < newTicks; tick++)
            {
                ModelUtil.RunTick(manager.Model, NullChecksumEvaluator.Context);
                operations.Add(null);
            }

            uint finalChecksum = ComputeModelChecksum(manager.Model);
            return new EntityTimelineUpdateMessage(
                operations:     operations,
                finalChecksum:  finalChecksum,
                debugChecksums: null);
        }

        /// <summary>
        /// Executes any pending Ticks for an entity and informs client.
        /// </summary>
        protected void UpdateEntityTicks(ChannelEntity manager)
        {
            EntityTimelineUpdateMessage update = TryGetEntityTickUpdate(manager);
            if (update != null)
                SendEntityMessage(manager, update);
        }

        /// <summary>
        /// Executes an action on an Entity and informs client.
        /// </summary>
        protected void ExecuteEntityAction(ChannelEntity manager, ModelAction action)
        {
            List<ModelAction>   operations      = new List<ModelAction>();
            int                 firstOperation  = manager.Operation;

            MetaActionResult result = ModelUtil.RunAction(manager.Model, action, NullChecksumEvaluator.Context);
            if (result == MetaActionResult.Success)
            {
                operations.Add(action);
                manager.Operation++;
            }
            else
                manager.Model.Log.Warning("Failed to execute action {Action}: {Result}", PrettyPrint.Compact(action), result);

            uint finalChecksum = ComputeModelChecksum(manager.Model);
            EntityTimelineUpdateMessage update = new EntityTimelineUpdateMessage(
                operations:     operations,
                finalChecksum:  finalChecksum,
                debugChecksums: null);
            SendEntityMessage(manager, update);
        }

        /// <summary>
        /// Sends a message to client in an Entity's message channel.
        /// </summary>
        protected void SendEntityMessage(ChannelEntity manager, MetaMessage message)
        {
            SendToClient(new EntityServerToClientEnvelope(manager.Channel, new MetaSerialized<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, _logicVersion)));
        }

        #endregion

        #region IPlayerModelServerListenerCore

        void IPlayerModelServerListenerCore.OnPlayerNameChanged(string newName)
        {
        }

        void IPlayerModelServerListenerCore.LanguageChanged(LanguageInfo newLanguage, ContentHash languageVersion)
        {
        }

        void IPlayerModelServerListenerCore.DynamicInAppPurchaseRequested(InAppProductId productId)
        {
            PostponeAction(() =>
            {
                ExecutePlayerServerAction(new PlayerConfirmPendingDynamicPurchaseContent(productId));
            });
        }

        void IPlayerModelServerListenerCore.StaticInAppPurchaseContextRequested(InAppProductId productId)
        {
            PostponeAction(() =>
            {
                ExecutePlayerServerAction(new PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext(productId));
            });
        }

        void IPlayerModelServerListenerCore.InAppPurchased(InAppPurchaseEvent ev, InAppProductInfoBase productInfo)
        {
            PostponeAction(() =>
            {
                // In offline mode, validation always succeeds

                SubscriptionQueryResult subscription;
                if (productInfo.Type == InAppProductType.Subscription)
                {
                    MetaTime currentTime = MetaTime.Now;

                    subscription = new SubscriptionQueryResult(
                        stateQueriedAt: currentTime,
                        new SubscriptionInstanceState(
                            isAcquiredViaFamilySharing: false,
                            startTime:                  currentTime,
                            expirationTime:             currentTime + MetaDuration.FromMinutes(10),
                            renewalStatus:              SubscriptionRenewalStatus.NotExpectedToRenew,
                            numPeriods:                 1));
                }
                else
                    subscription = null;

                ExecutePlayerServerAction(new PlayerInAppPurchaseValidated(
                    transactionId: ev.TransactionId,
                    status: InAppPurchaseStatus.ValidReceipt,
                    isDuplicateTransaction: false,
                    orderId: ev.OrderId,
                    originalTransactionId: ev.TransactionId,
                    subscription: subscription,
                    paymentType: null));
            });
        }

        void IPlayerModelServerListenerCore.FirebaseMessagingTokenAdded(string token)
        {
        }

#if !METAPLAY_DISABLE_GUILDS
        void IPlayerModelServerListenerCore.GuildMemberPlayerDataChanged()
        {
        }
#endif

        void IPlayerModelServerListenerCore.ActivableActivationStarted(MetaActivableKey activableKey)
        {
        }

        void IPlayerModelServerListenerCore.ActivableConsumed(MetaActivableKey activableKey)
        {
        }

        void IPlayerModelServerListenerCore.ActivableFinalized(MetaActivableKey activableKey)
        {
        }

        void IPlayerModelServerListenerCore.MetaOfferActivationStarted(MetaOfferGroupId groupId, MetaOfferId offerId)
        {
        }

        void IPlayerModelServerListenerCore.MetaOfferPurchased(MetaOfferGroupId groupId, MetaOfferId offerId)
        {
        }

        void IPlayerModelServerListenerCore.OnPlayerBannedStatusChanged(bool isBanned)
        {
        }

        void IPlayerModelServerListenerCore.AuthMethodAttached(AuthenticationKey authKey)
        {
        }

        void IPlayerModelServerListenerCore.AuthMethodDetached(AuthenticationKey authKey)
        {
        }

        #endregion

    }
}
