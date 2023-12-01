// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// The Metaplay-core fields for guild search. Do not modify these.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildSearchParamsBase : IMetaIntegrationConstructible<GuildSearchParamsBase>
    {
        // \note: setters need to be public for Dashboard to populate test queries.

        [MetaMember(1)] public string SearchString { get; set; }

        // \todo: pagination string NextPageToken

        public GuildSearchParamsBase() { }
        protected GuildSearchParamsBase(string searchString)
        {
            SearchString = searchString;
        }

        /// <summary>
        /// Validates whether the params are internally valid and are good for a
        /// search. Returning false rejects the search query early. This can be
        /// used to guarantee certain parameters are in an expected range.
        /// </summary>
        public virtual bool Validate()
        {
            if (SearchString == null)
                return false;
            if (string.IsNullOrWhiteSpace(SearchString))
                return false;
            return true;
        }
    }

    /// <summary>
    /// Default implementation of GuildSearchParams, used when no integration required
    /// </summary>
    [MetaSerializableDerived(100)]
    public sealed class DefaultGuildSearchParams : GuildSearchParamsBase
    {
    }
}

#endif
