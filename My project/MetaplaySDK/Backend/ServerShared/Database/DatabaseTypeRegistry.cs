// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Metaplay.Server.Database
{
    /// <summary>
    /// Information about each persisted item stored in the database. One entry corresponds to each table in the database.
    /// </summary>
    public class DatabaseItemSpec
    {
        public readonly Type    ItemType;                       // Type of the item
        public readonly string  TableName;                      // Name of the table in database
        public readonly string  PrimaryKeyName;                 // Member and column name of the primary key (used for finding and equality)
        public readonly bool    IsPartitioned;                  // Are the items split over multiple partitions/shards?
        public readonly string  PartitionKeyName;               // Member and column name used for deciding which shard the item should be on

        public readonly string  MemberNamesStr;                 // Comma-separated list of member/column names

        public readonly string  GetQuery;                       // SELECT * FROM <tableName> WHERE <Key> = @Key
        public readonly string  ExistsQuery;                    // SELECT 1 FROM <tableName> WHERE <Key> = @Key
        public readonly string  InsertQuery;                    // INSERT INTO <tableName> (Member1, ..) VALUES (@Member1, ..)
        public readonly string  InsertOrIgnoreQuery;            // INSERT OR IGNORE INTO <tableName> (Member1, ..) VALUES (@Member1, ..)
        public readonly string  InsertOrUpdateQuery;            // INSERT INTO <tableName> (Member1, ..) VALUES (@Member1, ..) ON CONFLICT UPDATE VALUES
        public readonly string  UpdateQuery;                    // UPDATE <tableName> SET Member1 = @Member1, .. WHERE <MemberKey> = @MemberKey
        public readonly string  RemoveQuery;                    // DELETE FROM <tableName> WHERE <Key> = @Key
        public readonly string  PagedQuery;                     // SELECT * FROM <tableName> WHERE <Key> > @StartKeyExclusive ORDER BY <Key> LIMIT @PageSize
        public readonly string  PagedRangeInclusiveStartQuery;  // SELECT * FROM <tableName> WHERE <Key> >= @StartKeyInclusive AND <Key> <= @LastKeyInclusive ORDER BY <Key> LIMIT @PageSize
        public readonly string  PagedRangeExclusiveStartQuery;  // SELECT * FROM <tableName> WHERE <Key> > @StartKeyExclusive AND <Key> <= @LastKeyInclusive ORDER BY <Key> LIMIT @PageSize

        public bool HasPrimaryKey => PrimaryKeyName != null;

        public readonly Func<IPersistedItem, string>        GetItemPrimaryKey;
        public readonly Func<IPersistedItem, (string, int)> GetItemShardNdx;
        public readonly Func<string, int>                   GetKeyShardNdx;

        public DatabaseItemSpec(DatabaseBackend db, string tableName, Type itemType) //, string tableName, string primaryKeyName, bool isPartitioned, string partitionKeyName, string getQuery, string insertQuery, string insertOrIgnoreQuery, string updateQuery, string removeQuery, string pagedQuery, string pagedRangeInclusiveStartQuery, string pagedRangeExclusiveStartQuery)
        {
            bool        hasPrimaryKey       = itemType.GetCustomAttribute<NoPrimaryKeyAttribute>() == null;
            bool        isPartitioned       = itemType.GetCustomAttribute<NonPartitionedAttribute>() == null;
            string      primaryKeyName      = hasPrimaryKey ? GetPersistedItemPrimaryKey(itemType).Name : null;
            string      partitionKeyName    = isPartitioned ? GetPersistedItemPartitionKey(itemType).Name : null;
            string[]    memberNames         = GetPersistedItemMemberNames(itemType);

            MemberNamesStr = string.Join(", ", memberNames);
            string valuesStr    = string.Join(", ", memberNames.Select(name => $"@{name}"));
            string setStr       = string.Join(", ", memberNames.Where(name => name != primaryKeyName).Select(name => $"{name} = @{name}"));

            ItemType                        = itemType;
            TableName                       = tableName;
            PrimaryKeyName                  = primaryKeyName;
            IsPartitioned                   = isPartitioned;
            PartitionKeyName                = partitionKeyName;
            GetQuery                        = hasPrimaryKey ? $"SELECT * FROM {tableName} WHERE {primaryKeyName} = @Key" : null;
            ExistsQuery                     = hasPrimaryKey ? $"SELECT 1 FROM {tableName} WHERE {primaryKeyName} = @Key" : null;
            InsertQuery                     = $"INSERT INTO {tableName} ({MemberNamesStr}) VALUES ({valuesStr})";
            InsertOrIgnoreQuery             = $"{db.GetInsertOrIgnoreSql()} INTO {tableName} ({MemberNamesStr}) VALUES ({valuesStr})";
            InsertOrUpdateQuery             = hasPrimaryKey ? $"INSERT INTO {tableName} ({MemberNamesStr}) VALUES ({valuesStr}) {db.GetInsertOnConflictSetSql(primaryKeyName, setStr)}" : null;
            UpdateQuery                     = hasPrimaryKey ? $"UPDATE {tableName} SET {setStr} WHERE {primaryKeyName} = @{primaryKeyName}" : null;
            RemoveQuery                     = hasPrimaryKey ? $"DELETE FROM {tableName} WHERE {primaryKeyName} = @Key" : null;
            PagedQuery                      = hasPrimaryKey ? $"SELECT * FROM {tableName} WHERE {primaryKeyName} > @StartKeyExclusive ORDER BY {primaryKeyName} LIMIT @PageSize" : null;
            PagedRangeInclusiveStartQuery   = hasPrimaryKey ? $"SELECT * FROM {tableName} WHERE {primaryKeyName} >= @StartKeyInclusive AND {primaryKeyName} <= @LastKeyInclusive ORDER BY {primaryKeyName} LIMIT @PageSize" : null;
            PagedRangeExclusiveStartQuery   = hasPrimaryKey ? $"SELECT * FROM {tableName} WHERE {primaryKeyName} > @StartKeyExclusive AND {primaryKeyName} <= @LastKeyInclusive ORDER BY {primaryKeyName} LIMIT @PageSize" : null;

            GetItemPrimaryKey   = HasPrimaryKey ? GetKeyGetter(itemType, primaryKeyName) : null;
            GetItemShardNdx     = IsPartitioned ? GetItemShardNdxGetter(GetKeyGetter(itemType, partitionKeyName), db.GetShardIndex) : (IPersistedItem item) => ("not-partitioned", 0);
            GetKeyShardNdx      = IsPartitioned ? GetKeyShardNdxGetter(db.GetShardIndex) : (string key) => 0;
        }

        Func<IPersistedItem, string> GetKeyGetter(Type itemType, string keyName)
        {
            MemberInfo[] members = itemType.GetMember(keyName);
            if (members.Length == 0)
                throw new InvalidOperationException($"No such key {itemType.Name}.{keyName} found!");

            MemberInfo member = members[0];
            if (member is PropertyInfo propInfo)
                return (IPersistedItem item) => (string)propInfo.GetValue(item);
            else if (member is FieldInfo fieldInfo)
                return (IPersistedItem item) => (string)fieldInfo.GetValue(item);
            else
                throw new InvalidOperationException($"Invalid type of {itemType.Name}.{keyName}");
        }

        Func<IPersistedItem, (string, int)> GetItemShardNdxGetter(Func<IPersistedItem, string> getKey, Func<string, int> getShardIndex)
        {
            return (IPersistedItem item) =>
            {
                string partitionKey = getKey(item);
                int shardNdx        = getShardIndex(partitionKey);
                return (partitionKey, shardNdx);
            };
        }

        Func<string, int> GetKeyShardNdxGetter(Func<string, int> getShardIndex)
        {
            return (string key) => getShardIndex(key);
        }

        MemberInfo GetPersistedItemPrimaryKey(Type itemType)
        {
            MemberInfo memberInfo = itemType.GetMembers().SingleOrDefault(member => member.GetCustomAttribute<KeyAttribute>() != null);
            if (memberInfo == null)
                throw new InvalidOperationException($"Type {itemType.ToGenericTypeString()} doesn't specify any member as the primary key (using [Key] attribute)");
            return memberInfo;
        }

        MemberInfo GetPersistedItemPartitionKey(Type itemType)
        {
            MemberInfo memberInfo = itemType.GetMembers().SingleOrDefault(member => member.GetCustomAttribute<PartitionKeyAttribute>() != null);
            if (memberInfo == null)
                throw new InvalidOperationException($"Type {itemType.ToGenericTypeString()} doesn't specify any member as the partition key (using [PartitionKey] attribute)");
            return memberInfo;
        }

        string[] GetPersistedItemMemberNames(Type itemType)
        {
            // \todo [petri] these will not work properly with private members in derived classes,
            //               PR #135 is adding GetFieldsOfTypeAndAncestors() that handles them correctly
            //               -> migrate to use them when available

            IEnumerable<string> propNames =
                itemType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(prop => prop.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
                .Where(prop => prop.GetSetMethod() != null)
                .Select(prop => prop.Name);

            IEnumerable<string> fieldNames =
                itemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
                .Select(field => field.Name);

            return propNames.Concat(fieldNames).ToArray();
        }
    }


    /// <summary>
    /// Sharded database interface.
    ///
    /// Persists information int MySQL / SQLite database using Entity Framework Core. Uses multiple
    /// SQL databases via sharding.
    ///
    /// When modifying the schema for the database, a new migration needs to be created.
    /// <c>Server/ServerMain$ dotnet ef migrations add SomeMigration</c>
    ///
    /// The migration code is run automatically when starting the server. It is executed against all
    /// database shards.
    /// </summary>
    public static class DatabaseTypeRegistry
    {
        static OrderedDictionary<Type, DatabaseItemSpec> _concreteItemSpecs;
        static OrderedDictionary<Type, DatabaseItemSpec> _virtualItemSpecs;

        public static IEnumerable<DatabaseItemSpec> ItemSpecs => _concreteItemSpecs.Values;

        public static void Initialize(DatabaseBackend database, Type contextType)
        {
            if (_concreteItemSpecs != null)
                throw new InvalidOperationException($"DatabaseTypeRegistry already initialized");
            (_concreteItemSpecs, _virtualItemSpecs) = ResolveItemSpecs(database, contextType);
        }

        public static IEnumerable<(Type, string)> ResolveEntityToDatabaseTableMap(Type dbContextType)
        {
            // Discover DB types that are statically declared as properties of the DB context class (pre-r19)
            HashSet<Type> staticallyDefinedEntities = new HashSet<Type>();
            for (Type contextType = dbContextType; contextType != null; contextType = contextType.BaseType)
            {
                foreach (PropertyInfo prop in contextType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(prop => prop.Name, StringComparer.Ordinal))
                {
                    Type propType = prop.PropertyType;
                    if (!propType.IsGenericTypeOf(typeof(DbSet<>)))
                        continue;

                    // Check if mapping is enabled
                    if (!prop.IsMetaFeatureEnabled())
                        continue;

                    Type itemType = propType.GetGenericArguments()[0];
                    yield return (itemType, prop.Name);
                    staticallyDefinedEntities.Add(itemType);
                }
            }

            // Scan IPersistedItems
            HashSet<Type> integrationTypesHandled = new HashSet<Type>();
            foreach (Type t in TypeScanner.GetInterfaceImplementations<IPersistedItem>())
            {
                Type concreteType;
                string tableName;
                if (t.ImplementsGenericInterface(typeof(IMetaIntegration<>)))
                {
                    Type apiType = t.GetGenericInterfaceTypeArguments(typeof(IMetaIntegration<>))[0];
                    if (integrationTypesHandled.Contains(apiType))
                        continue;
                    integrationTypesHandled.Add(apiType);

                    if (!apiType.IsMetaFeatureEnabled())
                        continue;
                    // \note: currently expecting IPersistedItems to be constructible, this might be
                    // relaxed in the future (potentially no need for default-constructing).
                    concreteType = IntegrationRegistry.GetSingleIntegrationType(apiType);
                    tableName = apiType.GetCustomAttribute<TableAttribute>()?.Name;
                }
                else if (t.IsAbstract || !t.IsMetaFeatureEnabled())
                {
                    continue;
                }
                else
                {
                    concreteType = t;
                    tableName = t.GetCustomAttribute<TableAttribute>()?.Name;
                }

                if (staticallyDefinedEntities.Contains(concreteType))
                {
                    // DbContext entries can override discovered types, but type must not declare table name
                    if (tableName != null)
                        throw new InvalidOperationException($"DB entity mapped both in DB context and via Table attribute: {concreteType}");
                    continue;
                }
                if (tableName == null)
                {
                    throw new InvalidOperationException($"Missing 'Table' attribute for PersistedItem {concreteType}");
                }

                yield return (concreteType, tableName);
            }
        }

        static (OrderedDictionary<Type, DatabaseItemSpec>, OrderedDictionary<Type, DatabaseItemSpec>) ResolveItemSpecs(DatabaseBackend database, Type concreteContextType)
        {
            OrderedDictionary<Type, DatabaseItemSpec> concreteItemSpecs = new OrderedDictionary<Type, DatabaseItemSpec>();
            OrderedDictionary<Type, DatabaseItemSpec> virtualItemSpecs = new OrderedDictionary<Type, DatabaseItemSpec>();

            // Resolve specs
            foreach ((Type itemType, string tableName) in ResolveEntityToDatabaseTableMap(concreteContextType))
            {
                DatabaseItemSpec itemSpec = new DatabaseItemSpec(database, tableName, itemType);

                // Register itemType itself as concreteSpec
                if (concreteItemSpecs.TryGetValue(itemType, out DatabaseItemSpec existingTableSpec))
                    throw new InvalidOperationException($"Ambiguous definitions for DbSet<{itemType.ToGenericTypeString()}>. Could be either {existingTableSpec.TableName} or {tableName}. Each table should use a unique PersistedType.");
                concreteItemSpecs.Add(itemType, itemSpec);

                // Register all base class types with [MetaPersistedVirtualItem] attribute as virtualSpecs
                for (Type type = itemType.BaseType; type != typeof(object); type = type.BaseType)
                {
                    if (type.GetCustomAttribute<MetaPersistedVirtualItemAttribute>(inherit: false) != null)
                    {
                        if (virtualItemSpecs.TryGetValue(type, out DatabaseItemSpec existingSpec))
                            throw new InvalidOperationException($"Ambiguous DbSet<> implementation for core {itemType.ToGenericTypeString()}. Could be DbSet<{existingSpec.ItemType.ToGenericTypeString()}> or DbSet<{type.ToGenericTypeString()}>. For custom tables, you should not inherit MetaplaySDK Persisted**Base types.");
                        virtualItemSpecs.Add(type, itemSpec);
                    }
                }
            }

            // Check that Context contains all needed items. We cannot know all usages, but certain cases can be checked.

            // PersistedActors must have their persisted types available in the database.
            // \note that we do the check here instead of PersistedActor(registry) due to both initialization order but also because the real name of TContext is available here.
            foreach ((Type entityConfigType, EntityConfigBase entityConfig) in EntityConfigRegistry.Instance.TypeToEntityConfig)
            {
                if (entityConfig is not PersistedEntityConfig persistedConfig)
                    continue;
                if (concreteItemSpecs.ContainsKey(persistedConfig.PersistedType))
                    continue;

                throw new InvalidOperationException(
                    $"Entity {persistedConfig.EntityActorType.ToGenericTypeString()} uses persisted type {persistedConfig.PersistedType.ToGenericTypeString()}, but no database table is declared for it in the {concreteContextType.ToGenericTypeString()}. "
                    + $"Add a DbSet<> property to {concreteContextType.ToGenericTypeString()} in GameDbContext.cs, for example 'DbSet<{persistedConfig.PersistedType.ToGenericTypeString()}> {persistedConfig.EntityKind}s {{ get; set }}'."
                    );
            }

            return (concreteItemSpecs, virtualItemSpecs);
        }

        public static DatabaseItemSpec GetItemSpec(Type type)
        {
            if (_concreteItemSpecs == null)
                throw new InvalidOperationException($"DatabaseTypeRegistry not initialized");
            if (_concreteItemSpecs.TryGetValue(type, out DatabaseItemSpec concreteItemSpec))
                return concreteItemSpec;
            if (_virtualItemSpecs.TryGetValue(type, out DatabaseItemSpec virtualItemSpec))
                return virtualItemSpec;
            throw new KeyNotFoundException($"No DatabaseItemSpec for type {type.ToGenericTypeString()}, make sure a matching DbSet<> exists in GameDbContext");
        }

        public static DatabaseItemSpec GetItemSpec<T>() => GetItemSpec(typeof(T));
    }
}
