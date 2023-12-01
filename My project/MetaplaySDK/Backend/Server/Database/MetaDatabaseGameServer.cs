// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Dapper;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Core.Web3;
using Metaplay.Server.EventLog;
#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Server.Guild;
#endif
using Metaplay.Server.InAppPurchase;
using Metaplay.Server.League;
using Metaplay.Server.Web3;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.Database
{
    /// <summary>
    /// Main interface for accessing the database. This class implements Metaplay core business level
    /// behavior for database access.
    /// </summary>
    /// <remarks>
    /// If you wish to add any game-specific database behavior, you can add it to the GameDatabase class,
    /// which is a similar clsas to this, except for game-specific methods.
    /// </remarks>
    /// <code>
    /// // Example usage:
    /// MetaDatabase db = MetaDatabase.Get(QueryPriority.Normal);
    /// await db.InsertASync(myPersistedItem);
    /// </code>
    public class MetaDatabase : MetaDatabaseBase
    {
        public MetaDatabase()
        {
        }

        public static new MetaDatabase Get(QueryPriority priority = QueryPriority.Normal)
        {
            return Get<MetaDatabase>(priority);
        }

        #region Player

        public async Task UpdatePlayerAsync(EntityId playerId, PersistedPlayerBase player, IEnumerable<string> newPlayerSearchStrings)
        {
            // If search name doesn't need to be updated, do a regular Entity update
            if (newPlayerSearchStrings == null)
            {
                await UpdateAsync(player).ConfigureAwait(false);
                return;
            }

            // Name update is required, transactionally update the existing PersistedPlayer and the name search table
            DatabaseItemSpec playerItemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerBase>();
            DatabaseItemSpec searchItemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerSearch>();
            (string partitionKey, int shardNdx) = playerItemSpec.GetItemShardNdx(player);

            // \todo: Should check the pre-existence inside transaction, but that is difficult. Let's assume that
            //        removal of a player between these steps is unlikely.
            bool alreadyExists = await TestExistsAsync<PersistedPlayerBase>(playerId.ToString());
            if (!alreadyExists)
                throw new NoSuchItemException($"No item in table {playerItemSpec.TableName} shard #{shardNdx} with primary key {playerId} found");

            int _ = await _backend.DapperExecuteTransactionAsync(_throttle, DatabaseReplica.ReadWrite, IsolationLevel.ReadCommitted, shardNdx, "Player", "UpdateWithName", async (conn, txn) =>
            {
                // Start with the query and params to update the PersistedPlayer
                List<string> queries = new List<string> { playerItemSpec.UpdateQuery };
                DynamicParameters queryParams = new DynamicParameters(player);

                // \note Convert to lower case to make case insensitive
                IEnumerable<string> nameParts = newPlayerSearchStrings.Select(x => x.ToLowerInvariant());

                // Add the query for removing old name parts
                // \note Using _ prefix for non-PersistedPlayer params to avoid potential collisions
                queries.Add($"DELETE FROM {searchItemSpec.TableName} WHERE EntityId = @_EntityId");
                queryParams.Add("_EntityId", playerId.ToString());

                // Create insert query (with value for each name parts)
                if (nameParts.Count() > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append($"INSERT INTO {searchItemSpec.TableName} ({searchItemSpec.MemberNamesStr}) VALUES ");
                    int namePartNdx = 0;
                    foreach (string namePart in nameParts)
                    {
                        string paramName = Invariant($"_NamePart{namePartNdx}");
                        if (namePartNdx != 0)
                            sb.Append(", ");
                        sb.Append($"(@{paramName}, @_EntityId)");
                        queryParams.Add(paramName, namePart);
                        namePartNdx++;
                    }
                    queries.Add(sb.ToString());
                }

                // Execute all the ops with single round-trip
                string fullQuery = string.Join(";\n", queries);
                int numUpdated = await conn.ExecuteAsync(fullQuery, queryParams, transaction: txn).ConfigureAwait(false);

                // Commit transaction
                await txn.CommitAsync().ConfigureAwait(false);

                return numUpdated;
            }).ConfigureAwait(false);
        }

        async Task<List<string>> SearchPlayerIdsByNameFromShardAsync(int shardNdx, int count, string searchText)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerSearch>();
            string query = $"SELECT DISTINCT EntityId FROM {itemSpec.TableName} WHERE NamePart LIKE @SearchFilter ESCAPE '/' LIMIT @Count";
            IEnumerable<string> playerIds = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "SearchByName", conn =>
                conn.QueryAsync<string>(query, new {
                    SearchFilter    = SqlUtil.LikeQueryForPrefixSearch(searchText.ToLowerInvariant(), '/'), // \note: escape with / because \ constant (for ESCAPE '') is interpreted differently in across SQL engines.
                    Count           = count,
                }))
                .ConfigureAwait(false);
            return playerIds.ToList();
        }

        public async Task<List<EntityId>> SearchPlayerIdsByNameAsync(int count, string searchText)
        {
            // Find results from all shards (in parallel)
            List<string>[] results = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx => SearchPlayerIdsByNameFromShardAsync(shardNdx, count, searchText)))
                .ConfigureAwait(false);

            // Limit results to 'count'
            return results
                .SelectMany(v => v)
                .Take(count)
                .Select(playerId => EntityId.ParseFromString(playerId))
                .ToList();
        }

        #endregion // Player

        #region IAP

        /// <summary>
        /// Get metadatas of the IAP subscriptions with the given original transaction id.
        /// Note that although the result items are of type <see cref="PersistedInAppPurchaseSubscription"/>,
        /// only its "metadata" fields will be set; in particular,
        /// <see cref="PersistedInAppPurchaseSubscription.SubscriptionInfo"/> is omitted.
        /// </summary>
        /// <param name="originalTransactionId"></param>
        /// <returns></returns>
        public async Task<List<PersistedInAppPurchaseSubscription>> GetIAPSubscriptionMetadatas(string originalTransactionId)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedInAppPurchaseSubscription>();

            string[] memberNames = new string[]
            {
                nameof(PersistedInAppPurchaseSubscription.PlayerAndOriginalTransactionId),
                nameof(PersistedInAppPurchaseSubscription.PlayerId),
                nameof(PersistedInAppPurchaseSubscription.OriginalTransactionId),
                nameof(PersistedInAppPurchaseSubscription.CreatedAt),
            };
            string query = $"SELECT {string.Join(", ", memberNames)} FROM {itemSpec.TableName} WHERE {nameof(PersistedInAppPurchaseSubscription.OriginalTransactionId)} = @OriginalTransactionId";

            IEnumerable<PersistedInAppPurchaseSubscription>[] results = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx =>
                {
                    return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "GetIAPSubscriptionMetadatas", conn =>
                    {
                        return conn.QueryAsync<PersistedInAppPurchaseSubscription>(query, new
                        {
                            OriginalTransactionId = originalTransactionId,
                        });
                    });
                }));

            return results.SelectMany(l => l).ToList();
        }

        #endregion

        #region EntitySearch

        /// <summary>
        /// Search for at most <paramref name="count"/> entities whose EntityId starts with <paramref name="searchText"/>.
        /// Note that the 'EntityKind:' prefix is automatically added to the <paramref name="searchText"/> if it's not there already.
        /// If <paramref name="searchText"/> is empty or null, no filtering is done. Uses the read replicas. Search is case-sensitive.
        /// </summary>
        public async Task<List<TEntity>> SearchEntitiesByIdAsync<TEntity>(EntityKind entityKind, int count, string searchText) where TEntity : IPersistedEntity
        {
            List<IPersistedEntity> entities = await SearchEntitiesByIdAsync(typeof(TEntity), entityKind, count, searchText);
            return entities.Cast<TEntity>().ToList();
        }

        /// <inheritdoc cref="SearchEntitiesByIdAsync{TEntity}(EntityKind, int, string)" />
        public async Task<List<IPersistedEntity>> SearchEntitiesByIdAsync(Type persistedType, EntityKind entityKind, int count, string searchText)
        {
            DatabaseItemSpec    itemSpec    = DatabaseTypeRegistry.GetItemSpec(persistedType);
            string              where       = string.IsNullOrEmpty(searchText) ? "" : "WHERE EntityId LIKE @SearchFilter ESCAPE '/'";
            string              query       = $"SELECT * FROM {itemSpec.TableName} {where} LIMIT @Count";

            // Ensure that searchText starts with the 'EntityKind:' prefix (or is empty or null).
            if (!string.IsNullOrEmpty(searchText) && !searchText.StartsWith($"{entityKind}:", StringComparison.InvariantCultureIgnoreCase))
                searchText = $"{entityKind}:" + searchText;

            IEnumerable<IPersistedEntity>[] results = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(async shardNdx =>
                {
                    return await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "QueryById", async conn =>
                    {
                        IEnumerable<object> result = await conn.QueryAsync(itemSpec.ItemType, query, new
                        {
                            SearchFilter    = searchText == null ? null : SqlUtil.LikeQueryForPrefixSearch(searchText, '/'), // \note: escape with / because \ constant (for ESCAPE '') is interpreted differently in across SQL engines.
                            Count           = count,
                        }).ConfigureAwait(false);
                        return result.Cast<IPersistedEntity>();
                    });
                }));

            // Choose 'count' results from all queries
            return results
                .SelectMany(v => v)
                .Take(count)
                .ToList();
        }

        #endregion // EntitySearch

        #region Guild
        #if !METAPLAY_DISABLE_GUILDS

        // \todo [petri] this is mostly duplicate with UpdatePlayerAsync() -- refactor common parts
        public async Task UpdateGuildAsync(EntityId guildId, PersistedGuildBase guild, string updateNameSearch)
        {
            // If search name doesn't need to be updated, do a regular Entity update
            if (updateNameSearch == null)
            {
                await UpdateAsync(guild).ConfigureAwait(false);
                return;
            }

            // Name update is required, transactionally update the existing PersistedGuild and the name search table
            DatabaseItemSpec guildItemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedGuildBase>();
            DatabaseItemSpec searchItemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedGuildSearch>();
            (string partitionKey, int shardNdx) = guildItemSpec.GetItemShardNdx(guild);
            int _ = await _backend.DapperExecuteTransactionAsync(_throttle, DatabaseReplica.ReadWrite, IsolationLevel.ReadCommitted, shardNdx, "Guild", "UpdateWithName", async (conn, txn) =>
            {
                // Start with the query and params to update the PersistedGuild
                List<string> queries = new List<string> { guildItemSpec.UpdateQuery };
                DynamicParameters queryParams = new DynamicParameters(guild);

                // If name search should be updated, add the queries and parameters
                if (updateNameSearch != null)
                {
                    // Compute all the valid name parts of the guild's name
                    // \note Convert to lower case to make case insensitive
                    List<string> nameParts = SearchUtil.ComputeSearchablePartsFromName(updateNameSearch.ToLowerInvariant(), PersistedGuildSearch.MinPartLengthCodepoints, PersistedGuildSearch.MaxPartLengthCodepoints, PersistedGuildSearch.MaxPersistNameParts);

                    // Add the query for removing old name parts
                    // \note Using _ prefix for non-PersistedGuild params to avoid potential collisions
                    queries.Add($"DELETE FROM {searchItemSpec.TableName} WHERE EntityId = @_EntityId");
                    queryParams.Add("_EntityId", guildId.ToString());

                    // Create insert query (with value for each part)
                    if (nameParts.Count > 0)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append($"INSERT INTO {searchItemSpec.TableName} ({searchItemSpec.MemberNamesStr}) VALUES ");
                        for (int ndx = 0; ndx < nameParts.Count; ndx++)
                        {
                            string paramName = Invariant($"_NamePart{ndx}");
                            if (ndx != 0)
                                sb.Append(", ");
                            sb.Append($"(@{paramName}, @_EntityId)");
                            queryParams.Add(paramName, nameParts[ndx]);
                        }
                        queries.Add(sb.ToString());
                    }
                }

                // _backend.Execute all the queries with single round-trip
                string fullQuery = string.Join(";\n", queries);
                int numUpdated = await conn.ExecuteAsync(fullQuery, queryParams, transaction: txn).ConfigureAwait(false);

                // Commit transaction
                await txn.CommitAsync().ConfigureAwait(false);

                return numUpdated;
            }).ConfigureAwait(false);
        }

        async Task<List<string>> SearchGuildIdsByNameFromShardAsync(int shardNdx, int count, string searchText)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedGuildSearch>();
            string query = $"SELECT DISTINCT EntityId FROM {itemSpec.TableName} WHERE NamePart LIKE @SearchFilter ESCAPE '/' LIMIT @Count";
            IEnumerable<string> guildIds = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "SearchByName", conn =>
                conn.QueryAsync<string>(query, new {
                    SearchFilter    = SqlUtil.LikeQueryForPrefixSearch(searchText.ToLowerInvariant(), '/'), // \note: escape with / because \ constant (for ESCAPE '') is interpreted differently in across SQL engines.
                    Count           = count,
                }))
                .ConfigureAwait(false);
            return guildIds.ToList();
        }

        // \todo [petri] code is mostly duplicated with player search -- refactor shared parts
        public async Task<List<EntityId>> SearchGuildIdsByNameAsync(int count, string searchText)
        {
            // Find results from all shards (in parallel)
            List<string>[] results = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx => SearchGuildIdsByNameFromShardAsync(shardNdx, count, searchText)))
                .ConfigureAwait(false);

            // Limit results to 'count'
            return results
                .SelectMany(v => v)
                .Take(count)
                .Select(guildId => EntityId.ParseFromString(guildId))
                .ToList();
        }

        #endif
        #endregion // Guild

        #region PlayerIncidents

        // \todo [petri] support paging
        public async Task<List<PlayerIncidentHeader>> QueryPlayerIncidentHeadersAsync(EntityId playerId, int pageSize)
        {
            // Stored on same shard as owning player
            int shardNdx = _backend.GetShardIndex(playerId.ToString());

            // Query the headers
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerIncident>();
            string queryFields = string.Join(", ", PersistedPlayerIncident.HeaderMemberNames);
            string query = $"SELECT {queryFields} FROM {itemSpec.TableName} WHERE PlayerId = @PlayerId ORDER BY IncidentId DESC LIMIT @PageSize";
            IEnumerable<PersistedPlayerIncident> result = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "QueryPaged", conn =>
                conn.QueryAsync<PersistedPlayerIncident>(query, new
                {
                    PlayerId = playerId.ToString(),
                    PageSize = pageSize,
                })).ConfigureAwait(false);

            // Convert to headers
            return result.Select(persisted => persisted.ToHeader()).ToList();
        }

        async Task<List<PlayerIncidentHeader>> QueryGlobalPlayerIncidentHeadersFromShardAsync(int shardNdx, string fingerprint, int pageSize)
        {
            // Query the headers
            DatabaseItemSpec itemType = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerIncident>();
            string whereClause = string.IsNullOrEmpty(fingerprint) ? "" : "WHERE FingerPrint = @Fingerprint";
            string queryFields = string.Join(", ", PersistedPlayerIncident.HeaderMemberNames);
            string query = $"SELECT {queryFields} FROM {itemType.TableName} {whereClause} ORDER BY PersistedAt DESC LIMIT @PageSize";
            IEnumerable<PersistedPlayerIncident> result = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemType.TableName, shardNdx, "QueryPaged", conn =>
                conn.QueryAsync<PersistedPlayerIncident>(query, new
                {
                    PageSize    = pageSize,
                    Fingerprint = fingerprint,
                })).ConfigureAwait(false);

            // Convert to headers
            return result.Select(persisted => persisted.ToHeader()).ToList();
        }

        /// <summary>
        /// Query latest player incident reports globally (from all players). Can query all types of incidents (fingerprint == null)
        /// or of specific type (fingerprint is valid).
        /// </summary>
        /// <param name="fingerprint">Optional type of incident to query (null means all types are included)</param>
        /// <param name="count">Maximum number of incidents to retur</param>
        /// <returns></returns>
        public async Task<List<PlayerIncidentHeader>> QueryGlobalPlayerIncidentHeadersAsync(string fingerprint, int count)
        {
            List<PlayerIncidentHeader>[] shardResults = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx => QueryGlobalPlayerIncidentHeadersFromShardAsync(shardNdx, fingerprint, count)))
                .ConfigureAwait(false);

            // Combine results into one, sort by occurredAt, and return pageSize of them
            // \todo [petri] would it be better to order by persistedAt ?
            return
                shardResults
                .SelectMany(v => v)
                .OrderByDescending(header => header.OccurredAt)
                .Take(count)
                .ToList();
        }

        async Task<List<PlayerIncidentStatistics>> QueryGlobalPlayerIncidentsStatisticsFromShardAsync(int shardNdx, string desiredStartIncidentId, int queryLimit)
        {
            DatabaseItemSpec itemType = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerIncident>();

            // We may group-by only queryLimit items. So group-by the range ( max(startId, id-queryLimit-records-ago) ... now)
            string limitQuery = $"SELECT IncidentId FROM {itemType.TableName} ORDER BY IncidentId DESC LIMIT 1 OFFSET @QueryLimit";
            string limitQueryResult = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemType.TableName, shardNdx, "QueryPaged", conn =>
                conn.QuerySingleOrDefaultAsync<string>(limitQuery, new
                {
                    QueryLimit = queryLimit - 1, // query is inclusive, so lets go back only N-1 steps.
                })).ConfigureAwait(false);

            bool isQuerySizeLimited;
            if (limitQueryResult == null)
            {
                // query limit cannot be exceeded. No that many records.
                isQuerySizeLimited = false;
            }
            else if (string.CompareOrdinal(desiredStartIncidentId, limitQueryResult) >= 0)
            {
                // desiredStartIncidentId is more recent or equal
                isQuerySizeLimited = false;
            }
            else
            {
                // limitQueryResult is more recent
                isQuerySizeLimited = true;
            }

            // Query the headers with the range
            string startIncidentId = isQuerySizeLimited ? limitQueryResult : desiredStartIncidentId;
            string query = $"SELECT Fingerprint, MAX(Type) AS Type, MAX(SubType) AS SubType, MAX(Reason) AS Reason, COUNT(*) AS Count FROM {itemType.TableName} WHERE IncidentId >= @StartIncidentId GROUP BY Fingerprint";
            IEnumerable<PlayerIncidentStatistics> result = await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemType.TableName, shardNdx, "QueryPaged", conn =>
                conn.QueryAsync<PlayerIncidentStatistics>(query, new
                {
                    StartIncidentId = startIncidentId,
                })).ConfigureAwait(false);

            List<PlayerIncidentStatistics> resultList = result.ToList();

            // If query hit the query limit, mark all groups in this query as CountIsLimitedByQuerySize
            if (isQuerySizeLimited)
            {
                foreach (PlayerIncidentStatistics group in resultList)
                    group.CountIsLimitedByQuerySize = true;
            }

            return resultList;
        }

        public async Task<List<PlayerIncidentStatistics>> QueryGlobalPlayerIncidentsStatisticsAsync(MetaTime since, int queryLimitPerShard)
        {
            // Resolve start incidentId from timestamp
            string startIncidentId = PlayerIncidentUtil.EncodeIncidentId(since, 0);

            // Fetch stats from all shards
            List<PlayerIncidentStatistics>[] shardResults = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx => QueryGlobalPlayerIncidentsStatisticsFromShardAsync(shardNdx, startIncidentId, queryLimitPerShard)))
                .ConfigureAwait(false);

            // Aggregate results from all shards together
            return
                shardResults
                .SelectMany(v => v)
                .GroupBy(v => v.Fingerprint)
                .Select(group =>
                {
                    PlayerIncidentStatistics elem = group.First();
                    bool countIsLimited = group.Any(e => e.CountIsLimitedByQuerySize);
                    return new PlayerIncidentStatistics(elem.Fingerprint, elem.Type, elem.SubType, elem.Reason, group.Sum(c => c.Count), countIsLimited);
                })
                .OrderByDescending(s => s.Count)
                .ToList();
        }

        async Task<int> PurgePlayerIncidentsFromShardAsync(int shardNdx, DateTime removeUntil, int limit)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerIncident>();
            if (_backend.SupportsDeleteOrderBy())
            {
                // For MySQL, only delete <limit> latest incidents, to avoid timeouts in case a large number of reports have accumulated
                string query = $"DELETE FROM {itemSpec.TableName} WHERE PersistedAt < @RemoveUntil ORDER BY PersistedAt LIMIT @Limit";
                return await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "DeleteBulk", conn =>
                    conn.ExecuteAsync(query, new { RemoveUntil = removeUntil, Limit = limit }))
                    .ConfigureAwait(false);
            }
            else
            {
                // SQLite doesn't support ORDER BY or LIMIT in DELETE, so delete all obsolete reports -- in SQLite environments, we don't expect a lot of build-up anyway
                string query = $"DELETE FROM {itemSpec.TableName} WHERE PersistedAt < @RemoveUntil";
                return await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "DeleteBulk", conn =>
                    conn.ExecuteAsync(query, new { RemoveUntil = removeUntil }))
                    .ConfigureAwait(false);
            }
        }

        public async Task<int> PurgePlayerIncidentsAsync(DateTime removeUntil, int perShardLimit)
        {
            if (perShardLimit <= 0)
                throw new ArgumentException("Per-shard purge limit must be positive", nameof(perShardLimit));

            // Fetch stats from all shards
            IEnumerable<int> counts = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx => PurgePlayerIncidentsFromShardAsync(shardNdx, removeUntil, perShardLimit)))
                .ConfigureAwait(false);

            return counts.Sum();
        }

        public async Task<PersistedPlayerIncident> TryGetIncidentReportAsync(EntityId playerId, string incidentId)
        {
            int shardNdx = _backend.GetShardIndex(playerId.ToString());
            DatabaseItemSpec itemType = DatabaseTypeRegistry.GetItemSpec<PersistedPlayerIncident>();
            return await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemType.TableName, shardNdx, "Get", conn =>
                conn.QuerySingleOrDefaultAsync<PersistedPlayerIncident>(itemType.GetQuery, new { Key = incidentId }))
                .ConfigureAwait(false);
        }

        #endregion // PlayerIncidents

        public async Task<IEnumerable<PersistedLocalizations>> QueryAllLocalizations(bool ShowArchived)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedLocalizations>();
            // \todo antti: hardcode the string or make it possible to provide per-item custom specs
            string queryFields = string.Join(',', itemSpec.MemberNamesStr.Split(',', StringSplitOptions.TrimEntries).Where(x => x != "ArchiveBytes"));
            string whereClause = ShowArchived ? "" : "WHERE IsArchived == 0";
            string query       = $"SELECT {queryFields} FROM {itemSpec.TableName} {whereClause} ORDER BY Id DESC";
            return await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, 0, "QueryAllLocalizations", conn =>
                conn.QueryAsync<PersistedLocalizations>(query)).ConfigureAwait(false);
        }

        #region GameConfigs
        public async Task<IEnumerable<PersistedStaticGameConfig>> QueryAllStaticGameConfigs(bool ShowArchived)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedStaticGameConfig>();
            // \todo antti: hardcode the string or make it possible to provide per-item custom specs
            string queryFields = string.Join(',', itemSpec.MemberNamesStr.Split(',', StringSplitOptions.TrimEntries).Where(x => x != "ArchiveBytes"));
            string whereClause = ShowArchived ? "" : "WHERE IsArchived == 0";
            string query = $"SELECT {queryFields} FROM {itemSpec.TableName} {whereClause} ORDER BY Id DESC";
            return await _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, 0, "QueryAllStaticGameConfigs", conn =>
                conn.QueryAsync<PersistedStaticGameConfig>(query)).ConfigureAwait(false);
        }

        public async Task<MetaGuid> SearchGameDataByVersionHashAndSource<TPersistedDataType>(string versionHash, string source) where TPersistedDataType : PersistedGameData
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<TPersistedDataType>();
            string           query    = $"SELECT {nameof(PersistedGameData.Id)} FROM {itemSpec.TableName} where {nameof(PersistedGameData.VersionHash)} = @VersionHash AND {nameof(PersistedGameData.Source)} = @Source ORDER BY {nameof(PersistedGameData.Id)} Desc";
            string idMaybe = await _backend.DapperExecuteAsync(
                _throttle,
                DatabaseReplica.ReadWrite,
                itemSpec.TableName,
                0,
                "SearchGameDataByVersionHashAndSource",
                conn =>
                    conn.QueryFirstOrDefaultAsync<string>(
                        query,
                        new
                        {
                            VersionHash = versionHash,
                            Source      = source
                        })).ConfigureAwait(false);
            return idMaybe != null ? MetaGuid.Parse(idMaybe) : MetaGuid.None;
        }

        #endregion

        #region Entity event log segments

        public Task<IEnumerable<TPersistedSegment>> GetAllEventLogSegmentsOfEntityAsync<TPersistedSegment>(EntityId ownerId) where TPersistedSegment : PersistedEntityEventLogSegment
        {
            DatabaseItemSpec    segmentItemSpec = DatabaseTypeRegistry.GetItemSpec<TPersistedSegment>();
            string              partitionKey    = PersistedEntityEventLogSegmentUtil.GetPartitionKey(ownerId);
            string              query           = $"SELECT * FROM {segmentItemSpec.TableName} WHERE {nameof(PersistedEntityEventLogSegment.OwnerId)} = @OwnerId";

            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, segmentItemSpec.TableName, partitionKey, "GetEventLog", conn =>
            {
                return conn.QueryAsync<TPersistedSegment>(query, new { OwnerId = ownerId.ToString() });
            });
        }

        public Task RemoveAllEventLogSegmentsOfEntityAsync<TPersistedSegment>(EntityId ownerId) where TPersistedSegment : PersistedEntityEventLogSegment
        {
            DatabaseItemSpec    segmentItemSpec = DatabaseTypeRegistry.GetItemSpec<TPersistedSegment>();
            string              partitionKey    = PersistedEntityEventLogSegmentUtil.GetPartitionKey(ownerId);
            string              query           = $"DELETE FROM {segmentItemSpec.TableName} WHERE {nameof(PersistedEntityEventLogSegment.OwnerId)} = @OwnerId";

            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, segmentItemSpec.TableName, partitionKey, "RemoveEventLog", conn =>
            {
                return conn.ExecuteAsync(query, new { OwnerId = ownerId.ToString() });
            });
        }

        public Task<DateTime?> TryGetEventLogSegmentLastEntryTimestamp<TPersistedSegment>(string primaryKey, string partitionKey) where TPersistedSegment : PersistedEntityEventLogSegment
        {
            DatabaseItemSpec    segmentItemSpec = DatabaseTypeRegistry.GetItemSpec<TPersistedSegment>();
            string              query           = $"SELECT {nameof(PersistedEntityEventLogSegment.LastEntryTimestamp)} FROM {segmentItemSpec.TableName} WHERE {segmentItemSpec.PrimaryKeyName} = @Key";

            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, segmentItemSpec.TableName, partitionKey, "GetEventLogTimestamp", async conn =>
            {
                return await conn.QuerySingleOrDefaultAsync<DateTime?>(query, new { Key = primaryKey });
            });
        }

        #endregion

        #region NFTs

        public async Task<List<PersistedNft>> QueryNftsAsync(NftCollectionId collection, EntityId? owner)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedNft>();

            List<string> whereParts = new List<string>();
            if (collection != null)
                whereParts.Add("CollectionId = @CollectionId");
            if (owner.HasValue)
                whereParts.Add("OwnerEntityId = @OwnerEntityId");

            string query = $"SELECT * FROM {itemSpec.TableName}";
            if (whereParts.Count > 0)
                query += $" WHERE {string.Join(" AND ", whereParts)}";

            IEnumerable<PersistedNft>[] nfts = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx =>
                {
                    return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "QueryNfts", conn =>
                    {
                        return conn.QueryAsync<PersistedNft>(query, new
                        {
                            CollectionId = collection?.ToString(),
                            OwnerEntityId = owner?.ToString() ?? null,
                        });
                    });
                }))
                .ConfigureAwait(false);

            return nfts
                .SelectMany(l => l)
                .OrderBy(nft => nft.CollectionId, StringComparer.Ordinal)
                .ThenBy(nft => NftId.ParseFromString(nft.TokenId))
                .ToList();
        }

        public async Task<NftId?> TryGetMaxNftIdInCollectionAsync(NftCollectionId collectionId)
        {
            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedNft>();

            string query = $"SELECT TokenId FROM {itemSpec.TableName} WHERE CollectionId = @CollectionId ORDER BY StringComparableTokenIdEquivalent DESC LIMIT 1";

            PersistedNft[] shardNftMaybes = await Task.WhenAll(
                Enumerable.Range(0, NumActiveShards)
                .Select(shardNdx =>
                {
                    return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadWrite, itemSpec.TableName, shardNdx, "QueryNfts", conn =>
                    {
                        return conn.QuerySingleOrDefaultAsync<PersistedNft>(query, new
                        {
                            CollectionId = collectionId.ToString(),
                        });
                    });
                }))
                .ConfigureAwait(false);

            IEnumerable<NftId> nftIds =
                shardNftMaybes
                .Where(nftMaybe => nftMaybe != null)
                .Select(nft => NftId.ParseFromString(nft.TokenId));

            if (nftIds.Any())
                return nftIds.Max();
            else
                return null;
        }

        #endregion

        #region Leagues

        // \todo [Nomi] Hacking these in here but would be nice to use LINQ IQueryable provider for this instead of Dapper.
        public async Task<List<PersistedParticipantDivisionAssociation>> QueryLeagueParticipantsByDivision(EntityId divisionId)
        {
            if (!divisionId.IsValid)
                throw new ArgumentException("Given divisionId should be a valid EntityId.", nameof(divisionId));

            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedParticipantDivisionAssociation>();

            string query = $"SELECT * FROM {itemSpec.TableName} WHERE {nameof(PersistedParticipantDivisionAssociation.DivisionId)} = @DivisionId";


            IEnumerable<PersistedParticipantDivisionAssociation>[] participants = await Task.WhenAll(
                    Enumerable.Range(0, NumActiveShards)
                        .Select(shardNdx =>
                        {
                            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "LeaguesQueryDivisionParticipants", conn =>
                            {
                                return conn.QueryAsync<PersistedParticipantDivisionAssociation>(query, new
                                {
                                    DivisionId = divisionId.ToString(),
                                });
                            });
                        }))
                .ConfigureAwait(false);

            return participants
                .SelectMany(l => l)
                .ToList();
        }

        public async Task<int> CountLeagueParticipantsByDivision(EntityId divisionId)
        {
            if (!divisionId.IsValid)
                throw new ArgumentException("Given divisionId should be a valid EntityId.", nameof(divisionId));

            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedParticipantDivisionAssociation>();

            string query = $"SELECT COUNT(*) FROM {itemSpec.TableName} WHERE {nameof(PersistedParticipantDivisionAssociation.DivisionId)} = @DivisionId";


            int[] counts = await Task.WhenAll(
                    Enumerable.Range(0, NumActiveShards)
                        .Select(shardNdx =>
                        {
                            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "LeaguesCountDivisionParticipants", conn =>
                            {
                                return conn.QuerySingleAsync<int>(query, new
                                {
                                    DivisionId = divisionId.ToString(),
                                });
                            });
                        }))
                .ConfigureAwait(false);

            return counts.Sum();
        }
        
        /// <summary>
        /// Returns all participant associations in a league with a state revision greater than the given state revision.
        /// </summary>
        public async Task<List<PersistedParticipantDivisionAssociation>> QueryLeagueParticipantAssociationsByLatestKnownStateRevision(EntityId leagueId, int stateRevision)
        {
            if (!leagueId.IsValid)
                throw new ArgumentException("Given leagueId should be a valid EntityId.", nameof(leagueId));

            DatabaseItemSpec itemSpec = DatabaseTypeRegistry.GetItemSpec<PersistedParticipantDivisionAssociation>();

            string query = $"SELECT * FROM {itemSpec.TableName} WHERE {nameof(PersistedParticipantDivisionAssociation.LeagueId)} = @LeagueId AND {nameof(PersistedParticipantDivisionAssociation.LeagueStateRevision)} > @StateRevision";
            
            IEnumerable<PersistedParticipantDivisionAssociation>[] participants = await Task.WhenAll(
                    Enumerable.Range(0, NumActiveShards)
                        .Select(shardNdx =>
                        {
                            return _backend.DapperExecuteAsync(_throttle, DatabaseReplica.ReadOnly, itemSpec.TableName, shardNdx, "LeaguesQueryByStateRevision", conn =>
                            {
                                return conn.QueryAsync<PersistedParticipantDivisionAssociation>(query, new
                                {
                                    LeagueId = leagueId.ToString(),
                                    StateRevision = stateRevision,
                                });
                            });
                        }))
                .ConfigureAwait(false);

            return participants
                .SelectMany(l => l)
                .ToList();
        }

        #endregion
    }
}
