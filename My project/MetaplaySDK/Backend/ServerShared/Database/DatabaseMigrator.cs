// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Dapper;
using Metaplay.Cloud;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.Database
{
    /// <summary>
    /// Database migrator. Ensures that database shards are both up-to-date with latest database schema (see GameDbContext)
    /// and handles re-sharding of databases when shards are added or removed.
    /// </summary>
    public class DatabaseMigrator
    {
        IMetaLogger         _log        = MetaLogger.ForContext<DatabaseMigrator>();
        MetaDatabaseBase    _db         = MetaDatabaseBase.Get(QueryPriority.Normal);
        DatabaseBackend     _backend    = DatabaseBackend.Instance;

        public DatabaseMigrator()
        {
        }

        #region Schema migration

        public List<string> GetAllTableNames()
        {
            // \note Returns the tables that are in the current GameDbContext, but won't know of any tables outside of it!
            using (MetaDbContext context = _backend.CreateContext(DatabaseReplica.ReadWrite, shardNdx: 0))
            {
                return
                    new List<string> { "__EFMigrationsHistory" }
                    .Concat(context.Model.GetEntityTypes().Select(entityType => entityType.GetTableName()))
                    .ToList();
            }
        }

        async Task ResetShardAsync(int shardNdx)
        {
            List<string> tableNames = GetAllTableNames();

            await _backend.ExecuteRawAsync(DatabaseReplica.ReadWrite, shardNdx, async conn =>
            {
                // \note drop tables one at a time, because SQLite doesn't support multiple tables in one command
                foreach (string tableName in tableNames)
                    await conn.ExecuteAsync($"DROP TABLE IF EXISTS {tableName}").ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        async Task ResetAllShardsAsync()
        {
            await Task.WhenAll(
                Enumerable.Range(0, _db.NumActiveShards)
                .Select(async shardNdx => await ResetShardAsync(shardNdx).ConfigureAwait(false)))
                .ConfigureAwait(false);
        }

        async Task MigrateShardAsync(int shardNdx)
        {
            // For MySql, ensure database uses utf8mb4 and binary comparisons, so EntityId etc. columns use case-sensitive comparisons.
            // MySql defaults to case-insensitive comparison, which causes Player profiles to conflict where id only differs by case.
            // \todo [petri] gets applied on every start (even if no migration is needed)
            if (_backend.BackendType == DatabaseBackendType.MySql)
            {
                await _backend.ExecuteRawAsync(DatabaseReplica.ReadWrite, shardNdx, conn =>
                {
                    return conn.ExecuteAsync("alter database character set utf8mb4 collate utf8mb4_bin");
                }).ConfigureAwait(false);
            }

            // Perform the migration for all database shards
            await _backend.ExecuteShardedDbContextAsync(DatabaseReplica.ReadWrite, shardNdx, async context =>
            {
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    // Allow lots of time (migration can take a long time for large databases)
                    TimeSpan MigrationTimeout = TimeSpan.FromHours(1);
                    _log.Information("Start migration of database shard #{ShardNdx} (timeout={MigrationTimeout})..", shardNdx, MigrationTimeout);
                    context.Database.SetCommandTimeout(MigrationTimeout);

                    // Start task that prints time progression periodically
                    // \todo [petri] extract this into its own class
                    Task progressLogTask = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                await Task.Delay(10_000, cts.Token).ConfigureAwait(false);
                                if (cts.IsCancellationRequested)
                                    break;
                                _log.Information("Migrating database shard #{ShardNdx}: {TimeElapsed} elapsed", shardNdx, sw.Elapsed);
                            }
                            catch (TaskCanceledException)
                            {
                                break;
                            }
                        }
                    });

                    // \note MySQL doesn't support rollbacks for DDL operations (like modifying tables), so even though underlying
                    //       migrations use transactions, they don't actually do much at all.
                    try
                    {
                        string[] appliedMigrations = (await context.Database.GetAppliedMigrationsAsync().ConfigureAwait(false)).ToArray();
                        string[] knownMigrations = context.Database.GetMigrations().ToArray();

                        // Log what we are about to migrate.
                        string[] pendingMigrations = knownMigrations.Except(appliedMigrations).ToArray();
                        if (pendingMigrations.Length > 0)
                            _log.Information("Migration of database shard #{ShardNdx} has {NumPending} pending migration(s): [{Pending}]", shardNdx, pendingMigrations.Length, string.Join(", ", pendingMigrations));

                        // Log if there are migrations applied that we don't know of.
                        string[] unknownMigrations = appliedMigrations.Except(knownMigrations).ToArray();
                        if (unknownMigrations.Length > 0)
                            _log.Warning("Database shard #{ShardNdx} has {NumUnknown} unknown migration(s): [{Unknown}]", shardNdx, unknownMigrations.Length, string.Join(", ", unknownMigrations));

                        // _database.Execute migration steps
                        await context.Database.MigrateAsync().ConfigureAwait(false);

                        _log.Information("Migration of database shard #{ShardNdx} completed ({MigrationElapsed}s elapsed)", shardNdx, sw.Elapsed.TotalSeconds);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Failed to migrate database shard #{ShardNdx}: {Exception}", shardNdx, ex);
                        throw;
                    }
                    finally
                    {
                        cts.Cancel();
                        await progressLogTask.ConfigureAwait(false);
                        progressLogTask.Dispose();
                    }
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Migrate the given shard range to latest database schema. Performs migration in parallel.
        /// </summary>
        /// <param name="log"></param>
        /// <param name="startNdx">Start index of shards to migrate</param>
        /// <param name="numShards">Number of shards to migrate</param>
        /// <returns></returns>
        public async Task MigrateShardsAsync(int startNdx, int numShards)
        {
            // Migrate all shards to latest schema version (in parallel)
            await Task.WhenAll(
                Enumerable.Range(startNdx, numShards)
                .Select(async shardNdx => await MigrateShardAsync(shardNdx).ConfigureAwait(false)))
                .ConfigureAwait(false);
        }

        #endregion // Schema migration

        #region Re-sharding

        /// <summary>
        /// Read all items from a database table in a streaming fashion, with item batching.
        /// </summary>
        /// <typeparam name="TItem">Type of item to read</typeparam>
        /// <param name="conn">Database connection object</param>
        /// <param name="query">Query to execute</param>
        /// <param name="param">Query parameters object</param>
        /// <param name="batchSize">Number of items to return in each batch</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> of item batches</returns>
        async IAsyncEnumerable<List<TItem>> ScanTableItemsBatched<TItem>(DbConnection conn, string query, object param, int batchSize)
        {
            DbDataReader reader = await conn.ExecuteReaderAsync(query, param, commandTimeout: 3600);
            Func<IDataReader, TItem> parseRow = reader.GetRowParser<TItem>();

            List<TItem> list = new List<TItem>(capacity: batchSize);
            while (await reader.ReadAsync())
            {
                TItem item = parseRow(reader);
                list.Add(item);
                if (list.Count == batchSize)
                {
                    yield return list;
                    list = new List<TItem>(capacity: batchSize);
                }
            }

            // Return last batch
            if (list.Count > 0)
                yield return list;
        }

        /// <summary>
        /// Migrate entities of type <typeparamref name="TItem"/> from shard <paramref name="srcShardNdx"/> to all other shards.
        /// </summary>
        /// <param name="srcShardNdx"></param>
        /// <returns></returns>
        async Task ReshardItemsFromShardAsync<TItem>(int srcShardNdx) where TItem : IPersistedItem
        {
            _log.Information("Re-sharding table {EntityType} items from shard #{SrcShardNdx}", typeof(TItem).Name, srcShardNdx);
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<TItem>();

            string query = $"SELECT * FROM {itemSpec.TableName} WHERE ({_backend.GetShardingSql(itemSpec.PartitionKeyName)} % @NumShards) <> @ShardNdx";
            await _backend.ExecuteRawAsync(DatabaseReplica.ReadWrite, srcShardNdx, async srcConn =>
            {
                IAsyncEnumerable<List<TItem>> batches = ScanTableItemsBatched<TItem>(srcConn, query, new { ShardNdx = srcShardNdx, NumShards = _db.NumActiveShards }, batchSize: 1000);
                await foreach (List<TItem> batch in batches)
                {
                    // Write entities into the correct shard
                    // \todo [petri] would it be safer to overwrite (could help in scenarios where re-sharding into a shard with stale data)?
                    await _db.MultiInsertOrIgnoreAsync(batch).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Reshard all items in all tables from the given source shard.
        /// </summary>
        /// <param name="log"></param>
        /// <param name="srcShardNdx"></param>
        /// <returns></returns>
        async Task ReshardAllTablesFromShardAsync(int srcShardNdx)
        {
            // Reshard all tables sequentially (to limit parallelism somewhat)
            MethodInfo reshardTableMethod = typeof(DatabaseMigrator).GetMethod(nameof(ReshardItemsFromShardAsync), BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (DatabaseItemSpec itemSpec in DatabaseTypeRegistry.ItemSpecs)
            {
                // Only reshard partitioned items
                if (itemSpec.IsPartitioned)
                {
                    _log.Information("Re-sharding table {TableName} items from shard #{SrcShardNdx}", itemSpec.TableName, srcShardNdx);
                    MethodInfo reshardMethod = reshardTableMethod.MakeGenericMethod(itemSpec.ItemType);
                    Task task = (Task)reshardMethod.Invoke(this, new object[] { srcShardNdx });
                    await task.ConfigureAwait(false);
                }
            }
        }

        async Task<bool> AreShardsTableEqualAsync<TItem>(int srcShardNdx, List<int> dstShardNdxs) where TItem : IPersistedItem
        {
            const string    OpName      = "ShardEquality";
            const int       PageSize    = 1000;

            // Only compare partitioned tables (non-partitioned should not be equal)
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<TItem>();
            if (!itemSpec.IsPartitioned)
                throw new InvalidOperationException($"Trying to compare non-partitioned table {itemSpec.TableName} for shard inequality");

            // Initialize iterator keys
            int         numTargets              = dstShardNdxs.Count;
            string      srcIteratorStartKey     = "";
            string[]    dstIteratorStartKeys    = dstShardNdxs.Select(_ => "").ToArray();

            while (true)
            {
                // Query page of source items
                List<TItem> srcItems =
                    (await _db.PagedQueryFullSingleShard<TItem>(
                        opName:                     OpName,
                        shardNdx:                   srcShardNdx,
                        iteratorStartKeyExclusive:  srcIteratorStartKey,
                        pageSize:                   PageSize).ConfigureAwait(false))
                    .ToList();
                int numItems = srcItems.Count;

                // Update iterator
                if (srcItems.Count > 0)
                    srcIteratorStartKey = itemSpec.GetItemPrimaryKey(srcItems.Last());

                // Fetch page of items from each target shard
                TItem[][] dstItems = await Task.WhenAll(
                    Enumerable.Range(0, numTargets)
                    .Select(async ndx => (await _db.PagedQueryFullSingleShard<TItem>(OpName, dstShardNdxs[ndx], dstIteratorStartKeys[ndx], PageSize).ConfigureAwait(false)).ToArray()))
                    .ConfigureAwait(false);

                // Check lengths of each target page and update iterators
                for (int ndx = 0; ndx < numTargets; ndx++)
                {
                    // If count mismatches, shards cannot be equal
                    if (dstItems[ndx].Length != numItems)
                    {
                        _log.Information("Detected mismatched item count in table {TableName} between shards #{SrcShardNdx} and #{DstShardNdx}", typeof(TItem).Name, srcShardNdx, dstShardNdxs[ndx]);
                        return false;
                    }

                    // Update iterator
                    if (dstItems[ndx].Length > 0)
                        dstIteratorStartKeys[ndx] = itemSpec.GetItemPrimaryKey(dstItems[ndx].Last());
                }

                // Compare all items
                // \todo [petri] this only compares primary keys, compare other members as well?
                for (int itemNdx = 0; itemNdx < numItems; itemNdx++)
                {
                    TItem srcItem = srcItems[itemNdx];
                    string srcPrimaryKey = itemSpec.GetItemPrimaryKey(srcItem);

                    for (int ndx = 0; ndx < numTargets; ndx++)
                    {
                        TItem dstItem = dstItems[ndx][itemNdx];
                        string dstPrimaryKey = itemSpec.GetItemPrimaryKey(dstItem);
                        if (dstPrimaryKey != srcPrimaryKey)
                        {
                            _log.Information("Detected mismatched primary key in table {TableName} between shards #{SrcShardNdx} and #{DstShardNdx}", typeof(TItem).Name, srcShardNdx, dstShardNdxs[ndx]);
                            return false;
                        }

                        // If item is an IPersistedEntity, make a more thorough check
                        // \todo [petri] Check more members of non-IPersistedEntity types, too?
                        if (srcItem is IPersistedEntity srcEntity)
                        {
                            IPersistedEntity dstEntity = (IPersistedEntity)dstItem;
                            if (srcEntity.PersistedAt != dstEntity.PersistedAt)
                            {
                                _log.Warning("Detected mismatched PersistedAt in table {TableName} for item {EntityId}: {SrcPersistedAt} in #{SrcShardNdx} vs {DstPersistedAt} in #{DstShardNdx}", typeof(TItem).Name, srcEntity.EntityId, srcEntity.PersistedAt, srcShardNdx, dstEntity.PersistedAt, dstShardNdxs);
                                return false;
                            }

                            if (srcEntity.SchemaVersion != dstEntity.SchemaVersion)
                            {
                                _log.Warning("Detected mismatched SchemaVersion in table {TableName} for item {EntityId}: {SrcPersistedAt} in #{SrcShardNdx} vs {DstPersistedAt} in #{DstShardNdx}", typeof(TItem).Name, srcEntity.EntityId, srcEntity.SchemaVersion, srcShardNdx, dstEntity.SchemaVersion, dstShardNdxs);
                                return false;
                            }

                            if (!Util.ArrayEqual(srcEntity.Payload, dstEntity.Payload))
                            {
                                _log.Warning("Detected mismatched Payload in table {TableName} for item {EntityId}: #{SrcShardNdx} vs #{DstShardNdx}", typeof(TItem).Name, srcEntity.EntityId, srcShardNdx, dstShardNdxs);
                                return false;
                            }
                        }
                    }
                }

                // If no items, all target shards match source!
                if (srcItems.Count == 0)
                    return true;
            }
        }

        async Task<bool> AreShardsEqualAsync(int srcShardNdx, List<int> dstShardNdxs)
        {
            // Compare all tables sequentially (to limit parallelism)
            MethodInfo compareTableMethod = typeof(DatabaseMigrator).GetMethod(nameof(AreShardsTableEqualAsync), BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (DatabaseItemSpec itemSpec in DatabaseTypeRegistry.ItemSpecs)
            {
                // Only check partitioned items (non-partitioned are always on shard #0) with primary key
                // \todo [petri] Skipping the PrimaryKeyless items is not strictly correct, but comparing all the other tables should be safe enough
                if (itemSpec.IsPartitioned && itemSpec.HasPrimaryKey)
                {
                    _log.Information("Comparing table {TableName} shards for equality to shard #{SrcShardNdx}", itemSpec.TableName, srcShardNdx);
                    MethodInfo compareMethod = compareTableMethod.MakeGenericMethod(itemSpec.ItemType);
                    Task<bool> compareTask = (Task<bool>)compareMethod.Invoke(this, new object[] { srcShardNdx, dstShardNdxs });
                    if (!await compareTask.ConfigureAwait(false))
                    {
                        _log.Information("Table {TableName} new shards failed equality comparison to source shard #{SrcShardNdx}", itemSpec.TableName, srcShardNdx);
                        return false;
                    }

                    _log.Information("Table {TableName} new shards are all equal to source shard #{SrcShardNdx}", itemSpec.TableName, srcShardNdx);
                }
            }

            return true;
        }

        /// <summary>
        /// Reshard the database from <paramref name="numOldShards"/> shards to <see cref="_db.NumActiveShards"/>.
        /// The resharding itself is idempotent, so it is safe to restart it after it has terminated incorrectly.
        /// The idempotency is achieved by first copying (without removing) all the items to their correct shards,
        /// and only then pruning (removing) any items that don't belong on other shards.
        /// </summary>
        /// <param name="log"></param>
        /// <param name="numOldShards">Number of previously active shards</param>
        /// <returns></returns>
        async Task ReshardAllEntitiesAsync(int numOldShards)
        {
            // Check if we can use integer multiple up-sharding fast path: new number of shards must be an integer
            // multiple of the old number, and all the new shards must be in-order clones of the original shards.
            bool useIntegerMultipleReshardingFastPath = false;
            if (_db.NumActiveShards > numOldShards && _db.NumActiveShards % numOldShards == 0)
            {
                int multiplier = _db.NumActiveShards / numOldShards;
                _log.Information("Testing whether fast-path for integer multiple re-sharding can be used (with multiplier {Multiplier})", multiplier);

                // Spawn task for each source shard to check whether all the new shards are equal to the corresponding original shards
                // \todo [petri] could optimize by canceling all the tasks when the first one finds a difference
                bool[] areEntitiesEqual = await Task.WhenAll(
                    Enumerable.Range(0, numOldShards)
                    .Select(async srcShardNdx =>
                    {
                        List<int> dstShards = Enumerable.Range(0, multiplier - 1).Select(i => srcShardNdx + (i + 1) * numOldShards).ToList();
                        return await AreShardsEqualAsync(srcShardNdx, dstShards).ConfigureAwait(false);
                    })).ConfigureAwait(false);

                // _database.Execute all the tasks and combine results
                useIntegerMultipleReshardingFastPath = areEntitiesEqual.All(canUseFastPath => canUseFastPath);
                if (useIntegerMultipleReshardingFastPath)
                    _log.Information("Can use integer multiple re-sharding fast path");
            }

            // If fast-path cannot be used, perform full re-sharding
            if (!useIntegerMultipleReshardingFastPath)
            {
                // _database.Execute a migration task for each src-dst shard pair, in parallel
                _log.Information("Performing full re-sharding from all source shards to all other shards (integer multiple fast-path cannot be used)");
                await Task.WhenAll(
                    Enumerable.Range(0, numOldShards)
                    .Select(async srcShardNdx => await ReshardAllTablesFromShardAsync(srcShardNdx).ConfigureAwait(false)))
                    .ConfigureAwait(false);
            }

            // Prune all non-belonging items from all shards, in parallel
            await Task.WhenAll(
                Enumerable.Range(0, _db.NumActiveShards)
                .Select(async shardNdx => await PruneShardPostDuplicateAsync(shardNdx).ConfigureAwait(false)))
                .ConfigureAwait(false);

            // Purge any old shards that are no longer used
            if (numOldShards > _db.NumActiveShards)
            {
                _log.Information("Purging old shards #{StartShardNdx} to #{EndShardNdx}", _db.NumActiveShards, numOldShards - 1);
                await Task.WhenAll(
                    Enumerable.Range(_db.NumActiveShards, numOldShards - _db.NumActiveShards)
                    .Select(async shardNdx => await ResetShardAsync(shardNdx).ConfigureAwait(false)))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Purge all items from all tables on the given shard. Used for emptying shard no longer in use after down-sharding.
        /// This is mostly a precaution to avoid that data leaking back into the system in case of some badly applied re-sharding.
        /// </summary>
        /// <param name="log"></param>
        /// <param name="shardNdx">Index of shard to purge</param>
        /// <returns></returns>
        async Task PurgeShardItemsAsync(int shardNdx)
        {
            _log.Information("Shard #{ShardNdx}: Purging all items from all tables", shardNdx);

            await _backend.ExecuteRawAsync(DatabaseReplica.ReadWrite, shardNdx, async conn =>
            {
                foreach (DatabaseItemSpec item in DatabaseTypeRegistry.ItemSpecs)
                {
                    // \todo [petri] use TRUNCATE for MySql, it's faster
                    await conn.ExecuteAsync($"DELETE FROM {item.TableName}").ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        async Task PruneShardPostDuplicateAsync(int shardNdx)
        {
            // Prune all item types
            // \note Doing this sequentially (instead of in parallel). The assumption is that the database would perform better as it can focus on smaller dataset at a time.
            foreach (DatabaseItemSpec item in DatabaseTypeRegistry.ItemSpecs)
            {
                if (item.IsPartitioned)
                {
                    _log.Information("Shard #{ShardNdx} table {TableName}: Pruning partitioned items that don't belong on this shard", shardNdx, item.TableName);
                    await _backend.ExecuteRawAsync(DatabaseReplica.ReadWrite, shardNdx, async conn =>
                    {
                        int numBefore = await conn.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {item.TableName}", commandTimeout: 3600).ConfigureAwait(false);
                        int numRemoved = await conn.ExecuteAsync($"DELETE FROM {item.TableName} WHERE ({_backend.GetShardingSql(item.PartitionKeyName)} % @NumShards) <> @ShardNdx", new { ShardNdx = shardNdx, NumShards = _db.NumActiveShards }, commandTimeout: 3600).ConfigureAwait(false);
                        _log.Information("Shard #{0} table {1}: {2} items removed of {3} total, {4} remaining", shardNdx, item.TableName, numRemoved, numBefore, numBefore - numRemoved);
                    }).ConfigureAwait(false);
                }
                else
                {
                    // For non-partitioned tables, only keep items in shard 0, drop all items from other shards
                    if (shardNdx != 0)
                    {
                        _log.Information("Shard #{ShardNdx} table {TableName}: Pruning all non-partitioned items as not primary shard", shardNdx, item.TableName);
                        await _backend.ExecuteRawAsync(DatabaseReplica.ReadWrite, shardNdx, async conn =>
                        {
                            await conn.ExecuteAsync($"DELETE FROM {item.TableName}").ConfigureAwait(false);
                        }).ConfigureAwait(false);
                    }
                }
            }
        }

        #endregion // Re-sharding

        /// <summary>
        /// Ensure that database schemas and sharding is up-to-date.
        /// </summary>
        /// <returns></returns>
        public async Task EnsureMigratedAsync()
        {
            DatabaseOptions dbOpts = RuntimeOptionsRegistry.Instance.GetCurrent<DatabaseOptions>();

            // Database master version must be defined in Options.xx.yaml
            if (dbOpts.MasterVersion <= 0)
                throw new InvalidOperationException($"{nameof(DatabaseOptions)}.{nameof(DatabaseOptions.MasterVersion)} is invalid, set it to a positive integer value in Config/Options.base.yaml");

            // Fetch latest meta info (null if table or row doesn't exists)
            DatabaseMetaInfo metaInfo = await _db.TryGetLatestMetaInfoAsync().ConfigureAwait(false);

            // Handle database master version
            if (metaInfo == null)
            {
                // No meta info found, create empty MetaInfo
                _log.Information("No meta-info found in database, assuming empty database");
                metaInfo = new DatabaseMetaInfo(version: 0, DateTime.UnixEpoch, masterVersion: 0, _db.NumActiveShards);
            }
            else
            {
                // Database exists, check if should reset database due to master version mismatch
                if (metaInfo.MasterVersion != dbOpts.MasterVersion)
                {
                    // Check that resets are allowed
                    if (!dbOpts.NukeOnVersionMismatch)
                        throw new InvalidOperationException($"Database master version mismatch: got v{metaInfo.MasterVersion}, expecting v{dbOpts.MasterVersion}, database reset not allowed!");

                    // Drop all tables in all shards
                    _log.Warning("Database master version has changed from v{ExistingDBVersion} to v{ServerDBVersion}, resetting all database shards!", metaInfo.MasterVersion, dbOpts.MasterVersion);
                    await ResetAllShardsAsync().ConfigureAwait(false);
                }
            }

            // Migrate all shards to latest schema version
            _log.Information("Migrating active database shards to latest schema ({NumActiveShards} active shards)", _db.NumActiveShards);
            await MigrateShardsAsync(0, _db.NumActiveShards).ConfigureAwait(false);

            // Check if need re-sharding (number of shards has changed)
            int numOldShards = metaInfo.NumShards;
            if (_db.NumActiveShards != numOldShards)
            {
                // If down-sharding, ensure that old shards are also up-to-date
                if (numOldShards > _db.NumActiveShards)
                {
                    if (numOldShards > _backend.NumTotalShards)
                        throw new InvalidOperationException($"Database contains references to {numOldShards} shards, but only {_backend.NumTotalShards} shards are specified!");

                    _log.Information("Migrating old shards #{StartShardNdx}..#{EndShardNdx} to latest schema version", _db.NumActiveShards, numOldShards - 1);
                    await MigrateShardsAsync(_db.NumActiveShards, numOldShards - _db.NumActiveShards).ConfigureAwait(false);
                }

                // Log tables to be re-sharded
                _log.Information("Re-sharding database from {NumOldShards} to {NumNewShards} shards, with tables:", numOldShards, _db.NumActiveShards);
                foreach (DatabaseItemSpec item in DatabaseTypeRegistry.ItemSpecs)
                    _log.Information("  {TableName}: primaryKey={PrimaryKey} partitionKey={PartitionKey}", item.TableName, item.PrimaryKeyName, item.IsPartitioned ? item.PartitionKeyName : "<none>");

                // Perform the re-sharding
                await ReshardAllEntitiesAsync(numOldShards).ConfigureAwait(false);
            }

            // Write meta info (if anything changed)
            if (metaInfo.MasterVersion != dbOpts.MasterVersion || _db.NumActiveShards != metaInfo.NumShards)
                await _db.InsertMetaInfoAsync(new DatabaseMetaInfo(metaInfo.Version + 1, DateTime.Now, dbOpts.MasterVersion, _db.NumActiveShards)).ConfigureAwait(false);
        }
    }
}
