// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Linq;

namespace Metaplay.Core.Localization
{
    public interface ILocalized
    {
        LanguageId Collapse(LanguageId preferredLanguage);
    }

    public interface ILocalized<T> : ILocalized
    {
        OrderedDictionary<LanguageId, T> Localizations { get; }
    }

    public static class LocalizedExtensions
    {
        public static T Localize<T>(this ILocalized<T> loc, LanguageId preferred = null)
        {
            if (loc.Localizations == null)
                return default;
            if (preferred != null && loc.Localizations.Count > 1)
            {
                if (loc.Localizations.TryGetValue(preferred, out T result))
                    return result;
            }
            return loc.Localizations.Values.FirstOrDefault();
        }

        public static LanguageId Collapse<T>(this ILocalized<T> loc, LanguageId preferredLanguage)
        {
            if (loc.Localizations == null)
                return preferredLanguage;

            if (!loc.Localizations.ContainsKey(preferredLanguage))
                preferredLanguage = loc.Localizations.Keys.FirstOrDefault();
            loc.Localizations.RemoveWhere(item => item.Key != preferredLanguage);
            return preferredLanguage;
        }
    }
}
