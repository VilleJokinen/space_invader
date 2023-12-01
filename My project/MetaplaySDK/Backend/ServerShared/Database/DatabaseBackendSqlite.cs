// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Dapper;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.Database
{
    // DatabaseBackendSqlite

    public class DatabaseBackendSqlite : DatabaseBackend
    {
        public override DatabaseBackendType BackendType => DatabaseBackendType.Sqlite;

        string[]            _connStrings;   // Pre-computed connection strings for all shards
        SqliteConnection[]  _inMemoryConns; // Dummy connections when using in-memory database to keep it alive while the server is running

        public DatabaseBackendSqlite(int numActiveShards, DatabaseShardSpec[] shardSpecs, Func<MetaDbContext> createGameDbContext)
            : base(numActiveShards, shardSpecs, createGameDbContext)
        {
            DatabaseOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<DatabaseOptions>();

            // Validate inputs
            foreach (DatabaseShardSpec shard in shardSpecs)
            {
                // Must have valid FilePath (even when using in-memory dbs to distinguish between shards)
                if (string.IsNullOrEmpty(shard.FilePath))
                    throw new ArgumentException($"Shard with empty FilePath given");
            }

            // Connection string for in-memory databases
            string inMemoryConnStr = opts.SqliteInMemory ? ";Mode=Memory;Cache=Shared" : "";

            // Construct connection strings for each shard
            _connStrings = shardSpecs.Select(shard => $"Data Source={shard.FilePath}{inMemoryConnStr}").ToArray();

            // When using in-memory database, must keep an open connection to each shard so the in-memory dbs don't get discarded
            if (opts.SqliteInMemory)
            {
                _inMemoryConns = _connStrings.Select(connStr =>
                {
                    SqliteConnection conn = new SqliteConnection(connStr);
                    conn.Open();
                    return conn;
                }).ToArray();
            }
        }

        protected override string GetConnectionString(DatabaseReplica replica, int shardNdx)
        {
            if (shardNdx < 0 || shardNdx >= _connStrings.Length)
                throw new ArgumentException($"Invalid shardNdx {shardNdx} (numTotalShards={NumTotalShards})");

            // There are no read replicas with SQLite, so both types of DatabaseReplica return the main instance
            return _connStrings[shardNdx];
        }

        public override async Task<DbConnection> CreateConnectionAsync(DatabaseReplica replica, int shardNdx)
        {
            if (shardNdx < 0 || shardNdx >= _connStrings.Length)
                throw new ArgumentException($"Invalid shardNdx {shardNdx} (numTotalShards={NumTotalShards})");

            // SQLite implementation is Synchronous (but wrapped in async api) and some DBs such as MySQL are
            // asynchronous. This causes an subtle behavior change: If the DB implementation is SQLite, the
            // async ops are always RunToCompletion and continuation are never run deferred/posted to synchronization
            // context. If code now uses `ConfigureAwait(false)`, on SQLite the execution is never deferred and
            // continuations propagate the ExecutionContext & synchronizationContext, but on MySQL they do not.
            // Essentially `ConfigureAwait` has sometimes an effect, sometimes not. As this can cause suprising
            // behavior, let's force SQLite to be async as well.
            return await Task.Run(async () => await DoCreateConnectionAsync(replica, shardNdx));
        }

        async Task<DbConnection> DoCreateConnectionAsync(DatabaseReplica replica, int shardNdx)
        {
            SqliteConnection conn = new SqliteConnection(_connStrings[shardNdx]);
            await conn.OpenAsync().ConfigureAwait(false);

            // Register minimd5() function -- this is only needed when doing shard pruning after re-sharding
            conn.CreateFunction("minimd5", (string input) => MiniMD5.ComputeMiniMD5(input), isDeterministic: true);

            // Configure LIKE queries to use case-sensitive matching
            await conn.ExecuteAsync("PRAGMA case_sensitive_like = true;");

            return conn;
        }

        public override Task<bool> TableExistsAsync(string tableName)
        {
            return DapperExecuteAsync<bool>(DatabaseThrottleNop.Instance, DatabaseReplica.ReadWrite, tableName, shardNdx: 0, "Get", async conn =>
            {
                int numRows = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{tableName}'");
                return numRows > 0;
            });
        }

        public override Task<int> EstimateItemCountInTableAsync(int shardNdx, string tableName) =>
            ExecuteRawAsync(DatabaseReplica.ReadOnly, shardNdx, conn => conn.QuerySingleAsync<int>($"SELECT count(*) FROM {tableName}"));

        public override bool SupportsDeleteOrderBy() => false;

        /// <summary>
        /// Computes 32-bit MD5 value matching Metaplay MiniMD5. Useful for pruning database shards after cloning when doing up-sharding in integer multiples.
        /// </summary>
        public override string GetShardingSql(string keyName) => $"minimd5({keyName})";

        public override string GetInsertOrIgnoreSql() => "INSERT OR IGNORE";

        public override string GetInsertOnConflictSetSql(string primaryKeyName, string setValuesSqlFragment) => $"ON CONFLICT({primaryKeyName}) DO UPDATE SET {setValuesSqlFragment} WHERE true";
    }
}
