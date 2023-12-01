// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using Metaplay.Server.Database;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Metaplay.Server.DatabaseScan.User
{
    public static class DatabaseScanUtil
    {
        static ConcurrentDictionary<Type, MethodInfo> s_specializedQueryPagedRangeAsyncCache = new();

        /// <summary>
        /// Helper for invoking <see cref="MetaDatabase.QueryPagedRangeAsync{T}(string, PagedIterator, int, string, string)"/> and converting the
        /// result to a IPersistedEntity-based result.
        /// </summary>
        static async Task<PagedQueryResult<IPersistedEntity>> QueryPagedRangeHelperAsync<TScannedItem>(MetaDatabase db, string opName, PagedIterator iterator, int pageSize, string firstKeyInclusive, string lastKeyInclusive)
            where TScannedItem : IPersistedEntity
        {
            PagedQueryResult<TScannedItem> result = await db.QueryPagedRangeAsync<TScannedItem>(opName, iterator, pageSize, firstKeyInclusive, lastKeyInclusive);
            return new PagedQueryResult<IPersistedEntity>(result.Iterator, result.Items.Cast<IPersistedEntity>().ToList());
        }

        /// <summary>
        /// Invoke <see cref="QueryPagedRangeHelperAsync{TScannedItem}(MetaDatabase, string, PagedIterator, int, string, string)"/>
        /// with a dynamic TScannedItem type.
        /// </summary>
        public static Task<PagedQueryResult<IPersistedEntity>> QueryPagedRangeAsync(MetaDatabase db, Type itemType, string opName, PagedIterator iterator, int pageSize, string firstKeyInclusive, string lastKeyInclusive)
        {
            MethodInfo specialized = s_specializedQueryPagedRangeAsyncCache.GetOrAdd(itemType, type =>
            {
                MethodInfo generic = typeof(DatabaseScanUtil).GetMethod(nameof(QueryPagedRangeHelperAsync), BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo specialized = generic.MakeGenericMethod(new Type[] { type });
                return specialized;
            });
            return (Task<PagedQueryResult<IPersistedEntity>>)specialized.Invoke(null, new object[] { db, opName, iterator, pageSize, firstKeyInclusive, lastKeyInclusive });
        }
    }
}
