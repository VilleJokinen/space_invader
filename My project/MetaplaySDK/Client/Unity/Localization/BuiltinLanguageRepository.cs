// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Localization;
using Metaplay.Core.Tasks;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity.Localization
{
    /// <summary>
    /// Helper to access the builtin localizations.
    /// </summary>
    public static partial class BuiltinLanguageRepository
    {
        /// <summary>
        /// Initializes the repository. Should be called only once in app startup, regardless of any feature flags.
        /// </summary>
        public static partial void Initialize();

        /// <summary>
        /// Gets all the builtin localizations and their version. Always contains at least one localization.
        /// </summary>
        public static partial OrderedDictionary<LanguageId, ContentHash> GetBuiltinLanguages();

        /// <summary>
        /// Gets the the initial language. The language is determined based on end-user device language,
        /// set of available languages and the previously set start language (see <see cref="StoreAppStartLanguageAsync(LanguageId)"/>.
        /// </summary>
        public static partial LocalizationLanguage GetAppStartLocalization();

        /// <summary>
        /// Sets hint for next application launch for desired initial language. This method does not block and it completes in the background.
        /// </summary>
        public static partial void StoreAppStartLanguage(LanguageId language);

        /// <summary>
        /// Retrieves the localization language from the builtin storage.
        /// </summary>
        public static Task<LocalizationLanguage> GetLocalizationAsync(LanguageId language)
        {
            // Always use Unity Thread:
            // * On Android, builtin IO uses WWW. Needs to use main thread.
            // * On WebGL, there is no thread pool. Use main thread.
            //
            // We could use thread pool on iOS and Editor but fragmenting behavior for
            // different platforms doesn't seem worth it
            TaskScheduler scheduler = MetaTask.UnityMainScheduler;

            return MetaTask.Run(async () =>
            {
                OrderedDictionary<LanguageId, ContentHash> versions = GetBuiltinLanguages();
                if (!versions.TryGetValue(language, out ContentHash version))
                    throw new ArgumentException("invalid language, not a builtin");

                // \note: On WebGL, this is a HTTP request which wont be cached. Usually this is not a problem as the app-start initial
                //        localization is specially cached, meaning that this only happens when changing a language. In which case, the
                //        download cache is unlikely to be useful anyway.
                MetaplaySDK.Logs.Localization.Debug("Accessing built-in localization for {Language}", language);
                string initialLocalizationPath = Path.Combine(Application.streamingAssetsPath, "Localizations", $"{language}.mpc");
                byte[] bytes = await FileUtil.ReadAllBytesAsync(initialLocalizationPath);
                return LocalizationLanguage.FromBytes(language, version, bytes);
            }, scheduler: scheduler);
        }
    }
}
