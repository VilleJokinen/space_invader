// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Linq;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Cloud.Persistence
{
    /// <summary>
    /// Specify storage backend to use in persistence layer.
    /// </summary>
    public enum DatabaseBackendType
    {
        Sqlite,
        MySql,
    }

    /// <summary>
    /// Specify configuration for a single database shard.
    /// </summary>
    public class DatabaseShardSpec
    {
        public string DatabaseName  { get; set; }   // Database name to use (used with MySql)
        public string ReadWriteHost { get; set; }   // Primary read-write hostname (used with MySql)
        public string ReadOnlyHost  { get; set; }   // Read-only replica, maybe be slightly behind primary (used with MySql)
        public string FilePath      { get; set; }   // Path to file (used with Sqlite)

        public string UserId        { get; set; }   // User id for authentication
        [Sensitive]
        public string Password      { get; set; }   // Password for authentication
    }

    /// <summary>
    /// Options for database backend.
    /// </summary>
    [RuntimeOptions("Database", isStatic: true, "Configuration options for the active database.")] // TOFIX ask jarkko why?
    public class DatabaseOptions : RuntimeOptionsBase
    {
        public static readonly string[] SupportedMySqlVersions  = new string[] { "5.7", "8.0" }; // \todo [petri] remove support for 5.7 in R20
        public const string             DefaultMySqlVersion     = "8.0"; // Default MySQL version -- this variable is used when invoking `dotnet ef migrations` with a MySQL backend (must be const because the RuntimeOptions system aren't initialized)

        [MetaDescription("The database backend to use (`Sqlite` or `MySql`).")]
        public DatabaseBackendType  Backend                 { get; private set; } = IsLocalEnvironment ? DatabaseBackendType.Sqlite : DatabaseBackendType.MySql;
        [MetaDescription("List of configurations for the database shards.")]
        public DatabaseShardSpec[]  Shards                  { get; private set; } = null;
        [MetaDescription("Specifies how many of the database shards (defined in `Shards`) are active. When re-sharding the database it is allowable for this value to be lower than the number of shards configured in `Shards`.")]
        public int                  NumActiveShards         { get; private set; } = IsLocalEnvironment ? 4 : 1;

        // \note Limit the number of simultaneous database connections in development environments.
        // Low-volume environments should not require many connections -- if this isn't enough, there
        // is likely a deeper underlying problem which needs to be investigated (instead of increasing
        // the limit)!
        [MetaDescription("If 'Backend' is 'MySql': The maximum number of connections to each database shard.")]
        public int                  MaxConnectionsPerShard  { get; private set; } = IsDevelopmentEnvironment ? 5 : 100;
        [MetaDescription("If `Backend` is `MySql`: The MySql version to use.")]
        public string               MySqlVersion            { get; private set; } = DefaultMySqlVersion;
        [MetaDescription("If `Backend` is `MySql`: Enable verbose logging.")]
        public bool                 EnableMySqlLogging      { get; private set; } = false;

        [MetaDescription("If `Backend` is `Sqlite`: Use an in-memory database. Useful for locally running lots of bots without configuring MySQL.")]
        public bool                 SqliteInMemory          { get; private set; } = false;

        [MetaDescription("If `Backend` is `Sqlite` and `Shards` not explicitly configured: Directory for sqlite database shards")]
        public string               SqliteDirectory         { get; private set; } = "bin";

        [MetaDescription("Required: The master version of the database. Avoid changing after your game is launched.")]
        public int                  MasterVersion           { get; private set; } = -1;
        [MetaDescription("When enabled, permanently delete and reinitialize the database on `MasterVersion` mismatch. _Always disable in production!_")]
        public bool                 NukeOnVersionMismatch   { get; private set; } = !IsStagingEnvironment && !IsProductionEnvironment;

        [MetaDescription("The maximum number of `Low` priority queries allowed per database shard.")]
        public int                  MaxConnectionsLowPriority    { get; private set; } = 4;
        [MetaDescription("The maximum number of `Lowest` priority queries allowed per database shard.")]
        public int                  MaxConnectionsLowestPriority { get; private set; } = 1;

        [MetaDescription("The preferred compression algorithm to use when persisting objects in the database. Supports `None`, `LZ4` and `Zstandard`.")]
        public CompressionAlgorithm CompressionAlgorithm    { get; private set; } = CompressionAlgorithm.Zstandard;

        public override Task OnLoadedAsync()
        {
            if (!Array.Exists(SupportedMySqlVersions, vsn => vsn == MySqlVersion))
                throw new InvalidOperationException($"Invalid MySqlVersion '{MySqlVersion}', supported values are: {string.Join(", ", SupportedMySqlVersions)}");

            if (MaxConnectionsPerShard <= 0)
                throw new InvalidOperationException($"{nameof(MaxConnectionsPerShard)} must be at least 1");

            // If Shards not specified, populate default shard definitions for local use.
            if (Shards == null)
            {
                // \note Always populate at least 4 shards to allow re-sharding to work
                string databaseNamePrefix = MetaplayCore.Options.ProjectName.ToLowerInvariant();
                Shards =
                    Enumerable.Range(0, Math.Max(4, NumActiveShards))
                    .Select(shardNdx => new DatabaseShardSpec
                    {
                        DatabaseName  = Invariant($"{databaseNamePrefix}_{shardNdx}"),
                        ReadWriteHost = "localhost",
                        ReadOnlyHost  = "localhost",
                        FilePath      = Invariant($"{SqliteDirectory}/Shardy-{shardNdx}.db"),

                        UserId        = "metaplay",
                        Password      = "s3cret",
                    })
                    .ToArray();
            }

            if (CompressionAlgorithm != CompressionAlgorithm.None && CompressionAlgorithm != CompressionAlgorithm.LZ4 && CompressionAlgorithm != CompressionAlgorithm.Zstandard)
                throw new InvalidOperationException($"CompressionAlgorithm.{CompressionAlgorithm} is not supported for persisting in database");

            return Task.CompletedTask;
        }
    }
}
