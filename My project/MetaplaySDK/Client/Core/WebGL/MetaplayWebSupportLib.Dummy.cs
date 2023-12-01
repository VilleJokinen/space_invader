// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !(UNITY_WEBGL && !UNITY_EDITOR)

using Metaplay.Core.Localization;
using System;

namespace Metaplay.Core.WebGL
{
    public static partial class WebSupportLib
    {
        static Exception CreateNotSupported()
        {
            return new NotSupportedException("Only supported in a browser environment.");
        }

        public static partial InitialLocalization GetInitialLocalization() => throw CreateNotSupported();

        public static partial OrderedDictionary<LanguageId, ContentHash> GetBuiltinLanguages() => throw CreateNotSupported();

        public static partial void StoreAppStartLanguage(LanguageId language) => throw CreateNotSupported();
    }
}

#endif
