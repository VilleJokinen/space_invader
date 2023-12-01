// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Server;
using System.Collections.Generic;

namespace Metaplay.Core.Localization
{
    /// <summary>
    /// A string container that contains multiple localizations for a single string.
    /// The default language is always the first localization in the dictionary. To resolve this to a single locale string
    /// use the <see cref="LocalizedExtensions.Localize{T}"/> method.
    /// <para>
    /// You can alternatively use the <see cref="LocalizationKey"/> instead to defer the resolution of the localization to the client.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public struct LocalizedString : ILocalized<string>
    {
        [MetaMember(1)] public OrderedDictionary<LanguageId, string> Localizations { get; private set; }
        /// <summary>
        /// Localization Key can be used to fetch localizations in the client from the Metaplay localization system.
        /// If this value is set, the <see cref="Localizations"/> dictionary is assumed null.
        /// </summary>
        [MetaMember(2)] public string LocalizationKey { get; private set; }

        public LocalizedString(IEnumerable<(LanguageId LanguageId, string Text)> localizations)
        {
            Localizations = localizations.ToOrderedDictionary(
                l => l.LanguageId,
                l => l.Text);
            LocalizationKey = null;
        }

        private LocalizedString(string key)
        {
            Localizations = null;
            LocalizationKey = key;
        }

        public static LocalizedString FromLocalizationKey(string key)
        {
            return new LocalizedString(key);
        }

        public LanguageId Collapse(LanguageId preferredLanguage)
        {
            return LocalizedExtensions.Collapse(this, preferredLanguage);
        }
    }

    public static class LocalizedStringExtensions
    {
        public static LocalizedString ToLocalizedString(this string content, LanguageId lang)
        {
            return new LocalizedString(new[] { (lang, content) });
        }
    }
}
