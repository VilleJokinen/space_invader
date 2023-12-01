// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Localization;
using UnityEngine;

namespace Metaplay.Unity.Localization
{
    /// <summary>
    /// Language Id mapping defines game-specific LanguageIds. This determines whether English is
    /// represented as LanguageId "en", "English", or "en-us"
    /// </summary>
    public class LanguageIdMapping : IMetaIntegrationSingleton<LanguageIdMapping>
    {
        readonly struct LanguageToIdMapping
        {
            public readonly SystemLanguage Language;
            public readonly string Bcp47Prefix;

            public LanguageToIdMapping(SystemLanguage language, string bcp47Prefix)
            {
                Language = language;
                Bcp47Prefix = bcp47Prefix;
            }
        }
        readonly LanguageToIdMapping[] _languageMappings = new LanguageToIdMapping[]
        {
            new LanguageToIdMapping(SystemLanguage.Afrikaans,             "af"      ),
            new LanguageToIdMapping(SystemLanguage.Arabic,                "ar"      ),
            new LanguageToIdMapping(SystemLanguage.Basque,                "eu"      ),
            new LanguageToIdMapping(SystemLanguage.Belarusian,            "be"      ),
            new LanguageToIdMapping(SystemLanguage.Bulgarian,             "bg"      ),
            new LanguageToIdMapping(SystemLanguage.Catalan,               "ca"      ),
            // \note: SystemLanguage.Chinese skipped because it could be either the traditional or the simplified. ChineseSimplified and ChineseTraditional and handled properly.
            new LanguageToIdMapping(SystemLanguage.Czech,                 "cs"      ),
            new LanguageToIdMapping(SystemLanguage.Danish,                "da"      ),
            new LanguageToIdMapping(SystemLanguage.Dutch,                 "nl"      ),
            new LanguageToIdMapping(SystemLanguage.English,               "en"      ),
            new LanguageToIdMapping(SystemLanguage.Estonian,              "et"      ),
            new LanguageToIdMapping(SystemLanguage.Faroese,               "fo"      ),
            new LanguageToIdMapping(SystemLanguage.Finnish,               "fi"      ),
            new LanguageToIdMapping(SystemLanguage.French,                "fr"      ),
            new LanguageToIdMapping(SystemLanguage.German,                "de"      ),
            new LanguageToIdMapping(SystemLanguage.Greek,                 "el"      ),
            new LanguageToIdMapping(SystemLanguage.Hebrew,                "he"      ),
            new LanguageToIdMapping(SystemLanguage.Hungarian,             "hu"      ),
            new LanguageToIdMapping(SystemLanguage.Icelandic,             "is"      ),
            new LanguageToIdMapping(SystemLanguage.Indonesian,            "id"      ),
            new LanguageToIdMapping(SystemLanguage.Italian,               "it"      ),
            new LanguageToIdMapping(SystemLanguage.Japanese,              "ja"      ),
            new LanguageToIdMapping(SystemLanguage.Korean,                "ko"      ),
            new LanguageToIdMapping(SystemLanguage.Latvian,               "lv"      ),
            new LanguageToIdMapping(SystemLanguage.Lithuanian,            "lt"      ),
            new LanguageToIdMapping(SystemLanguage.Norwegian,             "no"      ),
            new LanguageToIdMapping(SystemLanguage.Polish,                "pl"      ),
            new LanguageToIdMapping(SystemLanguage.Portuguese,            "pt"      ),
            new LanguageToIdMapping(SystemLanguage.Romanian,              "ro"      ),
            new LanguageToIdMapping(SystemLanguage.Russian,               "ru"      ),
            new LanguageToIdMapping(SystemLanguage.SerboCroatian,         "sh"      ),
            new LanguageToIdMapping(SystemLanguage.Slovak,                "sk"      ),
            new LanguageToIdMapping(SystemLanguage.Slovenian,             "sl"      ),
            new LanguageToIdMapping(SystemLanguage.Spanish,               "es"      ),
            new LanguageToIdMapping(SystemLanguage.Swedish,               "sv"      ),
            new LanguageToIdMapping(SystemLanguage.Thai,                  "th"      ),
            new LanguageToIdMapping(SystemLanguage.Turkish,               "tr"      ),
            new LanguageToIdMapping(SystemLanguage.Ukrainian,             "uk"      ),
            new LanguageToIdMapping(SystemLanguage.Vietnamese,            "vi"      ),
            new LanguageToIdMapping(SystemLanguage.ChineseSimplified,     "zh-Hans" ),
            new LanguageToIdMapping(SystemLanguage.ChineseTraditional,    "zh-Hant" ),
        };

        /// <summary>
        /// Gets the minimal BCP 47 prefix for the language. This is unique among the languages. If language has no language prefix, returns null.
        ///
        /// <para>
        /// Prefixes are mostly ISO 639-1 (2-letter-names) languages codes. Exceptions are:
        /// <list type="bullet">
        /// <item>Serbian and Croatian are both mapped to the deprecated "sh" ISO 639-1 code</item>
        /// <item>Traditional Chinese is mapped to "zh-Hant" (BCP-47)</item>
        /// <item>Simplified Chinese are mapped to "zh-Hans" (BCP-47, mainland china)</item>
        /// </list>
        /// </para>
        /// </summary>
        public string TryGetBcp47PrefixForDeviceLanguage(SystemLanguage systemLanguage)
        {
            for (int ndx = 0; ndx < _languageMappings.Length; ++ndx)
            {
                if (_languageMappings[ndx].Language == systemLanguage)
                    return _languageMappings[ndx].Bcp47Prefix;
            }
            return null;
        }

        /// <summary>
        /// Returns the <see cref="LanguageId"/> of the <see cref="SystemLanguage"/>, or null if no such
        /// language exists or is supported. Method may return a value even if the language does not exist.
        /// <para>
        /// Default implementation uses <see cref="TryGetBcp47PrefixForDeviceLanguage"/>.
        /// </para>
        /// </summary>
        public virtual LanguageId TryGetLanguageIdForDeviceLanguage(SystemLanguage systemLanguage)
        {
            return LanguageId.FromString(TryGetBcp47PrefixForDeviceLanguage(systemLanguage));
        }
    }
}
