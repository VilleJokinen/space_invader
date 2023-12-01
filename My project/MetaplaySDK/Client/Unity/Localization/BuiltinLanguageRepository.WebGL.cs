// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR

using Metaplay.Core;
using Metaplay.Core.Localization;
using Metaplay.Core.WebGL;
using System;

namespace Metaplay.Unity.Localization
{
    public static partial class BuiltinLanguageRepository
    {
        public static partial void Initialize()
        {
        }

        public static partial OrderedDictionary<LanguageId, ContentHash> GetBuiltinLanguages()
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("BuiltinLanguageRepository requires EnableLocalizations feature to be enabled");

            return WebSupportLib.GetBuiltinLanguages();
        }

        public static partial LocalizationLanguage GetAppStartLocalization()
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("BuiltinLanguageRepository requires EnableLocalizations feature to be enabled");

            WebSupportLib.InitialLocalization initial = WebSupportLib.GetInitialLocalization();
            return LocalizationLanguage.FromBytes(initial.LanguageId, initial.Version, initial.Data);
        }

        public static partial void StoreAppStartLanguage(LanguageId language)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("BuiltinLanguageRepository requires EnableLocalizations feature to be enabled");

            WebSupportLib.StoreAppStartLanguage(language);
        }
    }
}

#endif
