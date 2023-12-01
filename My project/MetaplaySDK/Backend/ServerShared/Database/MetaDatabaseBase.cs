// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Dapper;
using Metaplay.Cloud;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Server.AdminApi.AuditLog;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.Database
{
    public enum QueryPriority
    {
        Lowest,     // No query time requirements, run queries when no other database queries are active.
        Low,        // Use for expensive queries (eg, ones that may return many results) where latency requirements aren't strict. Only a few queries are allowed at a time.
        Normal      // Normal level with no throttling. Should be used only used for cheap queries where low latency is important.
    }

    /// <summary>
    /// Meta-information about database. Always put on the first shard (not partitioned).
    /// </summary>
    [Table("MetaInfo")]
    [NonPartitioned]
    public class DatabaseMetaInfo : IPersistedItem
    {
        [Key]
        [Required]
        public int      Version         { get; set; }   // Unique incrementing version (latest one is always valid)

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime Timestamp       { get; set; }   // Time when change was done

        [Required]
        public int      MasterVersion   { get; set; }   // Master version of database, bumping the version causes full reset of database!

        [Required]
        public int      NumShards       { get; set; }   // Number of shards in the database (if differs from config, re-sharding is needed)

        public DatabaseMetaInfo()
        {
        }

        public DatabaseMetaInfo(int version, DateTime timestamp, int masterVersion, int numShards)
        {
            Version         = version;
            Timestamp       = timestamp;
            MasterVersion   = masterVersion;
            NumShards       = numShards;
        }
    }

    // Dapper type converters to force all DateTimes to UTC.

    public class DapperDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.Value = value;
        }

        public override DateTime Parse(object value)
        {
            switch (value)
            {
                case string str:
                    // With SQLite, we get strings, so let's parse it and force UTC
                    return DateTime.SpecifyKind(DateTime.Parse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTimeKind.Utc);
                case DateTime dt:
                    // With MySQL, we get DateTimes with DateTimeKind.Unspecified, so let's force UTC
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                default:
                    throw new InvalidOperationException($"Unable to convert input '{value}' into a DateTime");
            }
        }
    }

    /// <summary>
    /// TODO
    /// </summary>
    /// <remarks>
    /// To initialize the database, call the <see cref="Initialize{TDbContext, TGameClass}(DatabaseOptions)"/>
    /// with your game-specific database context and accessor classes.
    ///
    /// This class performs its operations via one of the classes implementing <see cref="DatabaseBackend"/>.
    /// There's a different implementation for both MySQL (for cloud use) and SQLite (for running the server
    /// locally).
    ///
    /// Also, throttling behavior is supported for Low and Lowest priority operations, to limit the number
    /// of simultaneous expensive queries, which could impact the performance of the database.
    /// </remarks>
    public class MetaDatabaseBase : IMetaIntegrationConstructible<MetaDatabaseBase>
    {
        static Prometheus.Counter       c_throttleEnqueuedTotal     = Prometheus.Metrics.CreateCounter("meta_db_throttle_ops_enqueued_total", "Number of DB operations entered throttling system", "priority");
        static Prometheus.Counter       c_throttleDequeuedTotal     = Prometheus.Metrics.CreateCounter("meta_db_throttle_ops_dequeued_total", "Number of DB operations completed throttling", "priority");
        static Prometheus.Counter       c_throttleBlockedTotal      = Prometheus.Metrics.CreateCounter("meta_db_throttle_ops_blocked_total", "Number of DB operations put into a blocking queue due to throttling", "priority");
        static Prometheus.Counter       c_throttleUnblockedTotal    = Prometheus.Metrics.CreateCounter("meta_db_throttle_ops_unblocked_total", "Number of DB operation completed the blocking queue after being blocked.", "priority");

        public readonly int             NumActiveShards = DatabaseBackend.Instance.NumActiveShards;
        protected static IMetaLogger    _logger = MetaLogger.ForContext("MetaDatabase");
        protected DatabaseBackend       _backend = DatabaseBackend.Instance;
        protected IDatabaseThrottle     _throttle;

        static Dictionary<QueryPriority, MetaDatabaseBase> _priorityContexts = null;

        public MetaDatabaseBase()
        {
        }

        class SemaphoreAutoRelease : IDisposable
        {
            readonly SemaphoreSlim _sem;
            readonly IDisposable _inner;

            public SemaphoreAutoRelease(SemaphoreSlim sem, IDisposable inner)
            {
                _sem = sem;
                _inner = inner;
            }

            public void Dispose()
            {
                if (_inner != null)
                {
                    _inner.Dispose();
                }
                _sem.Release();
            }
        }

        /// <summary>
        /// Top-level throttle for a single priority class.
        /// </summary>
        class DatabaseTopLevelPriorityClassThrottle : IDatabaseThrottle
        {
            readonly DatabaseThrottlePerShardState<SemaphoreSlim> _states;
            readonly Prometheus.Counter.Child _enqueuedCounter;
            readonly Prometheus.Counter.Child _dequeuedCounter;
            readonly Prometheus.Counter.Child _blockedCounter;
            readonly Prometheus.Counter.Child _unblockedCounter;

            public DatabaseTopLevelPriorityClassThrottle(int numShards, int limit, string priorityClassName)
            {
                _states             = new DatabaseThrottlePerShardState<SemaphoreSlim>(numShards, stateInitializer: (replica, shardNdx) => new SemaphoreSlim(limit));
                _enqueuedCounter    = c_throttleEnqueuedTotal.WithLabels(priorityClassName);
                _dequeuedCounter    = c_throttleDequeuedTotal.WithLabels(priorityClassName);
                _blockedCounter     = c_throttleBlockedTotal.WithLabels(priorityClassName);
                _unblockedCounter   = c_throttleUnblockedTotal.WithLabels(priorityClassName);
            }

            public async Task<IDisposable> LockAsync(DatabaseReplica replica, int shardNdx)
            {
                _enqueuedCounter.Inc();

                SemaphoreSlim sem = _states.GetState(replica, shardNdx);
                Task semWait = sem.WaitAsync();

                if (!semWait.IsCompleted)
                {
                    // Wait was not complete immediately. We could not enter semaphore immediately and awaiting so will block. Update
                    // counters and start the await.
                    _blockedCounter.Inc();
                    await semWait.ConfigureAwait(false);

                    // Await is over.
                    _unblockedCounter.Inc(1);
                }

                _dequeuedCounter.Inc(1);
                return new SemaphoreAutoRelease(sem, inner: null);
            }
        }

        public static void Initialize()
        {
            DatabaseOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<DatabaseOptions>();
            _logger.Information("Initializing database with {DatabaseBackend} backend and {NumShards} shards", opts.Backend, opts.NumActiveShards);

            // Initialize database backend
            DatabaseBackend.Initialize(opts, () => IntegrationRegistry.Create<MetaDbContext>());

            // Register Dapper type conversions
            RegisterDapperTypeConversions();

            // Initialize type registry
            DatabaseTypeRegistry.Initialize(DatabaseBackend.Instance, IntegrationRegistry.GetSingleIntegrationType<MetaDbContext>());

            // Initialize per priority context objects
            _priorityContexts = new Dictionary<QueryPriority, MetaDatabaseBase>();
            foreach (QueryPriority prio in Enum.GetValues(typeof(QueryPriority)))
            {
                MetaDatabaseBase ctx = IntegrationRegistry.Create<MetaDatabaseBase>();
                ctx._throttle = CreateDatabaseThrottle(prio, opts);
                _priorityContexts[prio] = ctx;
            }
        }

        static IDatabaseThrottle CreateDatabaseThrottle(QueryPriority prio, DatabaseOptions opts)
        {
            switch (prio)
            {
                case QueryPriority.Normal:
                    return DatabaseThrottleNop.Instance;
                case QueryPriority.Low:
                    return new DatabaseTopLevelPriorityClassThrottle(
                                numShards: DatabaseBackend.Instance.NumActiveShards,
                                limit: opts.MaxConnectionsLowPriority,
                                priorityClassName: "low");
                case QueryPriority.Lowest:
                    return new DatabaseTopLevelPriorityClassThrottle(
                                numShards: DatabaseBackend.Instance.NumActiveShards,
                                limit: opts.MaxConnectionsLowestPriority,
                                priorityClassName: "lowest");
                default:
                    MetaDebug.AssertFail("Unhandled QueryPriority {QueryPriority}", prio);
                    return null;
            }
        }

        static void RegisterDapperTypeConversions()
        {
            SqlMapper.AddTypeHandler(new DapperDateTimeHandler());
        }

        public static MetaDatabaseBase Get(QueryPriority priority = QueryPriority.Normal)
        {
            if (_priorityContexts == null)
                throw new InvalidOperationException($"{nameof(MetaDatabaseBase)} has not been initialized!");
            return _priorityContexts[priority];
        }

        public static TDatabase Get<TDatabase>(QueryPriority priority) where TDatabase : MetaDatabaseBase
        {
            return (Get(priority) as TDatabase) ?? throw new InvalidOperationException($"Database context is not of type {typeof(TDatabase).Name}"); ;
        }

        /// <summary>
        /// Get the exact item counts for all tables and all shards.
        /// WARNING: This method can take a long time to run with large databases (many minutes).
        /// </summary>
        /// <returns></returns>
        public async Task<OrderedDictionary<string, int[]>> GetTableItemCountsAsync()
        {
            // Initialize itemCounts with empty
            OrderedDictionary<string, int[]> itemCounts = new OrderedDictionary<string, int[]>();
            foreach (DatabaseItemSpec itemSpec in DatabaseTypeRegistry.ItemSpecs)
                itemCounts.Add(itemSpec.TableName, new int[NumActiveShards]);

            // \note Run all the shards in parallel, but queries on a single shard sequentially.
            await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(async shardNdx =>
                {
                    foreach (DatabaseItemSpec itemSpec in DatabaseTypeRegistry.ItemSpecs)
                    {
                        int itemCount = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "Count", async conn =>
                        {
                            return await conn.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {itemSpec.TableName}").ConfigureAwait(false);
                        }).ConfigureAwait(false);

                        itemCounts[itemSpec.TableName][shardNdx] = itemCount;
                    }
                })).ConfigureAwait(false);

            return itemCounts;
        }

        /// <summary>
        /// Get estimated item counts for all tables and all shards.
        /// NOTE: The estimates can be off by at least factor of two due to how MySQL does its estimate.
        /// </summary>
        /// <returns></returns>
        public async Task<OrderedDictionary<string, int[]>> EstimateTableItemCountsAsync()
        {
            // Initialize itemCounts with empty
            OrderedDictionary<string, int[]> itemCounts = new OrderedDictionary<string, int[]>();
            foreach (DatabaseItemSpec itemSpec in DatabaseTypeRegistry.ItemSpecs)
                itemCounts.Add(itemSpec.TableName, new int[NumActiveShards]);

            await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(async shardNdx =>
                {
                    foreach (DatabaseItemSpec itemSpec in DatabaseTypeRegistry.ItemSpecs)
                    {
                        int itemCount = await _backend.EstimateItemCountInTableAsync(shardNdx, itemSpec.TableName).ConfigureAwait(false);
                        itemCounts[itemSpec.TableName][shardNdx] = itemCount;
                    }
                })).ConfigureAwait(false);

            return itemCounts;
        }

        #region MetaInfo

        public Task InsertMetaInfoAsync(DatabaseMetaInfo meta)
        {
            DatabaseItemSpec itemType = DatabaseTypeRegistry.GetItemSpec<DatabaseMetaInfo>();
            return _backend.ExecuteRawAsync(DatabaseReplica.ReadWrite, shardNdx: 0, conn =>
            {
                return conn.ExecuteAsync(itemType.InsertQuery, meta);
            });
        }

        public async Task<DatabaseMetaInfo> TryGetLatestMetaInfoAsync()
        {
            // If MetaInfo table doesn't exist, return null
            DatabaseItemSpec itemType = DatabaseTypeRegistry.GetItemSpec<DatabaseMetaInfo>();
            if (!await _backend.TableExistsAsync(itemType.TableName).ConfigureAwait(false))
                return null;

            return await _backend.DapperExecuteAsync<DatabaseMetaInfo>(_throttle, DatabaseReplica.ReadWrite, itemType.TableName, shardNdx: 0, "Get", async conn =>
            {
                return await conn.QuerySingleOrDefaultAsync<DatabaseMetaInfo>($"SELECT * FROM {itemType.TableName} ORDER BY Version DESC LIMIT 1").ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        public async Task HealthCheck()
        {
            // Do any simple query against the ReadWrite replica
            // Using table exists query for the meta info table for now
            DatabaseItemSpec itemType = DatabaseTypeRegistry.GetItemSpec<DatabaseMetaInfo>();
            await _backend.TableExistsAsync(itemType.TableName).ConfigureAwait(false);
        }

        #endregion // Meta

        #region IPersistedItem methods

        public Task<T> TryGetAsync<T>(string key) where T : IPersistedItem
        {
            CheckItemTypeIsSingleKey(typeof(T), "TryGetAsync with implicit partition");
            return TryGetAsync<T>(typeof(T), primaryKey: key, partitionKey: key);
        }

        public Task<T> TryGetAsync<T>(Type type, string key) where T : IPersistedItem
        {
            CheckItemTypeIsSingleKey(type, "TryGetAsync with implicit partition");
            return TryGetAsync<T>(type, primaryKey: key, partitionKey: key);
        }

        public Task<T> TryGetAsync<T>(string primaryKey, string partitionKey) where T : IPersistedItem
        {
            return TryGetAsync<T>(typeof(T), primaryKey, partitionKey);
        }

        public Task<T> TryGetAsync<T>(Type type, string primaryKey, string partitionKey) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec(type);
            int shardNdx = itemSpec.GetKeyShardNdx(partitionKey);
            return _backend.DapperExecuteAsync<T>(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "Get", async conn =>
            {
                return (T)await conn.QuerySingleOrDefaultAsync(itemSpec.ItemType, itemSpec.GetQuery, new { Key = primaryKey });
            });
        }

        public Task<bool> TestExistsAsync<T>(string key) where T : IPersistedItem
        {
            CheckItemTypeIsSingleKey(typeof(T), "TestExistsAsync with implicit partition");
            return TestExistsAsync(typeof(T), key);
        }

        public Task<bool> TestExistsAsync(Type type, string key)
        {
            CheckItemTypeIsSingleKey(type, "TestExistsAsync with implicit partition");
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec(type);
            int shardNdx = itemSpec.GetKeyShardNdx(key);
            return _backend.DapperExecuteAsync<bool>(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "Test", async conn =>
            {
                int testResult = await conn.QuerySingleOrDefaultAsync<int>(itemSpec.ExistsQuery, new { Key = key });
                return testResult == 1;
            });
        }

        public Task InsertAsync<T>(T item) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            if (item.GetType() != itemSpec.ItemType)
                throw new InvalidOperationException($"Trying to write object of type {item.GetType().ToGenericTypeString()} into database table '{itemSpec.TableName}' which has type {itemSpec.ItemType.ToGenericTypeString()}");

            (string partitionKey, int shardNdx) = itemSpec.GetItemShardNdx(item);
            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "Insert", async conn =>
            {
                int numInserted = await conn.ExecuteAsync(itemSpec.InsertQuery, item).ConfigureAwait(false);
                if (numInserted == 0)
                    throw new ItemAlreadyExistsException($"Item in table {itemSpec.TableName} with partition key {partitionKey} already exists");
                return numInserted;
            });
        }

        /// <summary>
        /// Insert the item if it doesn't already exist.
        /// If the item already exists, it is left unchanged.
        /// </summary>
        /// <returns>Whether the item was inserted.</returns>
        public Task<bool> InsertOrIgnoreAsync<T>(T item) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            if (item.GetType() != itemSpec.ItemType)
                throw new InvalidOperationException($"Trying to write object of type {item.GetType().ToGenericTypeString()} into database table '{itemSpec.TableName}' which has type {itemSpec.ItemType.ToGenericTypeString()}");

            (string partitionKey, int shardNdx) = itemSpec.GetItemShardNdx(item);
            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "Insert", async conn =>
            {
                int numInserted = await conn.ExecuteAsync(itemSpec.InsertOrIgnoreQuery, item).ConfigureAwait(false);
                return numInserted != 0;
            });
        }

        public Task InsertOrUpdateAsync<T>(T item) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            if (item.GetType() != itemSpec.ItemType)
                throw new InvalidOperationException($"Trying to write object of type {item.GetType().ToGenericTypeString()} into database table '{itemSpec.TableName}' which has type {itemSpec.ItemType.ToGenericTypeString()}");

            (string partitionKey, int shardNdx) = itemSpec.GetItemShardNdx(item);
            return _backend.DapperExecuteAsync<int>(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "InsertOrUpdate", async conn =>
            {
                // Number of rows affected is highly engine and situation specific:
                // MySQL: "the affected-rows value per row is 1 if the row is inserted as a new row, 2 if an existing row is updated, and 0 if an existing row is set to its current values"
                // so let's not even assign that to a variable
                _ = await conn.ExecuteAsync(itemSpec.InsertOrUpdateQuery, item).ConfigureAwait(false);
                return -1;
            });
        }

        public Task UpdateAsync<T>(T item) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            if (item.GetType() != itemSpec.ItemType)
                throw new InvalidOperationException($"Trying to write object of type {item.GetType().ToGenericTypeString()} into database table '{itemSpec.TableName}' which has type {itemSpec.ItemType.ToGenericTypeString()}");

            (string partitionKey, int shardNdx) = itemSpec.GetItemShardNdx(item);
            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "Update", async conn =>
            {
                int numUpdated = await conn.ExecuteAsync(itemSpec.UpdateQuery, item).ConfigureAwait(false);
                if (numUpdated == 0)
                    throw new NoSuchItemException($"No item in table {itemSpec.TableName} shard #{shardNdx} with primary key {itemSpec.GetItemPrimaryKey(item)} found");
                return numUpdated;
            });
        }

        public Task<bool> RemoveAsync<T>(string key) where T : IPersistedItem
        {
            CheckItemTypeIsSingleKey(typeof(T), "RemoveAsync with implicit partition");
            return RemoveAsync<T>(primaryKey: key, partitionKey: key);
        }

        public Task<bool> RemoveAsync<T>(string primaryKey, string partitionKey) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            int shardNdx = itemSpec.GetKeyShardNdx(partitionKey);
            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "Remove", async conn =>
            {
                int numRemoved = await conn.ExecuteAsync(itemSpec.RemoveQuery, new { Key = primaryKey }).ConfigureAwait(false);
                return numRemoved != 0;
            });
        }

        public Task MultiInsertOrIgnoreAsync<T>(IEnumerable<T> allItems) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            return MultiInsertQueryImplAsync(allItems, itemSpec.InsertOrIgnoreQuery);
        }

        public Task MultiInsertOrUpdateAsync<T>(IEnumerable<T> allItems) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            return MultiInsertQueryImplAsync(allItems, itemSpec.InsertOrUpdateQuery);
        }

        async Task MultiInsertQueryImplAsync<T>(IEnumerable<T> allItems, string query) where T : IPersistedItem
        {
            // Allocate list of items for each shard
            List<T>[] shardedItems = new List<T>[NumActiveShards];
            for (int ndx = 0; ndx < NumActiveShards; ndx++)
                shardedItems[ndx] = new List<T>();

            // Partition items for each shard
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            foreach (T item in allItems)
            {
                if (item.GetType() != itemSpec.ItemType)
                    throw new InvalidOperationException($"Trying to write object of type {item.GetType().ToGenericTypeString()} into database table '{itemSpec.TableName}' which has type {itemSpec.ItemType.ToGenericTypeString()}");

                (string partitionKey, int shardNdx) = itemSpec.GetItemShardNdx(item);
                shardedItems[shardNdx].Add(item);
            }

            // Bulk insert to each shard (in parallel)
            Task[] tasks = new Task[NumActiveShards];
            for (int shardNdxIter = 0; shardNdxIter < NumActiveShards; shardNdxIter++)
            {
                int shardNdx = shardNdxIter; // take copy to avoid lambda capture seeing the final iterated value
                List<T> itemsForShard = shardedItems[shardNdx];
                tasks[shardNdxIter] = Task.Run(async () =>
                {
                    await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "MultiInsert", async conn =>
                    {
                        DbTransaction txn = await conn.BeginTransactionAsync().ConfigureAwait(false);
                        try
                        {
                            int numInserted = await conn.ExecuteAsync(query, itemsForShard, transaction: txn).ConfigureAwait(false);
                            await txn.CommitAsync().ConfigureAwait(false);
                            return numInserted;
                        }
                        catch (Exception)
                        {
                            await txn.RollbackAsync().ConfigureAwait(false);
                            throw;
                        }
                        finally
                        {
                            await txn.DisposeAsync().ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);
                });
            }

            // Wait for all writes to finish
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Query the db shards and return information in pages
        /// </summary>
        /// <remarks>Uses read replica</remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="opName">Name of the operation (included as label in metrics)</param>
        /// <param name="iterator">Iterator for paged access</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <returns>Result containing the items and an iterator for continuing</returns>
        public Task<PagedQueryResult<T>> QueryPagedAsync<T>(string opName, PagedIterator iterator, int pageSize) where T : IPersistedItem
        {
            return QueryPagedPossiblyRangedAsyncImpl<T>(opName, iterator, pageSize, keyRange: null);
        }

        /// <summary>
        /// Query the db shards for entities with key greater than or equal to <paramref name="rangeFirstKeyInclusive"/>
        /// and less than or equal to <paramref name="rangeLastKeyInclusive"/>, and return information in pages
        /// </summary>
        /// <remarks>Uses read replica</remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="opName">Name of the operation (included as label in metrics)</param>
        /// <param name="iterator">Iterator for paged access</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="rangeFirstKeyInclusive">First key in the range</param>
        /// <param name="rangeLastKeyInclusive">Last key in the range</param>
        /// <returns>Result containing the items and an iterator for continuing</returns>
        public Task<PagedQueryResult<T>> QueryPagedRangeAsync<T>(string opName, PagedIterator iterator, int pageSize, string rangeFirstKeyInclusive, string rangeLastKeyInclusive) where T : IPersistedItem
        {
            if (rangeFirstKeyInclusive == null)
                throw new ArgumentNullException(nameof(rangeFirstKeyInclusive));
            if (rangeLastKeyInclusive == null)
                throw new ArgumentNullException(nameof(rangeLastKeyInclusive));

            return QueryPagedPossiblyRangedAsyncImpl<T>(opName, iterator, pageSize, keyRange: (rangeFirstKeyInclusive, rangeLastKeyInclusive));
        }

        /// <summary>
        /// Query the db shards for entities with key greater than or equal to <paramref name="rangeFirstKeyInclusive"/>
        /// and less than or equal to <paramref name="rangeLastKeyInclusive"/>, and return a single page of results
        /// starting from <paramref name="startKeyExclusive"/>. This query wraps around the key space -- if a full page
        /// isn't filled by the end of running out of keys entries are added from the beginning of the key space.
        /// </summary>
        /// <remarks>Uses read replica</remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="opName">Name of the operation (included as label in metrics)</param>
        /// <param name="startKeyExclusive">Offset of returned page in key space</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="rangeFirstKeyInclusive">First key in the range</param>
        /// <param name="rangeLastKeyInclusive">Last key in the range</param>
        /// <returns>Result list of items</returns>
        public async Task<List<T>> QuerySinglePageRangeAsync<T>(string opName, string startKeyExclusive, int pageSize, string rangeFirstKeyInclusive, string rangeLastKeyInclusive) where T : IPersistedItem
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            int firstShard = itemSpec.GetKeyShardNdx(startKeyExclusive);
            List<T> items = new();
            for (int shard = 0; shard <= NumActiveShards; shard++)
            {
                int shardNdx = (shard + firstShard) % NumActiveShards;
                int numRemaining = pageSize - items.Count;

                items.AddRange(await PagedQuerySingleShard<T>(
                    opName:                     opName,
                    shardNdx:                   shardNdx,
                    iteratorStartKeyExclusive:  shard == 0 ? startKeyExclusive : "",
                    pageSize:                   numRemaining,
                    keyRange:                   (rangeFirstKeyInclusive, shard == NumActiveShards ? startKeyExclusive : rangeLastKeyInclusive)).ConfigureAwait(false));

                if (items.Count >= pageSize)
                    break;
            }
            return items;
        }

        async Task<PagedQueryResult<T>> QueryPagedPossiblyRangedAsyncImpl<T>(string opName, PagedIterator iterator, int pageSize, (string firstInclusive, string lastInclusive)? keyRange) where T : IPersistedItem
        {
            if (pageSize <= 0)
                throw new ArgumentException($"Page size must be greater than 0, but is {pageSize}", nameof(pageSize));

            if (iterator.IsFinished)
                throw new ArgumentException("Iterator is already finished", nameof(iterator));

            // Iterator holds the key of last item of previous page, or empty string for fresh iterator
            string iteratorStartKeyExclusive = iterator.StartKeyExclusive;

            // Iterate over all shards until have full page (or all objects scanned)
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();
            List<T> items = new List<T>();
            for (int shardNdx = iterator.ShardIndex; shardNdx < NumActiveShards; shardNdx++)
            {
                // Fetch a batch of items and append to result
                int numRemaining = pageSize - items.Count;

                items.AddRange(await PagedQuerySingleShard<T>(
                    opName:                     opName,
                    shardNdx:                   shardNdx,
                    iteratorStartKeyExclusive:  iteratorStartKeyExclusive,
                    pageSize:                   numRemaining,
                    keyRange:                   keyRange
                    ).ConfigureAwait(false));

                // If have full page, return it
                if (items.Count == pageSize)
                {
                    return new PagedQueryResult<T>(
                        new PagedIterator(
                            shardIndex:         shardNdx,
                            startKeyExclusive:  itemSpec.GetItemPrimaryKey(items.Last()),
                            isFinished:         false),
                        items);
                }
                else
                    MetaDebug.Assert(items.Count < pageSize, "pageSize was exceeded: items.Count = {0}, pageSize = {1}", items.Count, pageSize);

                // Reset iteratorStartKeyExclusive when switching shards
                iteratorStartKeyExclusive = "";
            }

            // Last batch, iterator finished
            return new PagedQueryResult<T>(PagedIterator.End, items);
        }

        Task<IEnumerable<T>> PagedQuerySingleShard<T>(string opName, int shardNdx, string iteratorStartKeyExclusive, int pageSize, (string firstInclusive, string lastInclusive)? keyRange)
        {
            if (keyRange.HasValue)
                return PagedQueryRangeSingleShard<T>(opName, shardNdx, iteratorStartKeyExclusive, pageSize, keyRange.Value.firstInclusive, keyRange.Value.lastInclusive);
            else
                return PagedQueryFullSingleShard<T>(opName, shardNdx, iteratorStartKeyExclusive, pageSize);
        }

        public Task<IEnumerable<T>> PagedQueryFullSingleShard<T>(string opName, int shardNdx, string iteratorStartKeyExclusive, int pageSize)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();

            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, opName, async conn =>
            {
                IEnumerable<object> items = await conn.QueryAsync(itemSpec.ItemType, itemSpec.PagedQuery, new
                {
                    StartKeyExclusive   = iteratorStartKeyExclusive,
                    PageSize            = pageSize,
                });
                return items.Cast<T>();
            });
        }

        Task<IEnumerable<T>> PagedQueryRangeSingleShard<T>(string opName, int shardNdx, string iteratorStartKeyExclusive, int pageSize, string rangeFirstKeyInclusive, string rangeLastKeyInclusive)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<T>();

            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, opName, async conn =>
            {
                // \note rangeFirstKeyInclusive is inclusive, iteratorStartKeyExclusive is exclusive.
                //       Select query type and start key based on which one is more constraining.
                bool isConstrainedByIterator = string.CompareOrdinal(iteratorStartKeyExclusive, rangeFirstKeyInclusive) >= 0;
                if (isConstrainedByIterator)
                {
                    IEnumerable<object> items = await conn.QueryAsync(itemSpec.ItemType, itemSpec.PagedRangeExclusiveStartQuery, new
                    {
                        StartKeyExclusive   = iteratorStartKeyExclusive,
                        LastKeyInclusive    = rangeLastKeyInclusive,
                        PageSize            = pageSize,
                    });
                    return items.Cast<T>();
                }
                else
                {
                    IEnumerable<object> items = await conn.QueryAsync(itemSpec.ItemType, itemSpec.PagedRangeInclusiveStartQuery, new
                    {
                        StartKeyInclusive   = rangeFirstKeyInclusive,
                        LastKeyInclusive    = rangeLastKeyInclusive,
                        PageSize            = pageSize,
                    });
                    return items.Cast<T>();
                }
            });
        }

        /// <summary>
        /// Checks that the <paramref name="persistedItemType"/> does not have non-trivial partition key, i.e. item
        /// can be identified with just the Key (and particulary without PartitionKey).
        /// </summary>
        protected void CheckItemTypeIsSingleKey(Type persistedItemType, string funcDescription)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec(persistedItemType);

            if (itemSpec.IsPartitioned)
            {
                if (itemSpec.PartitionKeyName != itemSpec.PrimaryKeyName)
                    throw new InvalidOperationException($"{funcDescription} requires item type to have partition key same as primary key, but {persistedItemType} has partition key {itemSpec.PartitionKeyName} and primary key {itemSpec.PrimaryKeyName}");
            }
        }

        #endregion // IPersistedItem methods

        #region CSAuditLog

        public async Task<List<PersistedAuditLogEvent>> QueryAuditLogEventsAsync(string eventIdLessThan, string targetType, string targetId, string source, string sourceIpAddress, string sourceCountryIsoCode, int pageSize)
        {
            // Construct the query dynamically
            List<string> whereClauses = new List<string>();
            dynamic paramList = new ExpandoObject();
            IEnumerable<int> shardsToQuery = Enumerable.Range(0, NumActiveShards);

            // Start event clause? Used for paging
            if (!string.IsNullOrEmpty(eventIdLessThan))
            {
                whereClauses.Add("EventId < @EventIdLessThan");
                paramList.EventIdLessThan = eventIdLessThan;
            }

            // Target clauses?
            if (!String.IsNullOrEmpty(targetType))
            {
                if (String.IsNullOrEmpty(targetId))
                {
                    whereClauses.Add("Target LIKE @PartialTargetString ESCAPE '/'");
                    paramList.PartialTargetString = $"{SqlUtil.EscapeSqlLike(targetType, '/')}:%";  // \note: escape with / because \ constant (for ESCAPE '') is interpreted differently in across SQL engines.
                }
                else
                {
                    whereClauses.Add("Target = @FullTargetString");
                    paramList.FullTargetString = $"{targetType}:{targetId}";

                    // If we have a full target specified then we can figure out in advance
                    // exactly which shard those events will be on (becase target is the
                    // partition key) and thus we only need to query that one shard
                    int itemShardNdx = _backend.GetShardIndex(paramList.FullTargetString);
                    shardsToQuery = Enumerable.Range(itemShardNdx, 1);
                }
            }

            // Source clauses?
            if (!String.IsNullOrEmpty(source))
            {
                whereClauses.Add("Source = @FullSourceString");
                paramList.FullSourceString = source;
            }
            if (!String.IsNullOrEmpty(sourceIpAddress))
            {
                whereClauses.Add("SourceIpAddress = @FullSourceIpAddressString");
                paramList.FullSourceIpAddressString = sourceIpAddress;
            }
            if (!String.IsNullOrEmpty(sourceCountryIsoCode))
            {
                whereClauses.Add("SourceCountryIsoCode = @FullSourceCountryIsoCodeString");
                paramList.FullSourceCountryIsoCodeString = sourceCountryIsoCode;
            }

            // Paging clause
            string pagingClause = "ORDER BY EventId DESC LIMIT @PageSize";
            paramList.PageSize = pageSize;

            // Construct full query
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedAuditLogEvent>();
            string query = $"SELECT * FROM {itemSpec.TableName}";
            if (whereClauses.Count > 0)
                query += $" WHERE {String.Join(" AND ", whereClauses)}";
            query += " " + pagingClause;

            // Perform query on multiple shards and collate the results
            List<PersistedAuditLogEvent>[] shardResults = await Task.WhenAll(
                shardsToQuery
                .Select(shardNdx => QueryAuditLogEventsShardAsync(query, (object)paramList, shardNdx)))
                .ConfigureAwait(false);
            return
                shardResults
                .SelectMany(v => v)
                .OrderByDescending(header => header.EventId, StringComparer.Ordinal)
                .Take(pageSize)
                .ToList();
        }

        public async Task<PersistedAuditLogEvent> GetAuditLogEventAsync(string eventId)
        {
            // Construct full query
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedAuditLogEvent>();
            string query = $"SELECT * FROM {itemSpec.TableName} WHERE EventId = @FullEventId";
            object paramList = new
            {
                FullEventId = eventId
            };

            // We don't know which shard the event will be on so we have to query them all, but
            // we do know that the result will only exist on one shard
            List<PersistedAuditLogEvent>[] shardResults = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx => QueryAuditLogEventsShardAsync(query, paramList, shardNdx)))
                .ConfigureAwait(false);
            return shardResults
                .SelectMany(v => v)
                .SingleOrDefault();
        }

        public async Task<List<PersistedAuditLogEvent>> QueryAuditLogEventsShardAsync(string query, object paramList, int shardNdx)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedAuditLogEvent>();
            IEnumerable<PersistedAuditLogEvent> result = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "QueryPaged", conn =>
                conn.QueryAsync<PersistedAuditLogEvent>(query, paramList)).ConfigureAwait(false);
            return result.ToList();
        }

        async Task<int> PurgeAuditLogEventsAsyncAsync(int shardNdx, string endEventId)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedAuditLogEvent>();
            string query = $"DELETE FROM {itemSpec.TableName} WHERE EventId < @EndEventId";
            return await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "DeleteBulk", conn =>
                conn.ExecuteAsync(query, new { EndEventId = endEventId }))
                .ConfigureAwait(false);
        }

        public async Task<int> PurgeAuditLogEventsAsync(MetaTime removeUntil)
        {
            // Create start eventId from timestamp
            string endEventId = EventId.SearchStringFromTime(removeUntil).ToString();

            // Fetch stats from all shards
            IEnumerable<int> counts = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx => PurgeAuditLogEventsAsyncAsync(shardNdx, endEventId)))
                .ConfigureAwait(false);

            return counts.Sum();
        }

        #endregion // CSAuditLog
    }
}
