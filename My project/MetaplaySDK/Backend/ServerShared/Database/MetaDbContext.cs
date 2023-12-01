// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

namespace Metaplay.Server.Database
{
    /// <summary>
    /// Metaplay core EFCore database context. Used to declare the core database tables.
    /// Derive your game-specific <c>GameDbContext</c> from this.
    /// </summary>
    public class MetaDbContext : DbContext, IMetaIntegrationConstructible<MetaDbContext>
    {
        DatabaseBackendType _backend;       // Database backend
        string              _connString;    // Connection string to database

        public MetaDbContext()
        {
            // Default connection to SQLite to a dummy file, used by EFCore migration creation.
            _backend = DatabaseBackendType.Sqlite;
            _connString = "Data Source=./Dummy.db";
        }

        public void SetConnection(DatabaseBackendType backend, string connString)
        {
            _backend    = backend;
            _connString = connString;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Ignore mappings based on enabled features
            foreach (PropertyInfo prop in GetType().GetProperties())
            {
                if (!prop.IsMetaFeatureEnabled())
                {
                    if (!prop.PropertyType.IsGenericTypeOf(typeof(DbSet<>)))
                        throw new Exception("MetaplayFeatureEnabledConditionAttribute attribute only supported on DbSet<> properties!");

                    Type itemType = prop.PropertyType.GetGenericArguments()[0];
                    modelBuilder.Ignore(itemType);
                }
            }

            // Add dynamic entities from DatabaseTypeRegistry
            foreach ((Type itemType, string tableName) in DatabaseTypeRegistry.ResolveEntityToDatabaseTableMap(GetType()))
            {
                modelBuilder.Entity(itemType).ToTable(tableName);
            }

            // Apply some default column types based on CLR types,
            // also do some miscellaneous checking
            foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (IMutableProperty prop in entity.GetProperties())
                {
                    // Default all byte[] to use longblob to avoid 64kB limit on MySQL
                    if (prop.ClrType == typeof(byte[]))
                    {
                        if (prop.GetColumnType() == null)
                            prop.SetColumnType("longblob");
                    }

                    // NVARCHAR cannot be used
                    string columnType = prop.GetColumnType();
                    if (columnType != null && columnType.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"{entity.Name}.{prop.Name} uses NVARCHAR type -- use VARCHAR which is Unicode-compliant!");

                    // If there's a [MaxLength(N)] attribute, check that the column type matches it.
                    // \todo [nuutti] What's MaxLength _actually_ supposed to do?
                    MaxLengthAttribute maxLengthAttribute = prop.PropertyInfo.GetCustomAttribute<MaxLengthAttribute>();
                    if (maxLengthAttribute != null)
                    {
                        string maxLengthStr = maxLengthAttribute.Length.ToString(CultureInfo.InvariantCulture);
                        if (columnType != $"varchar({maxLengthStr})")
                            throw new InvalidOperationException($"{entity.Name}.{prop.Name} has [MaxLength({maxLengthStr})], so column type was expected to be varchar({maxLengthStr}), but it is {columnType}");
                    }
                }
            }

            base.OnModelCreating(modelBuilder);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            switch (_backend)
            {
                case DatabaseBackendType.Sqlite:
                    options.UseSqlite(_connString);
                    break;

                case DatabaseBackendType.MySql:
                    // When doing a `dotnet ef migrations` operation, RuntimeOptionsRegistry isn't initialized, so use DefaultMySqlVersion
                    DatabaseOptions dbOpts = RuntimeOptionsRegistry.Instance?.GetCurrent<DatabaseOptions>();
                    string mySqlVersion = dbOpts?.MySqlVersion ?? DatabaseOptions.DefaultMySqlVersion;
                    options.UseMySql(_connString, new MySqlServerVersion(mySqlVersion));
                    break;

                default:
                    throw new ArgumentException($"Unsupported backend {_backend}");
            }
        }
    }
}
