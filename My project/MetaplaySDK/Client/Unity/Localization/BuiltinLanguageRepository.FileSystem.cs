// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !UNITY_WEBGL || UNITY_EDITOR

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Localization;
using Metaplay.Core.Memory;
using Metaplay.Core.Tasks;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity.Localization
{
    public static partial class BuiltinLanguageRepository
    {
        const string    AppStartLanguageFile = "metaplay-lang-settings";
        const int       PersistedVersion     = 2;

        static TaskQueueExecutor s_writeExecutor;
        static OrderedDictionary<LanguageId, ContentHash> s_builtinLanguages;

        public static partial void Initialize()
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                return;

            s_builtinLanguages = InternalGetBuiltinLanguages();

            // Schedule writes on Thread Pool.
            s_writeExecutor = new TaskQueueExecutor(TaskScheduler.Default);
        }

        public static partial OrderedDictionary<LanguageId, ContentHash> GetBuiltinLanguages()
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("BuiltinLanguageRepository requires EnableLocalizations feature to be enabled");

            return s_builtinLanguages;
        }

        static OrderedDictionary<LanguageId, ContentHash> InternalGetBuiltinLanguages()
        {
            OrderedDictionary<LanguageId, ContentHash> builtinLanguages = new OrderedDictionary<LanguageId, ContentHash>();
            string localizationsBuiltinPath = Path.Combine(Application.streamingAssetsPath, "Localizations");

            try
            {
                ConfigArchiveBuildUtility.FolderEncoding.DirectoryIndex index = ConfigArchiveBuildUtility.FolderEncoding.ReadDirectoryIndex(localizationsBuiltinPath);
                foreach (ConfigArchiveBuildUtility.FolderEncoding.DirectoryIndex.Entry entry in index.FileEntries)
                {
                    string filename = Path.GetFileName(entry.Path);
                    LanguageId language = LanguageFilenameToLanguageName(filename);
                    builtinLanguages[language] = entry.Version;
                }
            }
            catch (Exception ex)
            {
                // \note: we capture the exception and fail the sanity check below, in order to produce more actionable error messages.
                MetaplaySDK.Logs.Metaplay.Error("Failed to load initial localizations: {Error}", ex);
            }

            // Sanity checks
            if (builtinLanguages.Count == 0)
            {
                throw new InvalidOperationException(
                      "The game build contains no localizations but EnableLocalizations=True is set in MetaplayCoreOptions. "
                    + "The build cannot launch from a clean state. Initial localizations must be built and included in the app StreamingAssets folder. "
                    + $"The asset directory of localizations is {localizationsBuiltinPath}");
            }

            return builtinLanguages;
        }

        public static LanguageId LanguageFilenameToLanguageName(string filename)
        {
            if (filename.Length <= 4 || !filename.EndsWith(".mpc"))
                throw new InvalidOperationException($"Invalid localization filename. Should end with .mpc but got \"{filename}\".");
            return LanguageId.FromString(filename.Substring(0, filename.Length - 4));
        }

        public static partial LocalizationLanguage GetAppStartLocalization()
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("BuiltinLanguageRepository requires EnableLocalizations feature to be enabled");

            OrderedDictionary<LanguageId, ContentHash> builtinLanguages = GetBuiltinLanguages();

            LanguageId appStartHintLanguage = null;

            // Read previously saved settings.
            string blobPath = Path.Combine(Application.persistentDataPath, AppStartLanguageFile);
            bool isPersistedDataValid = true;
            try
            {
                byte[] blob = AtomicBlobStore.TryReadBlob(blobPath);
                if (blob != null)
                {
                    // If contents are not valid, delete the data
                    if (!TryReadBlob(blob, out LanguageId persistedDefaultAppStartLanguage))
                    {
                        isPersistedDataValid = false;
                    }
                    else if (!builtinLanguages.ContainsKey(persistedDefaultAppStartLanguage))
                    {
                        isPersistedDataValid = false;
                    }
                    else
                    {
                        appStartHintLanguage = persistedDefaultAppStartLanguage;
                    }
                }
            }
            catch
            {
                isPersistedDataValid = false;
            }
            if (!isPersistedDataValid)
            {
                // If contents are not valid, delete the data
                _ = AtomicBlobStore.TryDeleteBlob(blobPath);
            }

            // If no app start language saved previously, use device language
            if (appStartHintLanguage == null)
            {
                LanguageId deviceLang = IntegrationRegistry.Get<LanguageIdMapping>().TryGetLanguageIdForDeviceLanguage(Application.systemLanguage);
                if (deviceLang != null && builtinLanguages.ContainsKey(deviceLang))
                {
                    appStartHintLanguage = deviceLang;
                }
            }

            // App start language from game config
            if (appStartHintLanguage == null)
            {
                appStartHintLanguage = MetaplayCore.Options.DefaultLanguage;

                // Save app start language for next time
                StoreAppStartLanguage(appStartHintLanguage);
            }

            // Read the language. No need to check for errors - if we fail, we cannot do anything anyway.
            string initialLocalizationPath = Path.Combine(Application.streamingAssetsPath, "Localizations", $"{appStartHintLanguage}.mpc");
            LocalizationLanguage localization = LocalizationLanguage.FromBytes(appStartHintLanguage, builtinLanguages[appStartHintLanguage], FileUtil.ReadAllBytes(initialLocalizationPath));

            return localization;
        }

        public static partial void StoreAppStartLanguage(LanguageId language)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("BuiltinLanguageRepository requires EnableLocalizations feature to be enabled");

            string blobPath = Path.Combine(Application.persistentDataPath, AppStartLanguageFile);
            byte[] blob = WriteBlob(defaultAppStartLanguage: language);

            // Write in background, in order.
            s_writeExecutor.EnqueueAsync(() =>
            {
                _ = AtomicBlobStore.TryWriteBlob(blobPath, blob);
            });
        }

        static byte[] WriteBlob(LanguageId defaultAppStartLanguage)
        {
            using (FlatIOBuffer buffer = new FlatIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    writer.WriteInt32(PersistedVersion);
                    writer.WriteString(defaultAppStartLanguage.ToString());
                }
                return buffer.ToArray();
            }
        }

        static bool TryReadBlob(byte[] blob, out LanguageId defaultAppStartLanguage)
        {
            try
            {
                using (IOReader reader = new IOReader(blob))
                {
                    if (reader.ReadInt32() == PersistedVersion)
                    {
                        string lang = reader.ReadString(maxSize: 128);

                        defaultAppStartLanguage = LanguageId.FromString(lang);
                        return true;
                    }
                }
            }
            catch
            {
            }

            defaultAppStartLanguage = null;
            return false;
        }
    }
}

#endif
