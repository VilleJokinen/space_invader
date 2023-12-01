// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Localization;

namespace Metaplay.Core.WebGL
{
    /// <summary>
    /// Provides runtime support for early-launch services on Web platform. Currently only includes application
    /// start localization selection and fetching.
    /// </summary>
    public static partial class WebSupportLib
    {
        public struct InitialLocalization
        {
            public LanguageId LanguageId;
            public ContentHash Version;
            public byte[] Data;

            public InitialLocalization(LanguageId languageId, ContentHash version, byte[] data)
            {
                LanguageId = languageId;
                Version = version;
                Data = data;
            }
        }

        /// <summary>
        /// Returns the localization chosen in web platform statup.
        /// </summary>
        public static partial InitialLocalization GetInitialLocalization();

        /// <summary>
        /// Returns the builtin localizations.
        /// </summary>
        public static partial OrderedDictionary<LanguageId, ContentHash> GetBuiltinLanguages();

        /// <summary>
        /// Sets hint for next application launch for desired language. This is just a fire-and-forget hint: Writing
        /// into the persisted storage happens at background at a later time and there is no way to observe if write ever
        /// completed.
        /// </summary>
        public static partial void StoreAppStartLanguage(LanguageId language);
    }
}
