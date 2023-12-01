// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;
using System.Globalization;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Cloud.Entity
{
    [MetaMessage(MessageCodesCore.EntityEnsureOnLatestSchemaVersionRequest, MessageDirection.ServerInternal)]
    public class EntityEnsureOnLatestSchemaVersionRequest : MetaMessage
    {
    }

    [MetaMessage(MessageCodesCore.EntityEnsureOnLatestSchemaVersionResponse, MessageDirection.ServerInternal)]
    public class EntityEnsureOnLatestSchemaVersionResponse : MetaMessage
    {
        public int CurrentSchemaVersion;

        EntityEnsureOnLatestSchemaVersionResponse(){ }
        public EntityEnsureOnLatestSchemaVersionResponse(int currentSchemaVersion)
        {
            CurrentSchemaVersion = currentSchemaVersion;
        }
    }

    /// <summary>
    /// Request to "refresh" the entity: wake up, persist, and reply, but do nothing else special.
    /// The purpose is to do any cleanup that gets automatically done at wake-up and persist.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntityRefreshRequest, MessageDirection.ServerInternal)]
    public class EntityRefreshRequest : MetaMessage
    {
    }

    [MetaMessage(MessageCodesCore.EntityRefreshResponse, MessageDirection.ServerInternal)]
    public class EntityRefreshResponse : MetaMessage
    {
    }

    /// <summary>
    /// Represents an entity which can be persisted into the database when not active.
    /// </summary>
    /// <typeparam name="TPersisted">Type of database-persisted representation of entity</typeparam>
    /// <typeparam name="TPersistedPayload">Type of the <see cref="TPersisted.Payload" /></typeparam>
    public abstract class PersistedEntityActor<TPersisted, TPersistedPayload> : EntityActor
        where TPersisted : IPersistedEntity
        where TPersistedPayload : class, ISchemaMigratable
    {
        class TickSnapshot { public static readonly TickSnapshot Instance = new TickSnapshot(); }

        class ScheduledPersistState
        {
            public readonly int RequestId;
            public ScheduledPersistState(int requestId) { RequestId = requestId; }
        }

        static readonly Prometheus.Counter  c_nonFinalEntityRestored        = Prometheus.Metrics.CreateCounter("game_entity_non_final_entity_restored_total", "Cumulative number of entities restored from database that didn't have final persist done", "entity");
        static readonly Prometheus.Counter  c_entitySchemaMigrated          = Prometheus.Metrics.CreateCounter("meta_entity_schema_migrated_total", "Number of times entities of specific kind have been migrated between two versions", new string[] { "entity", "fromVersion", "toVersion" });
        static readonly Prometheus.Counter  c_entitySchemaMigrationFailed   = Prometheus.Metrics.CreateCounter("meta_entity_schema_migration_failed_total", "Number of times entities an entity migration has failed for a specific entity kind and fromVersion", new string[] { "entity", "fromVersion" });

        protected readonly PersistedEntityConfig _entityConfig;
        DateTime                                _lastPersistedAt;

        protected static readonly TimeSpan  TickSnapshotInterval    = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Interval between periodic snapshots of the Entity.
        /// </summary>
        protected abstract TimeSpan         SnapshotInterval        { get; }

        bool        _hasPendingScheduledPersist         = false;
        int         _pendingScheduledPersistRunningId   = 0;
        DateTime    _earliestScheduledPersistAt;

        /// <summary>
        /// Minimum interval between persists due to <see cref="SchedulePersistState"/>.
        /// </summary>
        protected virtual TimeSpan MinScheduledPersistStateInterval => TimeSpan.FromSeconds(10);

        protected PersistedEntityActor(EntityId entityId) : base(entityId)
        {
            // Add noise to _lastPersistedAt to smooth out persisting (even if lots of actors woken up quickly)
            _lastPersistedAt = DateTime.UtcNow + (new Random().NextDouble() - 0.5) * SnapshotInterval;

            // Initially, allow scheduling persist to happen as soon as desired.
            _earliestScheduledPersistAt = DateTime.UtcNow;

            // Start snapshotting timer
            StartRandomizedPeriodicTimer(TickSnapshotInterval, TickSnapshot.Instance);

            _entityConfig = (PersistedEntityConfig)EntityConfigRegistry.Instance.GetConfigForEntityActor(GetType());
        }

        protected async Task InitializePersisted(TPersisted persisted)
        {
            // Restore from persisted (if exists), or initialize new entity
            if (persisted != null && persisted.Payload != null)
            {
                // Restore from snapshot and validate
                _log.Debug("Restoring from snapshot (persistedAt={PersistedAt}, schemaVersion={SchemaVersion}, isFinal={IsFinal}, size={NumBytes})", persisted.PersistedAt, persisted.SchemaVersion, persisted.IsFinal, persisted.Payload.Length);

                // Check if persisted schema version is still supported
                SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator<TPersistedPayload>();
                int oldestSupportedVersion = migrator.SupportedSchemaVersions.MinVersion;
                if (persisted.SchemaVersion < oldestSupportedVersion)
                {
                    _log.Warning("Schema version {PersistedSchemaVersion} is too old (oldest supported is {OldestSupportedSchemaVersion}), resetting state!", persisted.SchemaVersion, oldestSupportedVersion);

                    // \todo [petri] throw or take backup in prod build?

                    // Initialize new entity
                    TPersistedPayload payload = await InitializeNew();

                    // PostLoad()
                    await PostLoad(payload, DateTime.UtcNow, TimeSpan.Zero);
                }
                else
                {
                    // Log if final persist was not made (ie, entity likely has lost some state)
                    if (!persisted.IsFinal)
                    {
                        c_nonFinalEntityRestored.WithLabels(_entityId.Kind.ToString()).Inc();
                        _log.Info("Restoring from non-final snapshot!");
                    }

                    // Restore state from persisted
                    TPersistedPayload payload;
                    try
                    {
                        payload = await RestoreFromPersisted(persisted);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Failed to deserialize {TypeName}: {Exception}", typeof(TPersisted).ToGenericTypeString(), ex);
                        _log.Error("Serialized {Type} ({NumBytes} bytes): {Payload}.", typeof(TPersisted), persisted.Payload.Length, Convert.ToBase64String(persisted.Payload));
                        _log.Error("Tagged structure: {Data}", TaggedWireSerializer.ToString(persisted.Payload));
                        throw;
                    }

                    // Migrate state to latest supported version
                    MigrateState(payload, persisted.SchemaVersion);

                    // PostLoad()
                    await PostLoad(payload, persisted.PersistedAt, DateTime.UtcNow - persisted.PersistedAt);
                }
            }
            else
            {
                // Create new state & immediately persist (an empty placeholder already existed in database)
                TPersistedPayload payload = await InitializeNew();
                await PostLoad(payload, DateTime.UtcNow, TimeSpan.Zero);
                await PersistState(isInitial: true, isFinal: false);
            }
        }

        /// <summary>
        /// Serialize and compress the entity payload (usually model) for persistence in the database.
        /// Also used for entity export/import archives.
        /// </summary>
        /// <typeparam name="TModel">Type of payload or model to persist</typeparam>
        /// <param name="model">Model object to persist</param>
        /// <param name="logicVersion">Logic version of the payload (if used)</param>
        /// <returns></returns>
        protected byte[] SerializeToPersistedPayload<TModel>(TModel model, IGameConfigDataResolver resolver, int? logicVersion)
        {
            // Serialize and compress the payload
            byte[] persisted = _entityConfig.SerializeDatabasePayload(model, logicVersion);

            // Perform extra validation on the persisted state, if enabled
            ValidatePersistedState<TModel>(persisted, resolver, logicVersion);

            return persisted;
        }

        /// <summary>
        /// Deserialize and decompress the entity payload from the database persisted format.
        /// </summary>
        /// <typeparam name="TModel">Type of the payload (or model) to use</typeparam>
        /// <param name="persisted">Persisted bytes (serialized and compressed)</param>
        /// <param name="resolver">Game config data resolver</param>
        /// <param name="logicVersion">Logic version to use while deserializing</param>
        /// <returns></returns>
        protected TModel DeserializePersistedPayload<TModel>(byte[] persisted, IGameConfigDataResolver resolver, int? logicVersion)
        {
            return _entityConfig.DeserializeDatabasePayload<TModel>(persisted, resolver, logicVersion);
        }

        /// <summary>
        /// Validate that a persisted entity payload can be decompressed (if compressed) and deserialized
        /// as the corresponding <typeparamref name="TModel"/> type. The operation is only performed if
        /// <see cref="EnvironmentOptions.ExtraPersistenceChecks"/> is true.
        /// </summary>
        /// <typeparam name="TModel"></typeparam>
        /// <param name="persisted"></param>
        /// <param name="resolver"></param>
        /// <param name="logicVersion"></param>
        protected void ValidatePersistedState<TModel>(byte[] persisted, IGameConfigDataResolver resolver, int? logicVersion)
        {
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            if (envOpts.ExtraPersistenceChecks)
            {
                // Decompress payload (if compressed)
                using FlatIOBuffer uncompressed =
                    BlobCompress.IsCompressed(persisted) ? BlobCompress.DecompressBlob(persisted) : FlatIOBuffer.CopyFromSpan(persisted);

                try
                {
                    // Check that serialized state can be skipped over
                    using (IOReader reader = new IOReader(uncompressed))
                        TaggedWireSerializer.TestSkip(reader);

                    // Check that serialized state can be deserialized
                    using (IOReader reader = new IOReader(uncompressed))
                        _ = MetaSerialization.DeserializeTagged<TModel>(reader, MetaSerializationFlags.Persisted, resolver, logicVersion);
                }
                catch (Exception ex)
                {
                    // If deserialization fails, report error and immediately exit, so persisted state doesn't get corrupted!
                    _log.Error("Failed to validate persisted {TypeName}!\nPersisted ({NumBytes} bytes): {Payload}.\nException: {Exception}", typeof(TModel).Name, persisted.Length, Convert.ToBase64String(persisted), ex);
                    using IOReader reader = new IOReader(uncompressed);
                    _log.Error("Failing persisted {TModel} data: {Data}", nameof(TModel), TaggedWireSerializer.ToString(reader));
                    throw;
                }
            }
        }

        protected override void PostStop()
        {
            base.PostStop();
        }

        protected override async Task OnShutdown()
        {
            // Do final snapshot
            // \todo [petri] handle transient persist failures (cancel shutdown)
            _log.Debug("Entity shutdown, do final snapshot now");
            await PersistState(isInitial: false, isFinal: true);

            await base.OnShutdown();
        }

        protected override void RegisterHandlers()
        {
            ReceiveAsync<TickSnapshot>(ReceiveTickSnapshot);

            base.RegisterHandlers();
        }

        async Task ReceiveTickSnapshot(TickSnapshot _)
        {
            // Periodic persisting
            if (IsShutdownEnqueued)
                return;
            if (DateTime.UtcNow < _lastPersistedAt + SnapshotInterval)
                return;
            await PersistStateIntermediate();
        }

        protected Task PersistStateIntermediate()
        {
            return PersistState(isInitial: false, isFinal: false);
        }

        protected async Task PersistState(bool isInitial, bool isFinal)
        {
            await PersistStateImpl(isInitial: isInitial, isFinal: isFinal);

            _lastPersistedAt = DateTime.UtcNow;

            _hasPendingScheduledPersist = false;
            _earliestScheduledPersistAt = DateTime.UtcNow + MinScheduledPersistStateInterval;
        }

        /// <summary>
        /// Schedule <see cref="PersistStateIntermediate"/> to be executed
        /// sometime in the near future (unless already scheduled).
        /// The scheduled persists are rate-limited by
        /// <see cref="MinScheduledPersistStateInterval"/>.
        /// </summary>
        /// <remarks>
        /// This is for non-critical persists. Critically important persists
        /// should use <see cref="PersistStateIntermediate"/> directly.
        /// </remarks>
        protected void SchedulePersistState()
        {
            if (_hasPendingScheduledPersist)
                return;

            _hasPendingScheduledPersist = true;
            _pendingScheduledPersistRunningId++;

            Context.System.Scheduler.ScheduleTellOnce(
                delay: Util.Max(TimeSpan.Zero, _earliestScheduledPersistAt - DateTime.UtcNow),
                receiver: _self,
                message: new ScheduledPersistState(_pendingScheduledPersistRunningId),
                sender: _self);
        }

        [CommandHandler]
        async Task HandleScheduledPersistState(ScheduledPersistState scheduledPersistState)
        {
            if (!_hasPendingScheduledPersist)
                return;
            // Ignore stale messages
            if (scheduledPersistState.RequestId != _pendingScheduledPersistRunningId)
                return;

            await PersistStateIntermediate();
        }

        [EntityAskHandler]
        async Task HandleEntityEnsureOnLatestSchemaVersionRequest(EntityAsk ask, EntityEnsureOnLatestSchemaVersionRequest _)
        {
            // Since we're successfully up and receiving messages/asks, we must be on the latest schema version.
            // Just make that persistent, and reply OK.
            // \note This is pretty much the same as EntityRefreshRequest, except for the reply.
            //       The schema version in the reply isn't really necessary since it's a global constant,
            //       so we could really get by with just EntityRefreshRequest.
            //       But I guess this is a bit more explicit, so whatever.
            await PersistStateIntermediate();
            ReplyToAsk(ask, new EntityEnsureOnLatestSchemaVersionResponse(_entityConfig.CurrentSchemaVersion));

            // Probably a one-off request - request shutdown right away, if appropriate.
            // \note Please see comments in TryRequestShutdownAfterLikelyOneOffRequest.
            TryRequestShutdownAfterLikelyOneOffRequest();
        }

        [EntityAskHandler]
        async Task HandleEntityRefreshRequest(EntityAsk ask, EntityRefreshRequest _)
        {
            await PersistStateIntermediate();
            ReplyToAsk(ask, new EntityRefreshResponse());

            // Probably a one-off request - request shutdown right away, if appropriate.
            // \note Please see comments in TryRequestShutdownAfterLikelyOneOffRequest.
            TryRequestShutdownAfterLikelyOneOffRequest();
        }

        /// <summary>
        /// Request shutdown if we don't currently have subscribers, and our shutdown policy is
        /// to shut down when we don't have subscribers.
        ///
        /// Caveat, kludge:
        ///
        /// Normally, under such circumstances, the entity would automatically shut down after some timeout.
        /// This method is intended to be used to omit that timeout, and instead shut down immediately;
        /// this is used in use cases where lots of entities are woken up at a fast rate for some one-off
        /// operation, and it's desirable to stop them from lingering unnecessarily.
        ///
        /// Note that this is a kludge; a better solution would be (roughly speaking) for EntityActor to
        /// automatically understand that it was woken up not for a subscription, but for an one-off operation,
        /// and that it can shut down after the operation.
        ///
        /// Note also that using this method has a potential race with incoming subscriptions:
        /// a subscription can come in as we're shutting down, and the subscription will get terminated
        /// soon after it started. This method should thus be only used in cases where that is not
        /// expected to be a significant problem.
        /// </summary>
        void TryRequestShutdownAfterLikelyOneOffRequest()
        {
            if (ShutdownPolicy.mode == AutoShutdownState.Modes.NoSubscribers && _subscribers.Count == 0)
                RequestShutdown();
        }

        /// <summary>
        /// Called from <see cref="InitializePersisted"/>. Returns a payload for a new entity which will be delivered
        /// to <see cref="PostLoad"/>. Called when the entity is created for the first time, or if the persisted schema
        /// version is older than the last supported (<see cref="OldestSupportedSchemaVersion"/>).
        /// </summary>
        protected abstract Task<TPersistedPayload> InitializeNew();

        /// <summary>
        /// Called from <see cref="InitializePersisted"/>. Returns a payload for a new entity which will be delivered
        /// to <see cref="MigrateState"/> for each required migration if any, and finally to <see cref="PostLoad"/>. Called
        /// when entity is created and there exists a persited data for it (and the persisted data schema is not older than
        /// <see cref="OldestSupportedSchemaVersion"/>).
        /// </summary>
        protected abstract Task<TPersistedPayload> RestoreFromPersisted(TPersisted persisted);

        /// <summary>
        /// Called from <see cref="InitializePersisted"/>. Migrates payload in-place from <see cref="fromVersion"/> to the
        /// next version. Will be called repeatedly until <see cref="CurrentSchemaVersion"/> is reached. Returns true on success.
        /// </summary>
        protected void MigrateState(TPersistedPayload payload, int fromVersion)
        {
            string migratableType = typeof(TPersistedPayload).ToGenericTypeString();
            SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator<TPersistedPayload>();
            int toVersion = migrator.SupportedSchemaVersions.MaxVersion;

            if (fromVersion == toVersion)
                return; // already up-to-date
            else if (fromVersion > toVersion)
                throw new InvalidOperationException($"Migratable type {payload.GetType().ToGenericTypeString()} is in a future schema v{fromVersion} when maximum supported is v{toVersion}");
            else
            {
                _log.Info("Migrating from schema v{OldSchemaVersion} to v{NewSchemaVersion}", fromVersion, toVersion);
                try
                {
                    OnBeforeSchemaMigration(payload, fromVersion, toVersion);
                    migrator.RunMigrations(payload, fromVersion);
                    c_entitySchemaMigrated.WithLabels(migratableType, fromVersion.ToString(CultureInfo.InvariantCulture), toVersion.ToString(CultureInfo.InvariantCulture)).Inc();
                    OnSchemaMigrated(payload, fromVersion, toVersion);
                }
                catch (SchemaMigrationError ex)
                {
                    c_entitySchemaMigrationFailed.WithLabels(migratableType, ex.FromVersion.ToString(CultureInfo.InvariantCulture)).Inc();
                    _log.Error("Migration failure from schema v{FromVersion} to v{ToVersion}: {Exception}", ex.FromVersion, ex.FromVersion+1, ex.InnerException);
                    throw;
                }
            }
        }
        
        /// <summary>
        /// Called before the <typeparamref name="TPersistedPayload"/> schema version is migrated.
        /// </summary>
        protected virtual void OnBeforeSchemaMigration(TPersistedPayload payload, int fromVersion, int toVersion) { }

        /// <summary>
        /// Called when the <typeparamref name="TPersistedPayload"/> schema version has been migrated.
        /// </summary>
        /// <param name="fromVersion">Schema version before the migration</param>
        /// <param name="toVersion">Schema version after the migration</param>
        protected virtual void OnSchemaMigrated(TPersistedPayload payload, int fromVersion, int toVersion) { }

        /// <summary>
        /// Called from <see cref="InitializePersisted"/>. Entity should load state from <paramref name="payload"/>.
        /// </summary>
        protected abstract Task PostLoad(TPersistedPayload payload, DateTime persistedAt, TimeSpan elapsedTime);

        protected abstract Task PersistStateImpl(bool isInitial, bool isFinal);
    }
}
