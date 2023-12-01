// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;

namespace Metaplay.Core.Player
{
    [MetaSerializable]
    public struct PlayerLocation
    {
        [MetaMember(1)] public CountryId Country { get; private set; }

        public PlayerLocation(CountryId country)
        {
            Country = country;
        }
    }

    [MetaSerializable]
    public struct CountryId
    {
        /// <summary>
        /// The ISO 3166-1 alpha-2 code.
        /// E.g. "FI" or "US"
        /// </summary>
        [MetaMember(1)] public string IsoCode { get; private set; }

        public CountryId(string isoCode)
        {
            IsoCode = isoCode ?? throw new ArgumentNullException(nameof(isoCode));
        }
    }
}
