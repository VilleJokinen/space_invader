// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const MetaplayWebSupportLib = {
    MetaplayWebSupportLib_GetLocalizationError: function() {
        return MetaplayUtil.stringToUTF8(_metaplayWebSupport.localizationError);
    },
    MetaplayWebSupportLib_GetLocalizationLanguageInfo: function(slot) {
        const index = _metaplayWebSupport.initialLocalizationIndex;
        return MetaplayUtil.stringToUTF8(_metaplayWebSupport.builtinLanguages[index][slot]);
    },
    MetaplayWebSupportLib_GetLocalizationLibraryDataLength: function() {
        return _metaplayWebSupport.localizationData.byteLength;
    },
    MetaplayWebSupportLib_ConsumeLocalizationLibraryDataBytes: function(destPtr) {
        HEAPU8.set(new Uint8Array(_metaplayWebSupport.localizationData), destPtr);
        _metaplayWebSupport.localizationData = null;
    },
    MetaplayWebSupportLib_GetNumBuiltinLanguages: function() {
        return _metaplayWebSupport.builtinLanguages.length;
    },
    MetaplayWebSupportLib_GetBuiltinLanguageInfo: function(index, slot) {
        return MetaplayUtil.stringToUTF8(_metaplayWebSupport.builtinLanguages[index][slot]);
    },
    MetaplayWebSupportLib_StoreAppStartLanguage: function(languagePtr) {
        try {
            // \note: global setting, no namespacing
            window.localStorage.setItem('metaplay-initlang', UTF8ToString(languagePtr));
        } catch {
            // tolerate missing local storage
        }
    }
};
mergeInto(LibraryManager.library, MetaplayWebSupportLib);
