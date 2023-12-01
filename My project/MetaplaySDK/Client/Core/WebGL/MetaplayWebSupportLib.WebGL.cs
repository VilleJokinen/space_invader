// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR

using Metaplay.Core.Localization;
using System;
using System.Runtime.InteropServices;

namespace Metaplay.Core.WebGL
{
    public static partial class WebSupportLib
    {
        static InitialLocalization _initialLocalization;

        public static partial InitialLocalization GetInitialLocalization()
        {
            string error = MetaplayWebSupportLib_GetLocalizationError();
            if (error != null)
                throw new InvalidOperationException($"Localization load failed: {error}");

            // Fetch data once from support lib. This "consumes" the data from JS side (to allow GC there),
            // and further calls will fail.
            if (_initialLocalization.Data == null)
            {
                LanguageId langId = LanguageId.FromString(MetaplayWebSupportLib_GetLocalizationLanguageInfo(slot: 0));
                ContentHash version = ContentHash.ParseString(MetaplayWebSupportLib_GetLocalizationLanguageInfo(slot: 1));
                byte[] buffer = new byte[MetaplayWebSupportLib_GetLocalizationLibraryDataLength()];
                MetaplayWebSupportLib_ConsumeLocalizationLibraryDataBytes(buffer);

                _initialLocalization = new InitialLocalization(
                    languageId: langId,
                    version:    version,
                    data:       buffer);
            }

            return _initialLocalization;
        }

        public static partial OrderedDictionary<LanguageId, ContentHash> GetBuiltinLanguages()
        {
            OrderedDictionary<LanguageId, ContentHash> languages = new OrderedDictionary<LanguageId, ContentHash>();
            int count = MetaplayWebSupportLib_GetNumBuiltinLanguages();
            for (int index = 0; index < count; ++index)
            {
                LanguageId language = LanguageId.FromString(MetaplayWebSupportLib_GetBuiltinLanguageInfo(index, slot: 0));
                ContentHash version = ContentHash.ParseString(MetaplayWebSupportLib_GetBuiltinLanguageInfo(index, slot: 1));
                languages[language] = version;
            }
            return languages;
        }

        public static partial void StoreAppStartLanguage(LanguageId language)
        {
            MetaplayWebSupportLib_StoreAppStartLanguage(language.ToString());
        }

        [DllImport("__Internal")] static extern string MetaplayWebSupportLib_GetLocalizationError();
        [DllImport("__Internal")] static extern string MetaplayWebSupportLib_GetLocalizationLanguageInfo(int slot);
        [DllImport("__Internal")] static extern int MetaplayWebSupportLib_GetLocalizationLibraryDataLength();
        [DllImport("__Internal")] static extern void MetaplayWebSupportLib_ConsumeLocalizationLibraryDataBytes(byte[] destPtr);
        [DllImport("__Internal")] static extern int MetaplayWebSupportLib_GetNumBuiltinLanguages();
        [DllImport("__Internal")] static extern string MetaplayWebSupportLib_GetBuiltinLanguageInfo(int index, int slot);
        [DllImport("__Internal")] static extern void MetaplayWebSupportLib_StoreAppStartLanguage(string language);
    }
}

#endif
