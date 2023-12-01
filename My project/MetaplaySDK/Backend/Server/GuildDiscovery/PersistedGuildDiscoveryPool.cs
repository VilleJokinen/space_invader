// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Cloud;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.GuildDiscovery
{
    /// <summary>
    /// A page (a subset of guilds) persisted in the database. The whole persisted data
    /// is constructed from these parts. To facilitate construction, a single page is dedicated
    /// as a Header that is used to find the rest of the pages. The page/header separation is
    /// implicitly encoded in PageId format.
    /// </summary>
    [GuildsEnabledCondition]
    [Table("GuildDiscoveryPoolPages")]
    public class PersistedGuildDiscoveryPoolPage : IPersistedItem
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(160)]
        [Column(TypeName = "varchar(160)")]
        public string   PageId          { get; set; }

        [Required]
        public int      SchemaVersion   { get; set; }

        [Required]
        public byte[]   Payload         { get; set; } // tagged-serialized GuildDiscoveryPoolPage or GuildDiscoveryPoolHeader
    }

    [MetaSerializable]
    public class GuildDiscoveryPoolHeader
    {
        [MetaMember(1)] public int Serial;
        [MetaMember(2)] public int NumPages;
    }

    [MetaSerializable]
    public struct GuildDiscoveryPoolEntry
    {
        [MetaMember(1)] public GuildDiscoveryInfoBase           PublicDiscoveryInfo;
        [MetaMember(2)] public GuildDiscoveryServerOnlyInfoBase ServerOnlyDiscoveryInfo;
        [MetaMember(3)] public MetaTime                         LastRefreshedAt;
    }

    [MetaSerializable]
    public class GuildDiscoveryPoolPage
    {
        [MetaMember(1)] public GuildDiscoveryPoolEntry[] Entries;
    }

    /// <summary>
    /// A base class for a persisted Guild Discovery Pool. This class manages the
    /// storage management and basic logic for a Guild Discovery pool.
    /// </summary>
    public abstract class PersistedGuildDiscoveryPool : IGuildDiscoveryPool
    {
        readonly int SchemaVersion = 2;
        readonly int MaxNumEntriesPerPersistedPage = 200;

        class OngoingPersistState
        {
            public List<EntityId> Guilds;
            public int GuildNdx;
            public int NextPageNdx;
            public Exception LastError;
            public int NumRepeatedFailures;
        }

        MetaDatabase _db;
        int _logicVersion;
        IMetaLogger _log;

        bool _isOdd;
        protected OrderedDictionary<EntityId, GuildDiscoveryPoolEntry> _entries;
        int _nextPersistSerial;
        bool _isDirty;
        OngoingPersistState _ongoingPersist;

        /// <summary>
        /// Maximum number of guilds in this pool. If more are added, some existing record is silently dropped.
        /// </summary>
        protected abstract int MaxNumEntries { get; }

        public string PoolId { get; }

        public int Count => _entries.Count;

        public PersistedGuildDiscoveryPool(string poolId)
        {
            PoolId = poolId;
            _db = MetaDatabase.Get();
            _entries = new OrderedDictionary<EntityId, GuildDiscoveryPoolEntry>();
        }

        public async Task Initialize(int logicVersion, IMetaLogger log)
        {
            _logicVersion = logicVersion;
            _log = log;
            await TryInitializeFromPersisted();
        }

        public async Task Shutdown()
        {
            await PersistImmediatelyAsync();
        }

        #region Persistence

        async Task TryInitializeFromPersisted()
        {
            // Pool alters between writing "odd" and "even" version to make sure there is always
            // at least one complete set available.

            GuildDiscoveryPoolHeader headerOdd = await TryReadHeader(isOdd: true);
            GuildDiscoveryPoolHeader headerEven = await TryReadHeader(isOdd: false);

            if (headerOdd == null && headerEven != null)
            {
                if (await TryInitializeWithPages(headerEven, isOdd: false))
                    return;
            }
            else if (headerOdd != null && headerEven == null)
            {
                if (await TryInitializeWithPages(headerOdd, isOdd: true))
                    return;
            }
            else if (headerOdd != null && headerEven != null)
            {
                // prefer the newer
                if (headerOdd.Serial > headerEven.Serial)
                {
                    if (await TryInitializeWithPages(headerOdd, isOdd: true))
                        return;
                    if (await TryInitializeWithPages(headerEven, isOdd: false))
                        return;
                }
                else
                {
                    if (await TryInitializeWithPages(headerOdd, isOdd: true))
                        return;
                    if (await TryInitializeWithPages(headerEven, isOdd: false))
                        return;
                }
            }

            InitializeNew();
        }

        void InitializeNew()
        {
            _isOdd = false;
        }

        async Task<GuildDiscoveryPoolHeader> TryReadHeader(bool isOdd)
        {
            PersistedGuildDiscoveryPoolPage headerItem = await _db.TryGetAsync<PersistedGuildDiscoveryPoolPage>(GetPersistedHeaderKey(isOdd));

            if (headerItem == null)
                return null;

            try
            {
                switch (headerItem.SchemaVersion)
                {
                    case 1:
                    case 2:
                        return MetaSerialization.DeserializeTagged<GuildDiscoveryPoolHeader>(headerItem.Payload, MetaSerializationFlags.Persisted, resolver: null, logicVersion: _logicVersion);
                }
            }
            catch
            {
            }
            return null;
        }

        async Task<GuildDiscoveryPoolPage> TryReadPage(bool isOdd, int pageNdx)
        {
            PersistedGuildDiscoveryPoolPage pageItem = await _db.TryGetAsync<PersistedGuildDiscoveryPoolPage>(GetPersistedPageKey(pageNdx, isOdd));

            if (pageItem == null)
                return null;

            try
            {
                switch (pageItem.SchemaVersion)
                {
                    case 1:
                        return ParseLegacyVersion1PoolPage(pageItem.Payload);
                    case 2:
                        return ParsePageVersion2(pageItem.Payload);
                }
            }
            catch
            {
            }
            return null;
        }

        // \todo[jarkko]: delete this after migrations
        protected abstract GuildDiscoveryPoolPage ParseLegacyVersion1PoolPage(byte[] payload);

        GuildDiscoveryPoolPage ParsePageVersion2(byte[] payload)
        {
            return MetaSerialization.DeserializeTagged<GuildDiscoveryPoolPage>(payload, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);
        }

        async Task<bool> TryInitializeWithPages(GuildDiscoveryPoolHeader header, bool isOdd)
        {
            List<GuildDiscoveryPoolPage> pages = new List<GuildDiscoveryPoolPage>();
            for (int pageNdx = 0; pageNdx < header.NumPages; ++pageNdx)
            {
                GuildDiscoveryPoolPage page = await TryReadPage(isOdd, pageNdx);
                if (page == null)
                    return false;
                pages.Add(page);
            }

            try
            {
                foreach (var page in pages)
                {
                    foreach (var entry in page.Entries)
                    {
                        if (FilterEntry(entry))
                            _entries.Add(entry.PublicDiscoveryInfo.GuildId, entry);
                    }
                }
            }
            catch(Exception ex)
            {
                _log.Warning("Discovery pool contained invalid data. Pool cleared. Got {Exception}", ex);
                return false;
            }

            _isOdd = isOdd;
            _nextPersistSerial = header.Serial + 1;
            return true;
        }

        string GetPersistedHeaderKey(bool isOdd)
        {
            string oddEvenSpecifier = isOdd ? "A" : "B";
            return $"{PoolId}.{oddEvenSpecifier}";
        }

        string GetPersistedPageKey(int pageNdx, bool isOdd)
        {
            string oddEvenSpecifier = isOdd ? "A" : "B";
            return Invariant($"{PoolId}.{oddEvenSpecifier}.{pageNdx}");
        }

        /// <summary>
        /// Advances partial work for persistence. Returns true if no persisting work is ongoing
        /// or it was just completed.
        /// </summary>
        public async Task<bool> PersistStep()
        {
            // Start persisting if possible and necessay.
            _ = TryStartPersist();

            // If persisting, continue it.
            if (_ongoingPersist != null)
                await AdvanceOngoingPersistAsync();

            return (_ongoingPersist == null);
        }

        async Task PersistImmediatelyAsync()
        {
            _ = TryStartPersist();
            while (_ongoingPersist != null)
                await AdvanceOngoingPersistAsync();
        }

        async Task AdvanceOngoingPersistAsync()
        {
            // if missing pages, write next
            // otherwise, write header and we are done
            if (_ongoingPersist.GuildNdx < _ongoingPersist.Guilds.Count)
            {
                await TryWriteNextPage();
            }
            else
            {
                if (await TryWriteHeader())
                {
                    // complete
                    _log.Debug("Pool {PoolId} persisted successfully with {NumPages} pages.", PoolId, _ongoingPersist.NextPageNdx);

                    _ongoingPersist = null;
                    _isOdd = !_isOdd;
                    _nextPersistSerial++;
                }
            }

            if (_ongoingPersist != null && _ongoingPersist.NumRepeatedFailures > 3)
            {
                _log.Warning("Failed to persist pool {PoolId}: {Error}", PoolId, _ongoingPersist.LastError);
                _ongoingPersist = null;
                _isDirty = true;
            }
        }

        bool TryStartPersist()
        {
            if (_ongoingPersist == null && _isDirty)
            {
                _ongoingPersist = new OngoingPersistState();
                _ongoingPersist.Guilds = new List<EntityId>(_entries.Keys);
                _ongoingPersist.GuildNdx = 0;
                _ongoingPersist.NextPageNdx = 0;
                _ongoingPersist.NumRepeatedFailures = 0;
                _ongoingPersist.LastError = null;
                _isDirty = false;

                _log.Debug("Starting pool {PoolId} persist. Pool contains {NumEntries} entries.", PoolId, _entries.Count);
                return true;
            }
            return false;
        }

        async Task TryWriteNextPage()
        {
            List<GuildDiscoveryPoolEntry> entries = new List<GuildDiscoveryPoolEntry>();
            int entryIdCursor = _ongoingPersist.GuildNdx;

            for (;;)
            {
                if (entries.Count >= MaxNumEntriesPerPersistedPage)
                    break;
                if (entryIdCursor >= _ongoingPersist.Guilds.Count)
                    break;

                EntityId guildId = _ongoingPersist.Guilds[entryIdCursor];
                entryIdCursor++;

                // removed while we were saving?
                if (!_entries.TryGetValue(guildId, out var entry))
                    continue;

                // became stale while we were saving. Might as well remove from the pool.
                // \note: No need to dirty. Since we are skipping it now, it would change nothing.
                if (!FilterEntry(entry))
                {
                    _entries.Remove(guildId);
                    continue;
                }

                entries.Add(entry);
            }

            GuildDiscoveryPoolPage page = new GuildDiscoveryPoolPage()
            {
                Entries = entries.ToArray(),
            };

            PersistedGuildDiscoveryPoolPage persistedPage = new PersistedGuildDiscoveryPoolPage()
            {
                PageId = GetPersistedPageKey(_ongoingPersist.NextPageNdx, !_isOdd),
                SchemaVersion = SchemaVersion,
                Payload = MetaSerialization.SerializeTagged<GuildDiscoveryPoolPage>(page, MetaSerializationFlags.Persisted, logicVersion: _logicVersion),
            };

            try
            {
                await _db.InsertOrUpdateAsync<PersistedGuildDiscoveryPoolPage>(persistedPage);
            }
            catch (Exception ex)
            {
                _ongoingPersist.LastError = ex;
                _ongoingPersist.NumRepeatedFailures++;
                return;
            }

            _ongoingPersist.NextPageNdx++;
            _ongoingPersist.GuildNdx = entryIdCursor;
            _ongoingPersist.NumRepeatedFailures = 0;
        }

        async Task<bool> TryWriteHeader()
        {
            GuildDiscoveryPoolHeader header = new GuildDiscoveryPoolHeader()
            {
                Serial = _nextPersistSerial,
                NumPages = _ongoingPersist.NextPageNdx,
            };

            PersistedGuildDiscoveryPoolPage persistedHeader = new PersistedGuildDiscoveryPoolPage()
            {
                PageId = GetPersistedHeaderKey(!_isOdd),
                SchemaVersion = SchemaVersion,
                Payload = MetaSerialization.SerializeTagged<GuildDiscoveryPoolHeader>(header, MetaSerializationFlags.Persisted, logicVersion: _logicVersion),
            };

            try
            {
                await _db.InsertOrUpdateAsync<PersistedGuildDiscoveryPoolPage>(persistedHeader);
            }
            catch (Exception ex)
            {
                _ongoingPersist.LastError = ex;
                _ongoingPersist.NumRepeatedFailures++;
                return false;
            }

            return true;
        }

        #endregion

        public void OnGuildUpdate(IGuildDiscoveryPool.GuildInfo info)
        {
            // update for something that is not supposed to be in the pool? remove
            if (!Filter(info))
            {
                OnGuildRemove(info.PublicDiscoveryInfo.GuildId);
                return;
            }

            // update for something that is supposed to be in the pool

            MetaTime now = MetaTime.Now;
            if (_entries.ContainsKey(info.PublicDiscoveryInfo.GuildId))
            {
                // update existing
            }
            else
            {
                // add new. Make space if need to. If can't, don't.
                if (_entries.Count > MaxNumEntries)
                {
                    if (!TryMakeSpaceFor(info))
                        return;
                }
            }

            GuildDiscoveryPoolEntry entry = new GuildDiscoveryPoolEntry()
            {
                PublicDiscoveryInfo = info.PublicDiscoveryInfo,
                ServerOnlyDiscoveryInfo = info.ServerOnlyDiscoveryInfo,
                LastRefreshedAt = now,
            };
            _entries.AddOrReplace(entry.PublicDiscoveryInfo.GuildId, entry);

            _isDirty = true;
        }

        public void OnGuildRemove(EntityId guildId)
        {
            if (_entries.Remove(guildId))
                _isDirty = true;
        }

        protected virtual bool TryMakeSpaceFor(IGuildDiscoveryPool.GuildInfo info)
        {
            var keyEnumerator = _entries.Keys.GetEnumerator();
            if (keyEnumerator.MoveNext())
                _entries.Remove(keyEnumerator.Current);
            return true;
        }

        public List<IGuildDiscoveryPool.GuildInfo> Fetch(GuildDiscoveryPlayerContextBase playerContext, int maxCount)
        {
            List<IGuildDiscoveryPool.GuildInfo> guildInfos = new List<IGuildDiscoveryPool.GuildInfo>(capacity: maxCount);

            // take some from the top of the entry list
            {
                var enumerator = _entries.Values.GetEnumerator();
                for (;;)
                {
                    // got enough
                    if (guildInfos.Count >= maxCount)
                        break;

                    // eof
                    if (!enumerator.MoveNext())
                        break;

                    // filter it again
                    if (!FilterEntry(enumerator.Current))
                        continue;

                    // Filter if this is fit for this request
                    if (!ContextFilter(playerContext, new IGuildDiscoveryPool.GuildInfo(enumerator.Current.PublicDiscoveryInfo, enumerator.Current.ServerOnlyDiscoveryInfo)))
                        continue;

                    guildInfos.Add(new IGuildDiscoveryPool.GuildInfo(enumerator.Current.PublicDiscoveryInfo, enumerator.Current.ServerOnlyDiscoveryInfo));
                }
            }

            // cycle entry list
            // \todo: figure out a nicer way to provide good samples over the whole population
            {
                int numCycles = Math.Min(_entries.Count / 2, maxCount);
                for (int i = 0; i < numCycles; ++i)
                {
                    var enumerator = _entries.GetEnumerator();
                    enumerator.MoveNext();

                    var top = enumerator.Current;
                    _entries.Remove(top.Key);

                    if (FilterEntry(top.Value))
                    {
                        _entries.Add(top.Key, top.Value);
                    }
                    else
                    {
                        // stale, no point in inserting back.
                        // \todo: should we compensate numCycles?
                        _isDirty = true;
                    }
                }
            }

            return guildInfos;
        }

        public List<IGuildDiscoveryPool.InspectionInfo> Inspect(int maxCount)
        {
            List<IGuildDiscoveryPool.InspectionInfo> infos = new List<IGuildDiscoveryPool.InspectionInfo>(capacity: maxCount);

            foreach (GuildDiscoveryPoolEntry entry in _entries.Values)
            {
                if (infos.Count >= maxCount)
                    break;

                bool passesFilter = FilterEntry(entry);
                infos.Add(new IGuildDiscoveryPool.InspectionInfo(
                    new IGuildDiscoveryPool.GuildInfo(entry.PublicDiscoveryInfo, entry.ServerOnlyDiscoveryInfo),
                    passesFilter,
                    entry.LastRefreshedAt));
            }

            return infos;
        }

        public void TestGuild(GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo, out bool includedInPool, out bool poolDataPassesFilter, out bool freshDataPassesFilter)
        {
            if (_entries.TryGetValue(publicDiscoveryInfo.GuildId, out GuildDiscoveryPoolEntry entry))
            {
                includedInPool = true;
                poolDataPassesFilter = Filter(new IGuildDiscoveryPool.GuildInfo(entry.PublicDiscoveryInfo, entry.ServerOnlyDiscoveryInfo));
            }
            else
            {
                includedInPool = false;
                poolDataPassesFilter = false;
            }
            freshDataPassesFilter = Filter(new IGuildDiscoveryPool.GuildInfo(publicDiscoveryInfo, serverOnlyDiscoveryInfo));
        }

        /// <summary>
        /// Determines whether a certain guild should (still) be in of the pool. This is used on:
        /// <list type="bullet">
        /// <item>After item is read from persisted storage and before it is inserted into the pool.</item>
        /// <item>Before item is written to the persisted storage.</item>
        /// <item>For the resulting guilds, when the pool is sampled.</item>
        /// </list>
        /// </summary>
        public abstract bool Filter(IGuildDiscoveryPool.GuildInfo info);

        /// <summary>
        /// Determines whether a certain guild should is valid for the player. If this returns false, the entry is
        /// not eligible, and will not be returned from Fetch.
        /// </summary>
        public abstract bool ContextFilter(GuildDiscoveryPlayerContextBase playerContext, IGuildDiscoveryPool.GuildInfo info);

        bool FilterEntry(in GuildDiscoveryPoolEntry entry)
        {
            if (!Filter(new IGuildDiscoveryPool.GuildInfo(entry.PublicDiscoveryInfo, entry.ServerOnlyDiscoveryInfo)))
                return false;

            // \todo: auto-refresh mechanism. If the pool gets some stale values, we should
            //        somehow get rid of them. For example, drop too old records (LastRefreshedAt).
            //        and trust the Guilds announce themselves periodically?

            return true;
        }
    }
}

#endif
