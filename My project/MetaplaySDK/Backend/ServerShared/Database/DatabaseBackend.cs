// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace Metaplay.Server.Database
{
    public class ItemAlreadyExistsException : Exception
    {
        public ItemAlreadyExistsException(string message) : base(message)
        {
        }
    }

    public class NoSuchItemException : Exception
    {
        public NoSuchItemException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Decides where to perform a database action
    /// </summary>
    public enum DatabaseReplica
    {
        ReadWrite,          // Read/write primary replica
        ReadOnly,           // Read-only replica (eventually consistent, typically only delayed by some milliseconds)
    };

    /// <summary>
    /// Base class for different implementations of database backends. Currently, MySQL and SQLite are supported.
    /// MySQL is intended for use in the cloud and SQLite is used by default when running the server locally (to
    /// avoid needing to install MySQL on the machine).
    ///
    /// This class abstracts all the differences betewen the database implementations such as case sensitivity,
    /// differences in query syntax, aligning some feature functionality, etc.
    ///
    /// Prometheus statistics are also gathered from the performed operations to allow identifying and alert
    /// on any potential issues with the databae usage.
    ///
    /// Most of the query methods are implemented with Dapper, but there are also a few that use Entity Framework
    /// Core. For any performance sensitive queries, please use the Dapper-based variants as the EFCore-based
    /// queries have been known to make blocking database queries, which can significantly hinder the server's
    /// performance. The EFCore queries are only intended to be used during the databaes initialization, for
    /// schema migrations and re-sharding.
    /// </summary>
    public abstract class DatabaseBackend
    {
        // Metrics

        static readonly string[] c_metricsLabels = new string[] { "replica", "shard", "table", "op" };
        static Prometheus.HistogramConfiguration c_durationConfig = new Prometheus.HistogramConfiguration
        {
            Buckets     = Metaplay.Cloud.Metrics.Defaults.CoarseLatencyDurationBuckets,
            LabelNames  = c_metricsLabels,
        };
        static Prometheus.Counter   c_operations        = Prometheus.Metrics.CreateCounter("shardy_ops_total", "Amount of operations by table and operation type", c_metricsLabels);
        static Prometheus.Counter   c_operationErrors   = Prometheus.Metrics.CreateCounter("shardy_op_errors_total", "Amount of errors during operations by type", c_metricsLabels);
        static Prometheus.Histogram c_opElapsed         = Prometheus.Metrics.CreateHistogram("shardy_op_duration", "Duration of operation", c_durationConfig);

        // Singleton
        public static DatabaseBackend Instance { get; private set; }

        // Members
        protected readonly IMetaLogger              _log;
        protected readonly DatabaseShardSpec[]      _shardSpecs;
        protected readonly Func<MetaDbContext>      _createGameDbContext;

        public readonly int                         NumActiveShards;
        public int                                  NumTotalShards => _shardSpecs.Length;

        public abstract DatabaseBackendType         BackendType { get; }

        protected DatabaseBackend(int numActiveShards, DatabaseShardSpec[] shardSpecs, Func<MetaDbContext> createGameDbContext)
        {
            _log = MetaLogger.ForContext(this.GetType().Name);
            NumActiveShards = numActiveShards;
            _shardSpecs = shardSpecs;
            _createGameDbContext = createGameDbContext;
        }

        public static void Initialize(DatabaseOptions opts, Func<MetaDbContext> createGameDbContext)
        {
            if (Instance != null)
                throw new InvalidOperationException($"Database backend already initialized");

            // Initialize database & register globally
            if (opts.Shards == null)
                throw new InvalidOperationException("DatabaseOptions.Shards is null");
            else if (opts.Shards.Length == 0)
                throw new InvalidOperationException("DatabaseOptions.Shards is empty");

            if (opts.NumActiveShards > opts.Shards.Length)
                throw new InvalidOperationException($"DatabaseOptions.NumActiveShards ({opts.NumActiveShards}) is larger than Shards.Count ({opts.Shards.Length})");

            // Validate common shard spec members
            foreach (DatabaseShardSpec shard in opts.Shards)
            {
                if (string.IsNullOrEmpty(shard.UserId))
                    throw new ArgumentException("Shard with empty UserId given");

                if (string.IsNullOrEmpty(shard.Password))
                    throw new ArgumentException("Shard with empty Password given");
            }

            // Create the database backend singleton
            switch (opts.Backend)
            {
                case DatabaseBackendType.Sqlite:
                    Instance = new DatabaseBackendSqlite(opts.NumActiveShards, opts.Shards, createGameDbContext);
                    break;

                case DatabaseBackendType.MySql:
                    Instance = new DatabaseBackendMySql(opts.NumActiveShards, opts.Shards, createGameDbContext);
                    break;

                default:
                    throw new ArgumentException($"Invalid database backend {opts.Backend}");
            }
        }

        /// <summary>Create an EFCore database context (<c>GameDbContext</c>) for the given database replica and shard.</summary>
        public MetaDbContext CreateContext(DatabaseReplica replica, int shardNdx)
        {
            MetaDbContext ctx = _createGameDbContext();
            ctx.SetConnection(BackendType, GetConnectionString(replica, shardNdx));
            return ctx;
        }

        /// <summary>Get the connection string to the specific database replica and shard.</summary>
        protected abstract string GetConnectionString(DatabaseReplica replica, int shardNdx);

        /// <summary>Create a connection to the specific database replica and shard.</summary>
        public abstract Task<DbConnection> CreateConnectionAsync(DatabaseReplica replica, int shardNdx);

        /// <summary>Check whether a table with the given name exists in the database.</summary>
        public abstract Task<bool> TableExistsAsync(string tableName);

        /// <summary>Compute an estimate on the number of items in the given table.</summary>
        public abstract Task<int> EstimateItemCountInTableAsync(int shardNdx, string tableName);

        /// <summary>Is ORDER BY clause supported in DELETE operations</summary>
        public abstract bool SupportsDeleteOrderBy();

        /// <summary>Return the SQL for computing the sharding function on a given input variable.</summary>
        public abstract string GetShardingSql(string keyName);

        /// <summary>Return the SQL fragment for Insert-Or-Ignore operation.</summary>
        public abstract string GetInsertOrIgnoreSql();

        /// <summary>Return the SQL conflict resolution fragment for.</summary>
        public abstract string GetInsertOnConflictSetSql(string primaryKeyName, string setValuesSqlFragment);

        /// <summary>
        /// Use mini-MD5 (first 32 bits of MD5) for hashing, as it can be replicated on MySql.
        /// </summary>
        /// <param name="partitionKey"></param>
        /// <returns></returns>
        public int GetShardIndex(string partitionKey) =>
            (int)(MiniMD5.ComputeMiniMD5(partitionKey) % NumActiveShards);

        #region Raw queries

        public async Task ExecuteRawAsync(DatabaseReplica replica, int shardNdx, Func<DbConnection, Task> opFunc)
        {
            using (DbConnection conn = await CreateConnectionAsync(replica, shardNdx).ConfigureAwait(false))
            {
                await opFunc(conn).ConfigureAwait(false);
            }
        }

        public async Task<TResult> ExecuteRawAsync<TResult>(DatabaseReplica replica, int shardNdx, Func<DbConnection, Task<TResult>> opFunc)
        {
            using (DbConnection conn = await CreateConnectionAsync(replica, shardNdx).ConfigureAwait(false))
            {
                return await opFunc(conn).ConfigureAwait(false);
            }
        }

        #endregion // Raw queries

        #region EFCore queries

        /// <summary>
        /// Execute operation on database shard using EFCore.
        /// This method should only be used during server initialization as it may block the executing thread.
        /// </summary>
        /// <param name="shardNdx">Index of shard to operate on</param>
        /// <param name="opFunc">Functino to operate</param>
        /// <param name="replica">Choose between read-write and read-only replica</param>
        /// <returns></returns>
        internal async Task ExecuteShardedDbContextAsync(DatabaseReplica replica, int shardNdx, Func<MetaDbContext, Task> opFunc)
        {
            using (MetaDbContext context = CreateContext(replica, shardNdx))
                await opFunc(context).ConfigureAwait(false);
        }

        #endregion // EFCore queries

        #region Dapper queries

        public async Task<TResult> DapperExecuteAsync<TResult>(IDatabaseThrottle throttle, DatabaseReplica replica, string tableName, int shardNdx, string opLabel, Func<DbConnection, Task<TResult>> opFunc)
        {
            using (await throttle.LockAsync(replica, shardNdx).ConfigureAwait(false))
            {
                string[] labels = new string[] { replica.ToString(), shardNdx.ToString(CultureInfo.InvariantCulture), tableName, opLabel };
                c_operations.WithLabels(labels).Inc();

                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    using (DbConnection conn = await CreateConnectionAsync(replica, shardNdx).ConfigureAwait(false))
                    {
                        return await opFunc(conn).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    c_operationErrors.WithLabels(labels).Inc();
                    throw;
                }
                finally
                {
                    c_opElapsed.WithLabels(labels).Observe(sw.Elapsed.TotalSeconds);
                }
            }
        }

        public async Task<TResult> DapperExecuteTransactionAsync<TResult>(IDatabaseThrottle throttle, DatabaseReplica replica, IsolationLevel isolationLevel, int shardNdx, string txnName, string opLabel, Func<DbConnection, DbTransaction, Task<TResult>> opFunc)
        {
            using (await throttle.LockAsync(replica, shardNdx).ConfigureAwait(false))
            {
                string[] labels = new string[] { replica.ToString(), shardNdx.ToString(CultureInfo.InvariantCulture), txnName, opLabel };
                c_operations.WithLabels(labels).Inc();

                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    using (DbConnection conn = await CreateConnectionAsync(replica, shardNdx).ConfigureAwait(false))
                    {
                        using (DbTransaction txn = await conn.BeginTransactionAsync(isolationLevel).ConfigureAwait(false))
                        {
                            return await opFunc(conn, txn).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    c_operationErrors.WithLabels(labels).Inc();
                    throw;
                }
                finally
                {
                    c_opElapsed.WithLabels(labels).Observe(sw.Elapsed.TotalSeconds);
                }
            }
        }

        public Task<TResult> DapperExecuteAsync<TResult>(IDatabaseThrottle throttle, DatabaseReplica replica, string tableName, string partitionKey, string opLabel, Func<DbConnection, Task<TResult>> opFunc)
        {
            return DapperExecuteAsync(throttle, replica, tableName, GetShardIndex(partitionKey), opLabel, opFunc);
        }

        #endregion // Dapper queries
    }
}
