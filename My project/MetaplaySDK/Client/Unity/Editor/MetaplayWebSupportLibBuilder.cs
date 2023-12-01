// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Serialization;
using Metaplay.Unity.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using static System.FormattableString;

namespace Metaplay.Unity
{
    /// <summary>
    /// Unity build hook for creating <see cref="Metaplay.Core.WebGL.WebSupportLib"/> at build time. WebSupportLib
    /// includes relevant information of the build, such as the available localization languages in the StreamingAssets
    /// (if localizations are enabled), and also handles injecting relevant application preRun-hooks such as
    /// localization early launch logic.
    /// </summary>
    public class MetaplayWebSupportLibBuilder : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string GeneratedSupportJsPrePath = "Assets/MetaplayWebSupportLib.jspre";

        bool _enabled;

        int IOrderedCallback.callbackOrder => 101;

        void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
        {
            try
            {
                _enabled = (report.summary.platformGroup == BuildTargetGroup.WebGL);
                if (!_enabled)
                    return;

                File.WriteAllText(GeneratedSupportJsPrePath, GenerateSupportLibJsPre());
                AssetDatabase.ImportAsset(GeneratedSupportJsPrePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

        void IPostprocessBuildWithReport.OnPostprocessBuild(BuildReport report)
        {
            try
            {
                if (_enabled)
                    return;

                AssetDatabase.DeleteAsset(GeneratedSupportJsPrePath);
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

        static string GenerateSupportLibJsPre()
        {
            IndentedStringBuilder sb = new IndentedStringBuilder(outputDebugCode: false);
            sb.AppendLine("// MACHINE-GENERATED CODE, DO NOT MODIFY");
            sb.AppendLine();
            sb.AppendLine("const _metaplayWebSupport = {};");

            if (MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                GenerateLocalizationSupport(sb);

            return sb.ToString();
        }

        static void GenerateLocalizationSupport(IndentedStringBuilder sb)
        {
            // Inspect the default language and all available languages in StreamingAssets. Default language (in game config) must be
            // present in the StreamingAssets.

            ConfigArchive languagesConfig = ConfigArchiveBuildUtility.FolderEncoding.FromDirectory(Path.Combine(UnityEngine.Application.streamingAssetsPath, "Localizations"));
            List<LanguageId> languages = new List<LanguageId>();
            for (int ndx = 0; ndx < languagesConfig.Entries.Count; ++ndx)
                languages.Add(BuiltinLanguageRepository.LanguageFilenameToLanguageName(languagesConfig.Entries[ndx].Name));

            int defaultLanguageIndex = languages.IndexOf(MetaplayCore.Options.DefaultLanguage);
            if (defaultLanguageIndex == -1)
            {
                throw new InvalidOperationException(
                    $"Game config and localizations mismatch: Default language is {MetaplayCore.Options.DefaultLanguage} but no such language could be loaded from StreamingAssets/Localization. " +
                    "Either the built localization is broken (you can try to regenerate localizations), or this is an internal error and and you should contact Metaplay.");
            }

            // Inject preRun hook for Localization system.
            sb.AppendLine("// Fetch the most suitable initial localization and prepare it for the main application.");
            sb.AppendLine("// We block the application start until localization is available.");
            sb.Indent("Module['preRun'].push(function () {");
            {
                sb.AppendLine("Module.addRunDependency('metaplayFetchInitialLocalization');");

                // We need to map browser language (BCP 47) into a game language. We enumerate all SystemLanguage, map them to both BCP 47 prefix and to the game
                // language code.

                LanguageIdMapping mapping = IntegrationRegistry.Get<LanguageIdMapping>();
                List<string> mappingEntries = new List<string>();
                foreach (SystemLanguage systemLanguage in EnumUtil.GetValues<SystemLanguage>())
                {
                    // Device language exists?
                    LanguageId language = mapping.TryGetLanguageIdForDeviceLanguage(systemLanguage);
                    if (language == null)
                        continue;

                    // Language supported by the game?
                    int languageIndex = languages.IndexOf(language);
                    if (languageIndex == -1)
                        continue;

                    // Language has a prefix.
                    string prefix = mapping.TryGetBcp47PrefixForDeviceLanguage(systemLanguage);
                    if (prefix == null)
                        continue;

                    // Add [LanguageId, Version, BCP-47-prefix]
                    mappingEntries.Add($"['{language}', '{languagesConfig.Entries[languageIndex].Hash}', '{prefix}']");
                }

                // Bake the names and versions of all available localizations into the lib. (Otherwise the client couldn't enumerate all possible builtin languages).
                sb.AppendLine($"_metaplayWebSupport.builtinLanguages = [{string.Join(",", mappingEntries)}];");

                // Choose initial: 1. from saved 2. from navigator 3. from default
                sb.AppendLine("function get_init_lang() {");
                sb.AppendLine("  try {");
                sb.AppendLine("    let savedLang = window.localStorage.getItem('metaplay-initlang');");
                sb.AppendLine("    let foundIndex = _metaplayWebSupport.builtinLanguages.findIndex(lang => lang[0] === savedLang);");
                sb.AppendLine("    if (foundIndex !== -1) return [foundIndex, 'saved'];");
                sb.AppendLine("  } catch {}");
                sb.AppendLine("  for (let i = 0; i < navigator.languages.length; ++i) {");
                sb.AppendLine("    let browserBcp47 = navigator.languages[i];");
                sb.AppendLine("    let foundIndex = _metaplayWebSupport.builtinLanguages.findIndex(lang => lang[2] === browserBcp47);");
                sb.AppendLine("    if (foundIndex !== -1) return [foundIndex, 'navigator lang'];");
                sb.AppendLine("    foundIndex = _metaplayWebSupport.builtinLanguages.findIndex(lang => browserBcp47.startsWith(lang[2] + '-'));");
                sb.AppendLine("    if (foundIndex !== -1) return [foundIndex, 'navigator lang'];");
                sb.AppendLine("  }");
                sb.AppendLine(Invariant($"  return [{defaultLanguageIndex}, 'default'];"));
                sb.AppendLine("}");
                sb.AppendLine("const [initLangIndex,initReason] = get_init_lang();");

                // Fetch initial and place the result in `_metaplayWebSupport` global
                sb.AppendLine("const initLang = _metaplayWebSupport.builtinLanguages[initLangIndex][0];");
                sb.AppendLine("console.debug('[L18nPreRun] selecting initial language ' + initLang + '. Source: ' + initReason);");
                sb.AppendLine("const initLangUrl = Module.streamingAssetsUrl + '/Localizations/' + initLang + '.mpc';");
                sb.AppendLine("const conf = { companyName: Module.companyName,  productName: Module.productName, control: 'must-revalidate' };");
                sb.AppendLine("Module.cachedFetch(initLangUrl, conf).then((result) => {");
                sb.AppendLine("  console.debug('[L18nPreRun] initial language fetch complete.');");
                sb.AppendLine("  _metaplayWebSupport.localizationError = null;");
                sb.AppendLine("  _metaplayWebSupport.initialLocalizationIndex = initLangIndex;");
                sb.AppendLine("  _metaplayWebSupport.localizationData = new Uint8Array(result.parsedBody);");
                sb.AppendLine("  Module.removeRunDependency('metaplayFetchInitialLocalization')");
                sb.AppendLine("}, (reason) => {");
                sb.AppendLine("  _metaplayWebSupport.localizationError = reason.toString();");
                sb.AppendLine("  Module.removeRunDependency('metaplayFetchInitialLocalization')");
                sb.AppendLine("});");
            }
            sb.Unindent();
            sb.AppendLine("});");
        }
    }
}
