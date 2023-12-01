// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Localization;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// R25 debugging knobs for comparing behavior between legacy and new config parsing pipelines.
    /// </summary>
    static class ConfigBuildDevelopmentDebug
    {
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        /// <summary>
        /// Use legacy pipeline always, regardless of <see cref="MetaplayFeatureFlags.UseLegacyConfigParsingPipelineByDefault"/>,
        /// <see cref="GameConfigUseLegacyParsingPipelineAttribute"/>, and <see cref="GameConfigSyntaxAdapterAttribute"/>.
        /// </summary>
        public static readonly bool ForceLegacy = false;
#endif
        /// <summary>
        /// Force a fixed timestamp for archives, for reproducible archive contents (and thus hashes) for comparison.
        /// </summary>
        public static readonly bool ForceFixedArchiveTimestamp = false;
    }

    /// <summary>
    /// Game config build failed with an error related to the build inputs (eg, input failed to parse).
    /// Includes a <see cref="GameConfigBuildReport"/> that has the messages from the build log
    /// (<see cref="GameConfigBuildLog"/>).
    /// </summary>
    public class GameConfigBuildFailed : Exception
    {
        public readonly GameConfigBuildReport BuildReport;

        public GameConfigBuildFailed(GameConfigBuildReport buildreport) =>
            BuildReport = buildreport;

#if UNITY_2017_1_OR_NEWER
        // In Unity, override the printing to console to ensure the build report log is shown!
        public override string Message =>
            $"Game config build failed! See the log below for details.\n" +
            BuildReport.MessagesToString(maxBuildLogMessages: 20, maxValidationLogMessages: 20);
#endif

        /// <summary>
        /// Default ToString() includes the build and validation log messages to ensure the real cause of build
        /// failures is shown even if the exception is not handled.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            // Stack trace
            sb.AppendLine($"{GetType().FullName}: See the log below for details.");
            sb.AppendLine(StackTrace);

            // Include build report logs
            sb.AppendLine("");
            sb.Append(BuildReport.MessagesToString(maxBuildLogMessages: 20, maxValidationLogMessages: 20));

            return sb.ToString();
        }
    }

    public class GameConfigBuildDebugOptions
    {
        public bool EnableDebugPrints;
        /// <summary>
        /// Whether to use <see cref="ObjectGraphDump"/> for checking that <see cref="GameConfigBase.OnLoaded"/>
        /// didn't mutate items in an unsupported manner. This can use a lot of memory so it's best disabled
        /// outside local development environments.
        /// </summary>
        public bool EnableDebugDumpCheck = true;
        /// <summary>
        /// Run the full config build-time validation only for the baseline config, instead of validating
        /// all experiment variants. Setting this to true will generate a build warning in the output.
        /// </summary>
        public bool OnlyValidateBaseline;
    }

    /// <summary>
    /// Utilities provided to the game-specific game config build for populating a game config (shared or server) data from
    /// various sources.
    /// </summary>
    public interface IGameConfigBuilder
    {
        void AssignEmpty(string configEntryName);
        void AssignBaselineConfigEntryBuildResult(string configEntryName, IGameConfigEntry baselineEntry, GameConfigSourceMapping sourceMapping);
        void AssignConfigEntryBuildResult<TConfigEntryPatch>(string configEntryName, IGameConfigEntry baselineEntry, Dictionary<ExperimentVariantPair, TConfigEntryPatch> variantEntryPatches, GameConfigSourceMapping sourceMapping) where TConfigEntryPatch : GameConfigEntryPatch;
        void AssignConfigKeyValueStructureBuildResult<TStructure>(string configEntryName, List<VariantConfigStructureMember> members, GameConfigSourceMapping sourceMapping) where TStructure : GameConfigKeyValue, new();
        void AssignLibraryBuildResult<TKey, TInfo>(string configEntryName, List<VariantConfigItem<TKey, TInfo>> items, GameConfigSourceMapping sourceMapping) where TInfo : class, IGameConfigData<TKey>, new();
        void BuildGameConfigKeyValueStructure<TStructure>(string configEntryName, SpreadsheetContent sourceSheet) where TStructure : GameConfigKeyValue, new();
        void BuildGameConfigLibrary<TKey, TInfo>(string configEntryName, SpreadsheetContent sourceSheet) where TInfo : class, IGameConfigData<TKey>, new();
        void BuildGameConfigEntry(string configEntryName, SpreadsheetContent sourceSheet);
        void BuildGameConfigLibraryWithSourceTransform<TKey, TInfo, TSourceItem>(string configEntryName, SpreadsheetContent sheet)
            where TInfo : class, IGameConfigData<TKey>, new()
            where TSourceItem : IGameConfigSourceItem<TKey, TInfo>, new();

        void BuildGameConfigLibraryWithTransform<TKey, TInfo, TSourceItem>(string configEntryName, SpreadsheetContent sheet, Func<TSourceItem, TInfo> transform)
            where TInfo : class, IGameConfigData<TKey>, new()
            where TSourceItem : IGameConfigSourceItem<TKey, TInfo>, new();
        List<VariantConfigItem<TKey, TItem>> ParseLibrarySheetItems<TKey, TItem>(string configEntryName, SpreadsheetContent sheet) where TItem : IGameConfigKey<TKey>, new();
        List<VariantConfigStructureMember> ParseStructureSheetMembers<TStructure>(string configEntryName, SpreadsheetContent sheet) where TStructure : GameConfigKeyValue, new();
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        T ParseCsvKeyValueStructure<T>(string fileName, byte[] bytes) where T : GameConfigKeyValue, new();
        GameConfigLibrary<TKey, TValue> ParseCsvLibrary<TKey, TValue>(string fileName, byte[] bytes) where TValue : class, IGameConfigData<TKey>, new();
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        GameConfigLibrary<TKey, TValue> ParseJsonLibrary<TKey, TValue>(string fileName, byte[] bytes) where TValue : class, IGameConfigData<TKey>, new();
        void AddCustomArchiveEntry(ConfigArchiveEntry entry);
        Type ConfigType { get; }
        GameConfigBuildLog BuildLog { get; }
        List<GameConfigSourceMapping> SourceMappings { get;  }
    }

    public interface IGameConfigBuilder<TConfig> : IGameConfigBuilder where TConfig : IGameConfig
    {
        TConfig RawOutput { get; }
    }

    public interface IGameConfigBuilderProvider<TConfig> where TConfig : IGameConfig
    {
        IGameConfigBuilder<TDerivedConfig> MakeBuilder<TDerivedConfig>() where TDerivedConfig : TConfig;
    }

    interface IGameConfigBuild
    {
        GameConfigBuildDebugOptions    DebugOptions  { get; }
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        bool DebugForceLegacyParsing { get; }
        SpreadsheetParseOptions SpreadsheetParseOptions { get; }
        CsvParseOptions CsvParseOptions { get; }
#endif
        GameConfigBuildIntegration Integration { get; }
    }

    class GameConfigBuildState<TConfig> : IGameConfigBuilderProvider<TConfig> where TConfig : IGameConfig
    {
        GameConfigBuildLog                                        _buildLog;
        TConfig                                                   _output;
        HashSet<string>                                           _builtEntries         = new HashSet<string>();
        List<ConfigArchiveEntry>                                  _customArchiveEntries = new List<ConfigArchiveEntry>();
        Dictionary<string, GameConfigEntryBuildResult>            _entryBuildResults    = new Dictionary<string, GameConfigEntryBuildResult>();
        GameConfigRepository.GameConfigTypeInfo                   _configTypeInfo;
        public Type                                               GameConfigType => _configTypeInfo.GameConfigType;
        ConfigArchive                                             _parentArchive;
        public Dictionary<ExperimentVariantPair, GameConfigPatch> ParentVariantPatches;
        IGameConfigBuild                                          _build;
        public TConfig                                            RawOutput  => _output;
        public List<GameConfigSourceMapping>                      SourceMappings { get; } = new List<GameConfigSourceMapping>();

        public IGameConfigBuilder<TDerivedConfig> MakeBuilder<TDerivedConfig>() where TDerivedConfig : TConfig
        {
            return new BuilderApi<TDerivedConfig>(this);
        }

        public GameConfigBuildState(
            IGameConfigBuild build,
            ConfigArchive parentArchive,
            GameConfigBuildLog buildLog,
            TConfig output)
        {
            _build = build;
            _parentArchive = parentArchive;
            _output = output;
            _buildLog = buildLog;
            _configTypeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(output.GetType());
        }

        public GameConfigBuildOutput Finalize(MetaTime createdAt)
        {
            // Finalize config loading
            _output.OnConfigEntriesPopulated(isBuildingConfigs: true);

            // Construct final archive
            ConfigArchiveEntry[]            baselineMpcEntries    = _output.ExportMpcArchiveEntries();
            List<ConfigArchiveEntry> baselineCustomEntries = _customArchiveEntries;
            Dictionary<string, ConfigArchiveEntry> baselineEntriesByName = baselineMpcEntries.Concat(baselineCustomEntries).ToDictionary(entry => entry.Name);

            // If this is a partial build, some entries might be missing from the list of newly-built entries.
            // Namely, custom (non-mpc) entries aren't exported by GameConfigBase.ExportMpcArchiveEntries,
            // and might thus be missing at this point.
            // In that case, take those entries directly from the parent archive.
            if (_parentArchive != null)
            {
                foreach (ConfigArchiveEntry entry in _parentArchive.Entries)
                {
                    if (entry.Name.EndsWith(".AliasTable.mpc", StringComparison.Ordinal))
                        continue;
                    if (!baselineEntriesByName.ContainsKey(entry.Name))
                    {
                        if (_build.DebugOptions.EnableDebugPrints)
                            DebugLog.Info($"Copying entry {entry.Name} from baseline to new archive");
                        baselineEntriesByName.Add(entry.Name, entry);
                    }
                }
            }
            ConfigArchive                                      baselineArchive = new ConfigArchive(createdAt, baselineEntriesByName.Values);
            Dictionary<ExperimentVariantPair, GameConfigPatchEnvelope> variantPatches  = GetSerializedVariantPatches(ParentVariantPatches);

            return new GameConfigBuildOutput(baselineArchive, variantPatches);
        }

        public Dictionary<ExperimentVariantPair, GameConfigPatchEnvelope> GetSerializedVariantPatches(Dictionary<ExperimentVariantPair, GameConfigPatch> parentVariantPatches)
        {
            // _entryBuildResults contains nested lookups of shape  entryName -> (experimentVariantKey -> entryPatch) .
            // Here we translate that into entryPatchesPerVariant, of shape  experimentVariantKey -> (entryName -> entryPatch) .

            Dictionary<ExperimentVariantPair, Dictionary<string, GameConfigEntryPatch>> entryPatchesPerVariant = new Dictionary<ExperimentVariantPair, Dictionary<string, GameConfigEntryPatch>>();

            void AddOrReplaceEntryPatch(string entryName, ExperimentVariantPair evKey, GameConfigEntryPatch entryPatch)
            {
                if (!entryPatchesPerVariant.TryGetValue(evKey, out Dictionary<string, GameConfigEntryPatch> entryPatchesOfThisVariant))
                {
                    entryPatchesOfThisVariant = new Dictionary<string, GameConfigEntryPatch>();
                    entryPatchesPerVariant.Add(evKey, entryPatchesOfThisVariant);
                }

                entryPatchesOfThisVariant[entryName] = entryPatch;
            }

            foreach (GameConfigRepository.GameConfigEntryInfo entryInfo in _configTypeInfo.Entries.Values)
            {
                // If this entry was built now, use the patches from that build result.
                // Otherwise, use the patches from parent (if any).
                if (_entryBuildResults.ContainsKey(entryInfo.Name))
                {
                    Dictionary<ExperimentVariantPair, GameConfigEntryPatch> entryVariantPatches = _entryBuildResults[entryInfo.Name].VariantPatches;

                    foreach ((ExperimentVariantPair evKey, GameConfigEntryPatch entryPatch) in entryVariantPatches)
                        AddOrReplaceEntryPatch(entryInfo.Name, evKey, entryPatch);
                }
                else if (parentVariantPatches != null)
                {
                    foreach ((ExperimentVariantPair evKey, GameConfigPatch patch) in parentVariantPatches)
                    {
                        if (patch.TryGetSpecifiedEntryPatch(entryInfo.Name, out GameConfigEntryPatch entryPatch))
                            AddOrReplaceEntryPatch(entryInfo.Name, evKey, entryPatch);
                    }
                }
            }

            // Construct per-variant config patches, and serialize each.

            return entryPatchesPerVariant.ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    Dictionary<string, GameConfigEntryPatch> entryPatches = kv.Value;
                    GameConfigPatch configPatch = new GameConfigPatch(GameConfigType, entryPatches);
                    return configPatch.SerializeToEnvelope(MetaSerializationFlags.IncludeAll);
                });
        }

        class GameConfigEntryBuildResult
        {
            public IGameConfigEntry Baseline;
            public Dictionary<ExperimentVariantPair, GameConfigEntryPatch> VariantPatches;

            public GameConfigEntryBuildResult(IGameConfigEntry baseline, Dictionary<ExperimentVariantPair, GameConfigEntryPatch> variantPatches)
            {
                Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
                VariantPatches = variantPatches ?? throw new ArgumentNullException(nameof(variantPatches));
            }
        }

        class DummyInfo : IGameConfigData<object>
        {
            public object ConfigKey => throw new NotImplementedException();
        }
        class DummySourceItem : IGameConfigSourceItem<object, DummyInfo>
        {
            public object ConfigKey { get; }

            public DummyInfo ToConfigData()
            {
                throw new NotImplementedException();
            }
        }
        class DummyStruct : GameConfigKeyValue
        {
        }

        public class BuilderApi<TDerivedConfig> : IGameConfigBuilder<TDerivedConfig> where TDerivedConfig : TConfig
        {
            GameConfigBuildState<TConfig> _builder;

            public BuilderApi(GameConfigBuildState<TConfig> builder)
            {
                MetaDebug.Assert(typeof(TDerivedConfig).IsAssignableFrom(builder.GameConfigType), $"GameConfig type mismatch: {builder.GameConfigType} does not inherit from {typeof(TDerivedConfig)}");
                _builder = builder;
            }

            public TDerivedConfig RawOutput => (TDerivedConfig)_builder.RawOutput;

            public Type                          ConfigType     => _builder.RawOutput.GetType();
            public GameConfigBuildLog            BuildLog       => _builder._buildLog;
            public List<GameConfigSourceMapping> SourceMappings => _builder.SourceMappings;

            public void AddCustomArchiveEntry(ConfigArchiveEntry entry)
            {
                _builder._customArchiveEntries.Add(entry);
            }

            public void AssignEmpty(string configEntryName)
            {
                if (!_builder._configTypeInfo.Entries.TryGetValue(configEntryName, out GameConfigRepository.GameConfigEntryInfo entryInfo))
                {
                    throw new InvalidOperationException($"Config entry '{configEntryName}' is not a registered GameConfigEntry");
                }
                AssignBaselineConfigEntryBuildResult(configEntryName, (IGameConfigEntry)Activator.CreateInstance(entryInfo.MemberInfo.GetDataMemberType(), nonPublic: true), null);
            }

            public void BuildGameConfigEntry(string configEntryName, SpreadsheetContent sourceSheet)
            {
                if (!_builder._configTypeInfo.Entries.TryGetValue(configEntryName, out GameConfigRepository.GameConfigEntryInfo entryInfo))
                {
                    throw new InvalidOperationException($"Config entry '{configEntryName}' is not a registered GameConfigEntry");
                }
                Type t = entryInfo.MemberInfo.GetDataMemberType();
                MethodInfo method;
                if (t.IsGameConfigLibrary())
                {
                    if (entryInfo.BuildSourceType != null)
                    {
                        Type concreteSourceType = entryInfo.BuildSourceType;
                        // Special handling for abstract source item types
                        if (concreteSourceType.IsAbstract)
                            concreteSourceType = IntegrationRegistry.GetSingleIntegrationType(entryInfo.BuildSourceType);

                        MethodInfo prototype = GetType().GetMethod(nameof(BuildGameConfigLibraryWithSourceTransform));
                        method = prototype.GetGenericMethodDefinition().MakeGenericMethod(new Type[] { t.GetGenericArguments()[0], t.GetGenericArguments()[1], concreteSourceType });
                    }
                    else
                    {
                        MethodInfo prototype = GetType().GetMethod(nameof(BuildGameConfigLibrary));
                        method = prototype.GetGenericMethodDefinition().MakeGenericMethod(t.GetGenericArguments());
                    }
                }
                else if (t.IsDerivedFrom<GameConfigKeyValue>())
                {
                    MethodInfo prototype = GetType().GetMethod(nameof(BuildGameConfigKeyValueStructure));
                    method = prototype.GetGenericMethodDefinition().MakeGenericMethod(t);
                }
                else
                {
                    throw new InvalidOperationException($"Config entry '{configEntryName}' is not a GameConfigLibrary or GameConfigKeyValue");
                }

                method.InvokeWithoutWrappingError(this, new object[] { configEntryName, sourceSheet });
            }

            public void BuildGameConfigLibrary<TKey, TInfo>(string configEntryName, SpreadsheetContent sheet)
                where TInfo : class, IGameConfigData<TKey>, new()
            {
                // Parse items
                List<VariantConfigItem<TKey, TInfo>> items = ParseLibrarySheetItems<TKey, TInfo>(configEntryName, sheet);
                // \todo Detect configKey duplicates here? Seems like they can sneak past & cause exceptions

                // If errors occurred, bail out
                if (BuildLog.HasErrors())
                    return;

                // Register all parsed items into the mapping table so we can later resolve the source & location where the item was defined.
                GameConfigSourceMapping sourceMapping = GameConfigSourceMapping.ForGameConfigLibrary(sheet, items);

                // Create entry build result (entry baseline and patches)
                AssignLibraryBuildResult(configEntryName, items, sourceMapping);
            }

            public void BuildGameConfigLibraryWithSourceTransform<TKey, TInfo, TSourceItem>(string configEntryName, SpreadsheetContent sheet)
                where TInfo : class, IGameConfigData<TKey>, new()
                where TSourceItem : IGameConfigSourceItem<TKey, TInfo>, new()
            {
                BuildGameConfigLibraryWithTransform<TKey, TInfo, TSourceItem>(configEntryName, sheet, x => x.ToConfigData());
            }

            public void BuildGameConfigLibraryWithTransform<TKey, TInfo, TSourceItem>(string configEntryName, SpreadsheetContent sheet, Func<TSourceItem, TInfo> transform)
                where TInfo : class, IGameConfigData<TKey>, new()
                where TSourceItem : IGameConfigSourceItem<TKey, TInfo>, new()
            {
                // Parse source items
                List<VariantConfigItem<TKey, TSourceItem>> sourceItems = ParseLibrarySheetItems<TKey, TSourceItem>(configEntryName, sheet);

                // If errors occurred, bail out
                if (BuildLog.HasErrors())
                    return;

                // Transform source items to final config items
                List<VariantConfigItem<TKey, TInfo>> transformedItems = new List<VariantConfigItem<TKey, TInfo>>(sourceItems.Count);
                foreach (VariantConfigItem<TKey, TSourceItem> srcItem in sourceItems)
                {
                    try
                    {
                        TInfo transformedItem = transform(srcItem.Item);
                        transformedItems.Add(new VariantConfigItem<TKey, TInfo>(transformedItem, srcItem.VariantIdMaybe, srcItem.Aliases, srcItem.SourceLocation));
                    }
                    catch (Exception ex)
                    {
                        // \todo Include item identity here? Would really benefit from having the correct ConfigKey being parsed at this stage
                        BuildLog.WithSource(sheet.SourceInfo).WithLocation(srcItem.SourceLocation).Error($"Failed to transform config item '{srcItem.Item.ConfigKey}' of type '{srcItem.Item.GetType().ToGenericTypeString()}'", ex);
                    }
                }

                // If errors occurred, bail out
                if (BuildLog.HasErrors())
                    return;

                // Debug print
                DebugPrintLibraryItems(transformedItems, $"Items transformed from {sheet.Name}");

                // Build mapping table from output items back into the source locations to allow validation messages to be reported with original items
                // \note We only register the mappins for the transformed items (as those are the ones getting validated).
                GameConfigSourceMapping sourceMapping = GameConfigSourceMapping.ForGameConfigLibrary(sheet, transformedItems);

                // Create entry build result (entry baseline and patches)
                AssignLibraryBuildResult(configEntryName, transformedItems, sourceMapping);
            }

            /// <summary>
            /// Creates a baseline library and patches from the given items.
            /// Also assigns the baseline to the config entry member.
            /// </summary>
            public void AssignLibraryBuildResult<TKey, TInfo>(string configEntryName, List<VariantConfigItem<TKey, TInfo>> items, GameConfigSourceMapping sourceMapping)
                where TInfo : class, IGameConfigData<TKey>, new()
            {
                if (!_builder._configTypeInfo.Entries.TryGetValue(configEntryName, out GameConfigRepository.GameConfigEntryInfo configEntryInfo))
                    throw new InvalidOperationException($"{_builder.GameConfigType} contains no entry named {configEntryName}");

                // Group the items by variant id (as well as baseline, which has null variant id)
                IEnumerable<TInfo> baselineItems = items.Where(item => item.VariantIdMaybe == null).Select(item => item.Item);
                IEnumerable<IGrouping<string, TInfo>> itemsPerVariant = items.Where(item => item.VariantIdMaybe != null).GroupBy(item => item.VariantIdMaybe, item => item.Item);

                // Construct baseline library from the baseline items
                GameConfigLibrary<TKey, TInfo> baselineLibrary = GameConfigLibrary<TKey, TInfo>.CreateSolo(
                    GameConfigUtil.ConvertToOrderedDictionary<TKey, TInfo>(baselineItems));

                // Register aliases
                foreach (VariantConfigItem<TKey, TInfo> item in items.Where(item => item.Aliases != null))
                {
                    foreach (TKey alias in item.Aliases)
                    {
                        baselineLibrary.RegisterAlias(item.Item.ConfigKey, alias);
                    }
                }

                // Validate that, in the source items, variant-appended items appear after all other kinds of items.
                // \todo [nuutti] Support variant patches to insert new items at arbitrary positions, not just appending?
                {
                    VariantConfigItem<TKey, TInfo>? firstVariantAppendedItem = null;

                    foreach (VariantConfigItem<TKey, TInfo> item in items)
                    {
                        bool isVariantAppended = item.VariantIdMaybe != null && !baselineLibrary.ContainsKey(item.Item.ConfigKey);

                        if (isVariantAppended)
                        {
                            if (!firstVariantAppendedItem.HasValue)
                                firstVariantAppendedItem = item;
                        }
                        else if (firstVariantAppendedItem.HasValue)
                        {
                            TKey appendedKey = firstVariantAppendedItem.Value.Item.ConfigKey;
                            string appendedVariantId = firstVariantAppendedItem.Value.VariantIdMaybe;
                            TKey currentKey = item.Item.ConfigKey;
                            string currentDescription = item.VariantIdMaybe != null
                                                          ? $"{currentKey} (replaced by variant {item.VariantIdMaybe})"
                                                          : $"{currentKey} (present in baseline)";

                            throw new ConfigParserException(
                                $"Newly-added config variant items must occur after all other config items in the sheet. {appendedKey} (added by variant {appendedVariantId}) appears before {currentDescription}.",
                                item.SourceLocation);
                        }
                    }
                }

                // Construct library patches from the variants
                Dictionary<ExperimentVariantPair, GameConfigLibraryPatch<TKey, TInfo>> variantPatches = itemsPerVariant.ToDictionary(
                    keySelector: variantItems => ParseExperimentVariantIdFromSheet(variantItems.Key),
                    elementSelector: variantItems =>
                    {
                        try
                        {
                            IEnumerable<TInfo> appendedItems = variantItems.Where(item => !baselineLibrary.ContainsKey(item.ConfigKey));
                            IEnumerable<TInfo> replacedItems = variantItems.Where(item => baselineLibrary.ContainsKey(item.ConfigKey));
                            return new GameConfigLibraryPatch<TKey, TInfo>(
                                replacedItems: replacedItems,
                                appendedItems: appendedItems);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Error in variant {variantItems.Key}: {ex.Message}", ex);
                        }
                    });

                // Assign
                AssignConfigEntryBuildResult(configEntryName, baselineLibrary, variantPatches, sourceMapping);
            }

            public void BuildGameConfigKeyValueStructure<TStructure>(string configEntryName, SpreadsheetContent sheet)
                where TStructure : GameConfigKeyValue, new()
            {
                // Parse members
                List<VariantConfigStructureMember> members = ParseStructureSheetMembers<TStructure>(configEntryName, sheet);

                // If errors occurred, bail out
                if (BuildLog.HasErrors())
                    return;

                // Construct mapping from parsed output back to source locations
                GameConfigSourceMapping sourceMapping = GameConfigSourceMapping.ForKeyValueObject(sheet, members);

                // Create entry build result (entry baseline and patches)
                AssignConfigKeyValueStructureBuildResult<TStructure>(configEntryName, members.ToList(), sourceMapping);
            }

            public void AssignConfigKeyValueStructureBuildResult<TStructure>(string configEntryName, List<VariantConfigStructureMember> members, GameConfigSourceMapping sourceMapping)
                where TStructure : GameConfigKeyValue, new()
            {
                // Group the members by variant id (as well as baseline, which has null variant id)
                IEnumerable<ConfigStructureMember> baselineMembers = members.Where(member => member.VariantIdMaybe == null).Select(member => member.Member);
                IEnumerable<IGrouping<string, ConfigStructureMember>> membersPerVariant = members.Where(member => member.VariantIdMaybe != null).GroupBy(member => member.VariantIdMaybe, member => member.Member);

                // Construct baseline structure from the baseline members
                TStructure baselineStructure = new TStructure();
                {
                    HashSet<string> specifiedMembers = new HashSet<string>();
                    foreach (ConfigStructureMember member in baselineMembers)
                    {
                        if (!specifiedMembers.Add(member.MemberInfo.Name))
                            throw new InvalidOperationException($"Member {typeof(TStructure).Name}.{member.MemberInfo.Name} specified multiple times");

                        member.MemberInfo.GetDataMemberSetValueOnDeclaringType()(baselineStructure, member.MemberValue);
                    }
                }

                // Construct structure patches from the variants
                Dictionary<ExperimentVariantPair, GameConfigStructurePatch<TStructure>> variantPatches = membersPerVariant.ToDictionary(
                    keySelector: variantMembers => ParseExperimentVariantIdFromSheet(variantMembers.Key),
                    elementSelector: variantMembers =>
                    {
                        try
                        {
                            return new GameConfigStructurePatch<TStructure>(
                                replacedMembersByName: variantMembers.ToOrderedDictionary(
                                    member => member.MemberInfo.Name,
                                    member => member.MemberValue));
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Error in variant {variantMembers.Key}: {ex.Message}", ex);
                        }
                    });

                // Assign
                AssignConfigEntryBuildResult(configEntryName, baselineStructure, variantPatches, sourceMapping);
            }

            public void AssignBaselineConfigEntryBuildResult(string configEntryName, IGameConfigEntry baselineEntry, GameConfigSourceMapping sourceMapping)
            {
                if (!_builder._configTypeInfo.Entries.TryGetValue(configEntryName, out GameConfigRepository.GameConfigEntryInfo configEntryInfo))
                    throw new InvalidOperationException($"{_builder.GameConfigType} contains no entry named {configEntryName}");

                // Guard against the same entry being built multiple times (it's probably a mistake)
                if (!_builder._builtEntries.Add(configEntryName))
                    throw new InvalidOperationException($"{configEntryName} config is being built, but it was already built during this config build. This is probably a mistake.");

                if (sourceMapping != null)
                    _builder.SourceMappings.Add(sourceMapping);

                // Assign to config entry member
                // \todo: Type check
                configEntryInfo.MemberInfo.GetDataMemberSetValueOnDeclaringType()(_builder._output, baselineEntry);
            }

            public void AssignConfigEntryBuildResult<TConfigEntryPatch>(string configEntryName, IGameConfigEntry baselineEntry, Dictionary<ExperimentVariantPair, TConfigEntryPatch> variantEntryPatches, GameConfigSourceMapping sourceMapping)
                where TConfigEntryPatch : GameConfigEntryPatch
            {
                AssignBaselineConfigEntryBuildResult(configEntryName, baselineEntry, sourceMapping);

                // Create build result
                _builder._entryBuildResults.Add(configEntryName, new GameConfigEntryBuildResult(baselineEntry, variantEntryPatches.ToDictionary(kv => kv.Key, kv => (GameConfigEntryPatch)kv.Value)));
            }

            /// <summary>
            /// Parse experimentId-variantId pair from source sheet syntax: ExperimentId/VariantId
            /// </summary>
            ExperimentVariantPair ParseExperimentVariantIdFromSheet(string variantIdStr)
            {
                string[] parts = variantIdStr.Split('/');
                if (parts.Length != 2)
                    throw new ArgumentException($"Invalid experiment variant id '{variantIdStr}' . Expected two slash-separated parts.");

                return new ExperimentVariantPair(
                    PlayerExperimentId.FromString(parts[0]),
                    ExperimentVariantId.FromString(parts[1]));
            }

            public List<VariantConfigItem<TKey, TInfo>> ParseLibrarySheetItems<TKey, TInfo>(string configEntryName, SpreadsheetContent sheet) where TInfo : IGameConfigKey<TKey>, new()
            {
                // Scope build log to input sheet
                GameConfigBuildLog buildLog = BuildLog.WithSource(sheet.SourceInfo);

                GameConfigSyntaxAdapterAttribute[] gameConfigAdapterAttribs = _builder._configTypeInfo.GameConfigType.GetCustomAttributes<GameConfigSyntaxAdapterAttribute>().ToArray();

                GameConfigRepository.GameConfigEntryInfo entryInfo = _builder._configTypeInfo.Entries[configEntryName];
                GameConfigSyntaxAdapterAttribute[] entryAdapterAttribs = entryInfo.MemberInfo.GetCustomAttributes<GameConfigSyntaxAdapterAttribute>().ToArray();

                List<VariantConfigItem<TKey, TInfo>> items;

                bool useLegacyPipeline;

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
                bool hasLegacyAttrib = entryInfo.MemberInfo.GetCustomAttribute<GameConfigUseLegacyParsingPipelineAttribute>() != null;
                bool entryHasAdapterAttribs = entryAdapterAttribs.Any();

                if (entryHasAdapterAttribs && hasLegacyAttrib)
                {
                    // \todo Detect this already at SDK init time, in GameConfigRepository
                    throw new InvalidOperationException(
                        $"Config entry {configEntryName} has both {nameof(GameConfigSyntaxAdapterAttribute)} and {nameof(GameConfigUseLegacyParsingPipelineAttribute)}. " +
                        "Syntax adapters only apply when using the new pipeline.");
                }

                if (_builder._build.DebugForceLegacyParsing)
                {
                    // Debug force overrides everything else.
                    useLegacyPipeline = true;
                }
                else if (entryHasAdapterAttribs)
                {
                    // If adapter is specified, don't use legacy pipeline.
                    useLegacyPipeline = false;
                }
                else
                {
                    // Otherwise, use legacy if opted into (either per-entry or global).
                    useLegacyPipeline = hasLegacyAttrib
                                        || IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.FeatureFlags.UseLegacyConfigParsingPipelineByDefault;
                }

#else
                useLegacyPipeline = false;
#endif

                if (useLegacyPipeline)
                {
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
                    if (_builder._build.DebugOptions.EnableDebugPrints)
                        DebugLog.Info("Using legacy pipeline for parsing {Entry}", configEntryName);
                    items = LegacyParseLibrarySheetItems<TKey, TInfo>(sheet);
#else
                    throw new MetaAssertException("Cannot use legacy config pipeline when METAPLAY_LEGACY_GAMECONFIG_PIPELINE is not defined");
#endif
                }
                else
                {
                    try
                    {
                        GameConfigSyntaxAdapterAttribute[] syntaxAdapterAttribs = gameConfigAdapterAttribs.Concat(entryAdapterAttribs).ToArray();
                        UnknownConfigMemberHandling unknownMemberHandling = _builder._build.Integration.UnknownConfigItemMemberHandling;

                        items = GameConfigParsePipeline.ProcessSpreadsheetLibrary<TKey, TInfo>(
                            buildLog,
                            new GameConfigParseLibraryPipelineConfig(syntaxAdapterAttribs, unknownMemberHandling),
                            sheet)
                            ?.ToList();
                    }
                    catch (Exception ex)
                    {
                        buildLog.Error($"Internal error while parsing game config library", ex);
                        items = null;
                    }
                }

                // Debug print
                if (items != null)
                    DebugPrintLibraryItems(items, $"Items in {sheet.Name}");

                return items;
            }

            public List<VariantConfigStructureMember> ParseStructureSheetMembers<TStructure>(string configEntryName, SpreadsheetContent sheet)
                where TStructure : GameConfigKeyValue, new()
            {
                // Scope build log to input sheet
                GameConfigBuildLog buildLog = BuildLog.WithSource(sheet.SourceInfo);

                bool useLegacyPipeline;

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
                GameConfigRepository.GameConfigEntryInfo entryInfo = _builder._configTypeInfo.Entries[configEntryName];
                useLegacyPipeline = _builder._build.DebugForceLegacyParsing
                                    || entryInfo.MemberInfo.GetCustomAttribute<GameConfigUseLegacyParsingPipelineAttribute>() != null
                                    || IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.FeatureFlags.UseLegacyConfigParsingPipelineByDefault;
#else
                useLegacyPipeline = false;
#endif

                List<VariantConfigStructureMember> members;
                if (useLegacyPipeline)
                {
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
                    if (_builder._build.DebugOptions.EnableDebugPrints)
                        DebugLog.Info("Using legacy pipeline for parsing {Entry}", configEntryName);
                    members = LegacyParseStructureSheetMembers<TStructure>(sheet);
#else
                    throw new MetaAssertException("Cannot use legacy config pipeline when METAPLAY_LEGACY_GAMECONFIG_PIPELINE is not defined");
#endif
                }
                else
                {
                    try
                    {
                        members = GameConfigParsePipeline.ProcessSpreadsheetKeyValue<TStructure>(
                            buildLog,
                            new GameConfigParseKeyValuePipelineConfig(),
                            sheet)
                            ?.ToList();
                    }
                    catch (Exception ex)
                    {
                        buildLog.Error($"Internal error while parsing game config object", ex);
                        members = null;
                    }
                }

                // Debug print
                if (members != null)
                    DebugPrintStructureMembers(members, $"Members in {sheet.Name}");

                return members;
            }

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
            List<VariantConfigItem<TRef, TItem>> LegacyParseLibrarySheetItems<TRef, TItem>(SpreadsheetContent sheet) where TItem : IGameConfigKey<TRef>, new()
            {
                sheet = DoCommonSheetPreprocessing(sheet);

                // Transpose variant columns to rows
                var rows = GameConfigHelper.PreprocessVariants(sheet);

                SpreadsheetLibraryParser<TRef,TItem> parser = new SpreadsheetLibraryParser<TRef, TItem>(
                    sheet.Name,
                    rows[0].Row,
                    passes: new SpreadsheetParserPass<TItem>[]
                    {
                        new DirectMemberParserPass<TItem>(),
                        new ValueCollectionParserPass<TItem>(),
                        new MetaScheduleParserPass<TItem>(),
                    },
                    _builder._build.SpreadsheetParseOptions,
                    _builder._build.DebugOptions.EnableDebugPrints);

                // Parse items without variants
                List<VariantConfigItem<TRef,TItem>> configItems = parser.ParseRows(rows.Where(x=>x.VariantKey == null).Skip(1).ToList());

                var variantRows = rows.Where(x => x.VariantKey != null).ToList();
                if (variantRows.Count > 0)
                {
                    var variantItems = MatchAndParseVariantRows(parser, variantRows, rows[0], configItems);
                    configItems.AddRange(variantItems);
                }

                return configItems;
            }

            List<VariantConfigItem<TRef, TItem>> MatchAndParseVariantRows<TRef, TItem>(
                SpreadsheetLibraryParser<TRef,TItem> parser,
                List<GameConfigHelper.RowWithVariantAssociation> variantRows,
                GameConfigHelper.RowWithVariantAssociation headerRow,
                List<VariantConfigItem<TRef, TItem>> baselineConfigItems)
                where TItem : IGameConfigKey<TRef>, new()
            {
                // Parse the non-expanded variants
                List<VariantConfigItem<TRef,TItem>> variantItems = parser.ParseRows(variantRows, true, validateVariant: true);

                // If a variant has a configKey defined, and we find that configKey in the baselineConfigItems,
                // then we update the baseline of the variant to match to the baseline item.
                // This then ensures that we use the data from the correct baseline instead of the previous row.
                MatchConfigKeyAndUpdateBaselineRow(baselineConfigItems, variantItems);

                // Expand all missing fields
                int aliasColumn = headerRow.Row.FindIndex(x => x.Value.Equals(GameConfigHelper.AliasColumnName, StringComparison.InvariantCultureIgnoreCase));
                foreach (GameConfigHelper.RowWithVariantAssociation rowWithVariantAssociation in variantRows)
                {
                    try
                    {
                        rowWithVariantAssociation.ExpandFromBaseline(aliasColumn);
                    }
                    catch (ConfigParserException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new ConfigParserException("Exception occured while trying to construct patches", rowWithVariantAssociation, ex);
                    }
                }

                // Parse the expanded variants
                variantItems = parser.ParseRows(variantRows);
                return variantItems;
            }

            static void MatchConfigKeyAndUpdateBaselineRow<TRef, TItem>(List<VariantConfigItem<TRef, TItem>> baselineConfigItems, List<VariantConfigItem<TRef, TItem>> variantItems) where TItem : IGameConfigKey<TRef>, new()
            {
                for (int i = 0; i < variantItems.Count; i++)
                {
                    VariantConfigItem<TRef, TItem> variantConfigItem = variantItems[i];
                    if (variantConfigItem.Item.ConfigKey != null)
                    {
                        foreach (VariantConfigItem<TRef, TItem> baselineItem in baselineConfigItems)
                        {
                            if (baselineItem.Item.ConfigKey.Equals(variantConfigItem.Item.ConfigKey))
                            {
                                variantConfigItem.SourceRow.UpdateBaseline(baselineItem.SourceRow);
                                variantItems[i] = variantConfigItem;
                                break;
                            }
                        }
                    }

                    if(variantConfigItem.VariantIdMaybe != null && variantConfigItem.SourceRow.Baseline == null)
                        throw new ConfigParserException(
                            $"Variant row {variantConfigItem.VariantIdMaybe} has missing baseline, this is likely because it is the first row in the sheet and does not have use patching by key, which is not supported.",
                            variantConfigItem.SourceRow);
                }
            }

            List<VariantConfigStructureMember> LegacyParseStructureSheetMembers<TStructure>(SpreadsheetContent sheet)
                where TStructure : GameConfigKeyValue, new()
            {
                sheet = DoCommonSheetPreprocessing(sheet);

                List<GameConfigHelper.RowWithVariantAssociation> rows           = GameConfigHelper.PreprocessVariants(sheet);
                int                                              aliasColumnIdx = rows[0].Row.FindIndex(x => x.Value.Equals(GameConfigHelper.AliasColumnName, StringComparison.InvariantCultureIgnoreCase));

                foreach (GameConfigHelper.RowWithVariantAssociation row in rows)
                    row.ExpandFromBaseline(aliasColumnIdx);

                // Parse all members
                List<VariantConfigStructureMember> members = SpreadsheetKeyValueStructureParser<TStructure>.ParseSheetWithExpandedVariants(sheet.Name, rows);

                return members;
            }
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE

            SpreadsheetContent DoCommonSheetPreprocessing(SpreadsheetContent sheet)
            {
                // Perform various validations to the spreadsheet
                GameConfigHelper.ValidateNoGoogleSheetErrors(sheet); // Check for Google Sheet error values (eg '#REF!' or '#NULL!')
                                                                     //GameConfigHelper.ValidateNoExtraCellsOnDataRows(sheet);
                                                                     //GameConfigHelper.ValidateSomethingSomething(sheet);

                // Filter empty rows from sheet
                sheet = GameConfigHelper.FilterEmptyRows(sheet);

                // Filter rows whose first cell starts with '//'
                sheet = GameConfigHelper.FilterCommentRows(sheet);

                return sheet;
            }

            void DebugPrintLibraryItems<TRef, TItem>(IEnumerable<VariantConfigItem<TRef, TItem>> items, string title) where TItem : IGameConfigKey<TRef>
            {
                if (!_builder._build.DebugOptions.EnableDebugPrints)
                    return;
                DebugLog.Info("{Title}:\n{Items}\n", title, string.Join("\n", items.Select((item, ndx) =>
                {
                    string variantStr = item.VariantIdMaybe != null ? $" ({item.VariantIdMaybe})" : "";
                    return Invariant($"  #{ndx}{variantStr}: {PrettyPrint.Compact(item.Item)}");
                })));
            }

            void DebugPrintStructureMembers(IEnumerable<VariantConfigStructureMember> members, string title)
            {
                if (!_builder._build.DebugOptions.EnableDebugPrints)
                    return;
                DebugLog.Info("{Title}:\n{Members}\n", title, string.Join("\n", members.Select((member, ndx) =>
                {
                    string variantStr = member.VariantIdMaybe != null ? $" ({member.VariantIdMaybe})" : "";
                    return Invariant($"  #{ndx}{variantStr}: {member.Member.MemberInfo.Name} = {PrettyPrint.Compact(member.Member.MemberValue)}");
                })));
            }

            #region Utilities for building custom-format (non-mpc) config entries

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
            public GameConfigLibrary<TKey, TValue> ParseCsvLibrary<TKey, TValue>(string fileName, byte[] bytes) where TValue : class, IGameConfigData<TKey>, new()
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                {
                    return GameConfigLibrary<TKey, TValue>.CreateSolo(
                        GameConfigUtil.ParseCsvLibraryItems<TKey, TValue>(_builder._build.CsvParseOptions, fileName, stream));
                }
            }

            public T ParseCsvKeyValueStructure<T>(string fileName, byte[] bytes) where T : GameConfigKeyValue, new()
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                    return GameConfigUtil.ParseCsvKeyValueStructure<T>(fileName, stream);
            }
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE

            public GameConfigLibrary<TKey, TValue> ParseJsonLibrary<TKey, TValue>(string fileName, byte[] bytes) where TValue : class, IGameConfigData<TKey>, new()
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                {
                    return GameConfigLibrary<TKey, TValue>.CreateSolo(
                        GameConfigUtil.ParseJsonLibraryItems<TKey, TValue>(fileName, stream));
                }
            }

#endregion
        }
    }

    struct GameConfigBuildOutput
    {
        public readonly ConfigArchive Baseline;
        public readonly Dictionary<ExperimentVariantPair, GameConfigPatchEnvelope>  VariantPatches;

        public GameConfigBuildOutput(ConfigArchive baseline, Dictionary<ExperimentVariantPair, GameConfigPatchEnvelope> variantPatches)
        {
            Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
            VariantPatches = variantPatches ?? throw new ArgumentNullException(nameof(variantPatches));
        }
    }

    readonly struct BuildSourceFetcherAndMetadata
    {
        public readonly IGameConfigSourceFetcher      Fetcher;
        public readonly GameConfigBuildSourceMetadata Metadata;

        public BuildSourceFetcherAndMetadata(IGameConfigSourceFetcher fetcher, GameConfigBuildSourceMetadata metadata)
        {
            Fetcher  = fetcher;
            Metadata = metadata;
        }
    }

    public abstract class GameConfigBuild : IMetaIntegrationConstructible<GameConfigBuild>, IGameConfigBuild
    {
        public    GameConfigBuildDebugOptions      DebugOptions          { get; set; }
        public    IGameConfigSourceFetcherProvider SourceFetcherProvider { get; set; }
        public    GameConfigBuildIntegration       Integration           { get; set; } = IntegrationRegistry.Get<GameConfigBuildIntegration>();
        protected Type                             SharedConfigType      { get; set; } = GameConfigRepository.Instance.SharedGameConfigType;
        protected Type                             ServerConfigType      { get; set; } = GameConfigRepository.Instance.ServerGameConfigType;
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public bool DebugForceLegacyParsing { get; set; } = ConfigBuildDevelopmentDebug.ForceLegacy;
        protected virtual SpreadsheetParseOptions SpreadsheetParseOptions { get; } = SpreadsheetParseOptions.Default;
        protected virtual CsvParseOptions CsvParseOptions { get; } = CsvParseOptions.Default;
        SpreadsheetParseOptions IGameConfigBuild.SpreadsheetParseOptions => SpreadsheetParseOptions;
        CsvParseOptions IGameConfigBuild.CsvParseOptions => CsvParseOptions;
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE

        readonly Dictionary<GameConfigBuildSource, BuildSourceFetcherAndMetadata> _sources = new Dictionary<GameConfigBuildSource, BuildSourceFetcherAndMetadata>();
        protected readonly OrderedDictionary<string, GameConfigBuildSource> _sourcesByMemberName = new OrderedDictionary<string, GameConfigBuildSource>();
        protected IGameConfigSourceFetcher SourceFetcher(GameConfigBuildSource source) => _sources[source].Fetcher;

        async Task ConfigureSourceFetchersAsync(GameConfigBuildParameters buildParams, CancellationToken ct)
        {
            // Iterate all GameConfigBuildSource members of build params and configure a source fetcher for each unique source.
            foreach (MemberInfo sourceMember in Integration.GetBuildSourcesInBuildParameters(buildParams.GetType()))
            {
                GameConfigBuildSource source = (GameConfigBuildSource)sourceMember.GetDataMemberGetValueOnDeclaringType().Invoke(buildParams);
                if (source != null)
                {
                    _sourcesByMemberName[sourceMember.Name] = source;
                    if (!_sources.ContainsKey(source))
                    {
                        IGameConfigSourceFetcher      fetcher  = await ConfigureFetcherForBuildSourceAsync(source, ct);
                        GameConfigBuildSourceMetadata metadata = await fetcher.GetMetadataAsync(ct);
                        _sources[source] = new BuildSourceFetcherAndMetadata(fetcher, metadata);
                    }
                }
            }
        }

        OrderedDictionary<string, GameConfigBuildSourceMetadata> GetBuildSourcesMetadata()
        {
            OrderedDictionary<string, GameConfigBuildSourceMetadata> retVal = new OrderedDictionary<string, GameConfigBuildSourceMetadata>();
            foreach (OrderedDictionary<string, GameConfigBuildSource>.KeyValue source in _sourcesByMemberName)
            {
                GameConfigBuildSourceMetadata metadata = _sources[source.Value].Metadata;
                if (metadata != null)
                    retVal[source.Key] = metadata;
            }
            return retVal;
        }

        protected virtual Task<IGameConfigSourceFetcher> ConfigureFetcherForBuildSourceAsync(GameConfigBuildSource source, CancellationToken ct)
        {
            return SourceFetcherProvider.GetFetcherForBuildSourceAsync(source, ct);
        }

        ConfigArchive MaybeExtractParentArchive(ConfigArchive parentFullArchive, string entryName)
        {
            return parentFullArchive != null ? ConfigArchive.FromBytes(parentFullArchive.GetEntryByName(entryName).Bytes) : null;
        }

        GameConfigBuildState<ISharedGameConfig> CreateSharedGameConfigBuilder(GameConfigBuildLog buildLog, ConfigArchive parentArchive)
        {
            ConfigArchive sharedParent = MaybeExtractParentArchive(parentArchive, "Shared.mpa");
            ISharedGameConfig output;
            if (sharedParent != null)
                output = GameConfigUtil.ImportSharedConfig(sharedParent, isBuildingConfigs: true, isConfigBuildParent: true);
            else
                output = (ISharedGameConfig)GameConfigFactory.Instance.CreateGameConfig(SharedConfigType);
            return new GameConfigBuildState<ISharedGameConfig>(this, sharedParent, buildLog, output);
        }

        GameConfigBuildState<IServerGameConfig> CreateServerGameConfigBuilder(GameConfigBuildLog buildLog, ConfigArchive parentArchive)
        {
            ConfigArchive serverParent = MaybeExtractParentArchive(parentArchive, "Server.mpa");
            IServerGameConfig output;
            if (serverParent != null)
                output = GameConfigUtil.ImportServerConfig(serverParent, isBuildingConfigs: true, isConfigBuildParent: true);
            else
                output = (IServerGameConfig)GameConfigFactory.Instance.CreateGameConfig(ServerConfigType);
            return new GameConfigBuildState<IServerGameConfig>(this, serverParent, buildLog, output);
        }

        void ValidateThatUsedVariantsAreConfigured(GameConfigBuildOutput sharedOutput, GameConfigBuildOutput serverOutput)
        {
            IServerGameConfig server = GameConfigUtil.ImportServerConfig(serverOutput.Baseline, isBuildingConfigs: true);

            HashSet<ExperimentVariantPair> configuredExperimentVariantKeys =
                new HashSet<ExperimentVariantPair>(
                    server.PlayerExperiments.Values
                    .SelectMany(experimentInfo =>
                        experimentInfo.Variants.Values.Select(variant => new ExperimentVariantPair(experimentInfo.ExperimentId, variant.Id))));

            foreach ((ExperimentVariantPair key, GameConfigPatchEnvelope patch) in sharedOutput.VariantPatches)
            {
                if (!configuredExperimentVariantKeys.Contains(key))
                    throw new InvalidOperationException($"Shared Game Config entry {patch.EnumeratePatchedEntryNames().First()} has patch for variant '{key.ExperimentId}/{key.VariantId}' which hasn't been configured in the {nameof(IServerGameConfig.PlayerExperiments)} configuration");
            }

            foreach ((ExperimentVariantPair key, GameConfigPatchEnvelope patch) in serverOutput.VariantPatches)
            {
                if (!configuredExperimentVariantKeys.Contains(key))
                    throw new InvalidOperationException($"Server Game Config entry {patch.EnumeratePatchedEntryNames().First()} has patch for variant '{key.ExperimentId}/{key.VariantId}' which hasn't been configured in the {nameof(IServerGameConfig.PlayerExperiments)} configuration");
            }
        }

        List<GameConfigValidationResult> ValidateFullArchive(ConfigArchive fullArchive, List<GameConfigSourceMapping> sharedConfigSourceMapping, List<GameConfigSourceMapping> serverConfigSourceMapping, GameConfigBuildLog buildLog)
        {
            // Validate baseline, by loading it from the archive.
            FullGameConfigImportResources importResources = FullGameConfigImportResources.CreateWithAllConfiguredPatches(
                fullArchive,
                GameConfigRuntimeStorageMode.Deduplicating,
                initializeBaselineAndSingleVariantSpecializations: false);

            importResources.Shared.InitializeDeduplicationBaseline();
            importResources.Server.InitializeDeduplicationBaseline();

            DebugDumpConfigsContainer debugDumpConfigs = default;
            ObjectGraphDump.DumpResult debugDumpBefore = default;
            if (DebugOptions.EnableDebugDumpCheck)
            {
                debugDumpConfigs = new DebugDumpConfigsContainer(importResources);
                debugDumpBefore = DebugDumpConfigs(debugDumpConfigs);
            }

            importResources.Shared.InitializeAllDeduplicationSingleVariantSpecializations();
            importResources.Server.InitializeAllDeduplicationSingleVariantSpecializations();

            FullGameConfig baseline = FullGameConfig.CreateSpecialization(importResources, new OrderedSet<ExperimentVariantPair>(), isBuildingConfigs: true);

            GameConfigValidationResult baselineResult = new GameConfigValidationResult(GameConfigSourceMapping.BaselineVariantKey);

            baselineResult.ScopeTo(sharedConfigSourceMapping);
            baseline.SharedConfig.BuildTimeValidate(baselineResult);

            baselineResult.ScopeTo(serverConfigSourceMapping);
            baseline.ServerConfig.BuildTimeValidate(baselineResult);

            List<GameConfigValidationResult> validationResults = new List<GameConfigValidationResult>();
            validationResults.Add(baselineResult);

            if (!DebugOptions.OnlyValidateBaseline)
            {
                // Validate each configured variant, by loading full config from the archive patched by each variant's patch.
                // \note This does *not* validate all combinations of variants; this validates each variant individually.
                //       Ignoring any custom arbitrary config validations, this should be enough.

                List<ExperimentVariantPair> patchIds =
                    baseline.ServerConfig.PlayerExperiments.Values
                        .SelectMany(
                            experiment =>
                                experiment.Variants.Keys
                                    .Select(variantId => new ExperimentVariantPair(experiment.ExperimentId, variantId)))
                        .ToList();

                List<GameConfigValidationResult> variantResults =
                    patchIds
                        .Select(patchId => new GameConfigValidationResult(patchId.ExperimentId.Value + "/" + patchId.VariantId.Value))
                        .ToList();

                Parallel.For(
                    0,
                    patchIds.Count,
                    index =>
                    {
                        ExperimentVariantPair      patchId       = patchIds[index];
                        GameConfigValidationResult variantResult = variantResults[index];

                        // Skip empty patches, nothing to validate compared to baseline
                        if (importResources.Shared.Patches[patchId].IsEmpty
                            && importResources.Server.Patches[patchId].IsEmpty)
                        {
                            return;
                        }

                        FullGameConfig variantConfig = FullGameConfig.CreateSpecialization(
                            importResources,
                            new OrderedSet<ExperimentVariantPair> {patchId},
                            omitPatchesInServerConfigExperiments: true,
                            isBuildingConfigs: true);

                        variantResult.ScopeTo(sharedConfigSourceMapping);
                        variantConfig.SharedConfig.BuildTimeValidate(variantResult);

                        variantResult.ScopeTo(serverConfigSourceMapping);
                        variantConfig.ServerConfig.BuildTimeValidate(variantResult);

                        variantResult.DisposeScope();
                    });

                validationResults = validationResults.Concat(variantResults).ToList();
            }
            else
            {
                buildLog.Warning("Only baseline config has been validated");
            }

            if (DebugOptions.EnableDebugDumpCheck)
            {
                ObjectGraphDump.DumpResult debugDumpAfter = DebugDumpConfigs(debugDumpConfigs, referenceDump: debugDumpBefore);
                CompareConfigDebugDumps(debugDumpBefore, debugDumpAfter);
            }

            return validationResults;
        }

        class DebugDumpConfigsContainer
        {
            public ISharedGameConfig SharedGameConfig;
            public IServerGameConfig ServerGameConfig;

            public DebugDumpConfigsContainer(FullGameConfigImportResources importResources)
            {
                SharedGameConfig = (ISharedGameConfig)importResources.Shared.DeduplicationBaseline;
                ServerGameConfig = (IServerGameConfig)importResources.Server.DeduplicationBaseline;
            }
        }

        ObjectGraphDump.FieldInfoCache _debugDumpFieldInfoCache = new ObjectGraphDump.FieldInfoCache();

        ObjectGraphDump.DumpResult DebugDumpConfigs(DebugDumpConfigsContainer container, ObjectGraphDump.DumpResult referenceDump = null)
        {
            int totalConfigItemCount = CountConfigItems(container.SharedGameConfig)
                + CountConfigItems(container.ServerGameConfig);

            // Add some overhead for object count limit.
            // Even a 0-item config involves some objects.
            const int objectCountOverhead = 2000;

            int objectCollectionCapacity;
            if (referenceDump != null)
            {
                // Optimal capacity for the common case (i.e. identical dumps).
                objectCollectionCapacity = referenceDump.Objects.Count;
            }
            else
                objectCollectionCapacity = 10 * totalConfigItemCount;

            ObjectGraphDump.DumpOptions dumpOptions = new ObjectGraphDump.DumpOptions(
                objectCountSafetyLimit: 1000 * totalConfigItemCount + objectCountOverhead,
                objectCollectionInitialCapacity: objectCollectionCapacity);

            dumpOptions.FieldInfoCache = _debugDumpFieldInfoCache;

            if (DebugOptions.EnableDebugPrints)
                DebugLog.Debug($"Debug-dumping baseline config (using {nameof(dumpOptions.ObjectCountSafetyLimit)} = {{ObjectCountSafetyLimit}})...", dumpOptions.ObjectCountSafetyLimit);
            Stopwatch sw = Stopwatch.StartNew();

            ObjectGraphDump.DumpResult dumpResult = ObjectGraphDump.Dump(container, dumpOptions);

            if (DebugOptions.EnableDebugPrints)
                DebugLog.Debug("... dumping took {DumpMilliseconds} ms, dump contains {NumObjectsInDump} objects", sw.ElapsedMilliseconds, dumpResult.Objects.Count);

            return dumpResult;
        }

        void CompareConfigDebugDumps(ObjectGraphDump.DumpResult dumpBefore, ObjectGraphDump.DumpResult dumpAfter)
        {
            // Happy path: strict comparison reports no differences, all is OK.
            // If it does, then perform a more loose comparison
            // (by not requiring object identities to equal between the dumps),
            // and prefer to use that looser-comparison result if it still produces differences,
            // because they're likely to have more information (i.e. actual content differences,
            // not just identity differences).

            ObjectGraphDump.ComparisonResult comparisonResult = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);
            if (!comparisonResult.DumpsAreEqual)
            {
                ObjectGraphDump.ComparisonResult looserComparisonResult = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter, compareObjectRuntimeIdentities: false);
                if (!looserComparisonResult.DumpsAreEqual)
                    comparisonResult = looserComparisonResult;

                throw new InvalidOperationException(
                    "Config baseline instance was modified by the construction of specialized configs! " +
                    "The following difference was found (see next line):\n" +
                    $"    {comparisonResult.Description}\n" +
                    "This likely means that a config's OnLoaded method modified items that were " +
                    "not owned by that game config instance (due to sharing of config items for deduplication). " +
                    "This should be fixed as it can cause to unpredictable bugs at runtime.");
            }
        }

        int CountConfigItems(IGameConfig gameConfig)
        {
            int totalCount = 0;

            GameConfigRepository.GameConfigTypeInfo configTypeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(gameConfig.GetType());
            foreach (GameConfigRepository.GameConfigEntryInfo entryInfo in configTypeInfo.Entries.Values)
            {
                if (entryInfo.MemberInfo.GetDataMemberType().IsGameConfigLibrary())
                {
                    IGameConfigLibraryEntry library = (IGameConfigLibraryEntry)entryInfo.MemberInfo.GetDataMemberGetValueOnDeclaringType()(gameConfig);
                    totalCount += library.Count;
                }
            }

            return totalCount;
        }

        protected abstract Task BuildAsync(IGameConfigBuilderProvider<ISharedGameConfig> shared, IGameConfigBuilderProvider<IServerGameConfig> server, GameConfigBuildParameters parameters);

        public async Task<ConfigArchive> CreateArchiveAsync(MetaTime createdAt, GameConfigBuildParameters buildParams, MetaGuid parentId, ConfigArchive parent)
        {
            if (ConfigBuildDevelopmentDebug.ForceFixedArchiveTimestamp)
                createdAt = MetaTime.Epoch;

            // Fill in default debug options if not given
            DebugOptions ??= new GameConfigBuildDebugOptions();

            // Sanity check, cannot do incremental build without parent.
            if (((buildParams?.IsIncremental) ?? false) && parent == null)
                throw new ArgumentException("Incremental config build cannot be done without a parent config", nameof(parent));

            // Measure total build time
            Stopwatch swTotal = Stopwatch.StartNew();

            // Create build log for collecting all messages during build
            GameConfigBuildLog buildLog = new GameConfigBuildLog();

            // Initialize builders
            GameConfigBuildState<ISharedGameConfig> shared = CreateSharedGameConfigBuilder(buildLog, parent);
            GameConfigBuildState<IServerGameConfig> server = CreateServerGameConfigBuilder(buildLog, parent);

            // Get patches
            if (parent != null)
                ExtractPatches(parent, out server.ParentVariantPatches, out shared.ParentVariantPatches);

            // Initialize fetchers for build sources and get metadata
            if (buildParams != null)
                await ConfigureSourceFetchersAsync(buildParams, CancellationToken.None);

            // Run actual build (\note outputs errors into buildLog)
            try
            {
                await BuildAsync(shared, server, buildParams);
            }
            catch (Exception ex)
            {
                buildLog.Error("Unhandled exception during game config build", ex);
            }

            // If build failed, bail out with a report
            if (buildLog.HasErrors())
                throw new GameConfigBuildFailed(new GameConfigBuildReport(buildLog.Messages, validationResults: null));

            (GameConfigBuildOutput sharedOutput, GameConfigBuildOutput serverOutput) = FinalizeBuildStates(buildLog, createdAt, shared, server);

            // If finalization failed (e.g. MetaRef resolve failures), bail out with a report
            if (buildLog.HasErrors())
                throw new GameConfigBuildFailed(new GameConfigBuildReport(buildLog.Messages, validationResults: null));

            // Validate that the config doesn't have patches for variants that are not configured in the PlayerExperiments config.
            ValidateThatUsedVariantsAreConfigured(sharedOutput, serverOutput);

            // Construct full archive, containing shared & server configs as well as their patches.
            List<ConfigArchiveEntry> sharedPatchEntries =
                sharedOutput.VariantPatches
                .Select(kv =>
                {
                    (ExperimentVariantPair key, GameConfigPatchEnvelope patch) = kv;
                    return ConfigArchiveEntry.FromBlob($"SharedPatch.{key.ExperimentId}.{key.VariantId}.mpp", patch.Serialize());
                }).ToList();

            List<ConfigArchiveEntry> serverPatchEntries =
                serverOutput.VariantPatches
                .Select(kv =>
                {
                    (ExperimentVariantPair key, GameConfigPatchEnvelope patch) = kv;
                    return ConfigArchiveEntry.FromBlob($"ServerPatch.{key.ExperimentId}.{key.VariantId}.mpp", patch.Serialize());
                }).ToList();

            List<ConfigArchiveEntry> allEntries = sharedPatchEntries
                .Concat(serverPatchEntries)
                .Append(ConfigArchiveEntry.FromBlob("Shared.mpa", ConfigArchiveBuildUtility.ToBytes(sharedOutput.Baseline, CompressionAlgorithm.None, 0)))
                .Append(ConfigArchiveEntry.FromBlob("Server.mpa", ConfigArchiveBuildUtility.ToBytes(serverOutput.Baseline, CompressionAlgorithm.None, 0))).ToList<ConfigArchiveEntry>();
            ConfigArchive fullArchive = new ConfigArchive(createdAt, allEntries);

            if (DebugOptions.EnableDebugPrints)
            {
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
                DebugLog.Info($"Force legacy parsing = {DebugForceLegacyParsing}");
#endif
                DebugLog.Info($"Timestamp = {createdAt}");

                IReadOnlyList<ConfigArchiveEntry> sharedEntries = sharedOutput.Baseline.Entries;
                IReadOnlyList<ConfigArchiveEntry> serverEntries = serverOutput.Baseline.Entries;
                DebugLog.Info("Full archive hash (before adding _metadata): {FullArchiveContentHash}", fullArchive.Version);
                DebugLog.Info($"Shared config entries ({sharedEntries.Count}):{string.Concat(sharedEntries.Select(entry => $"\n  {entry.Name} ({entry.Bytes.Length} bytes): {entry.Hash}"))}");
                DebugLog.Info($"Server config entries ({serverEntries.Count}):{string.Concat(serverEntries.Select(entry => $"\n  {entry.Name} ({entry.Bytes.Length} bytes): {entry.Hash}"))}");
                DebugLog.Info($"Shared config patch entries ({sharedPatchEntries.Count}):{string.Concat(sharedPatchEntries.Select(entry => $"\n  {entry.Name} ({entry.Bytes.Length} bytes): {entry.Hash}"))}");
                DebugLog.Info($"Server config patch entries ({serverPatchEntries.Count}):{string.Concat(serverPatchEntries.Select(entry => $"\n  {entry.Name} ({entry.Bytes.Length} bytes): {entry.Hash}"))}\n");
            }

            // Validate all variant combinations from the full archive
            List<GameConfigValidationResult> validationResults = ValidateFullArchive(fullArchive, shared.SourceMappings, server.SourceMappings, buildLog);

            // Log information about build time
            buildLog.Information(Invariant($"Total build time: {swTotal.ElapsedMilliseconds / 1000.0:.00}s"));

            // Build a report out of the build (with build and validation logs, if applicable)
            GameConfigBuildReport report = new GameConfigBuildReport(buildLog.Messages, validationResults);
            report.PrintToConsole();

            // Add build metadata as custom archive entry named "_metadata"
            GameConfigMetaData metaData = new GameConfigMetaData(
                parentConfigId: parentId,
                parentConfigHash: parent?.Version ?? ContentHash.None,
                buildParams: buildParams,
                buildReport: report,
                buildSourceMetadata: GetBuildSourcesMetadata());
            byte[] metaDataBytes = metaData.ToBytes();
            allEntries.Add(ConfigArchiveEntry.FromBlob("_metadata", metaDataBytes));

            // Return the full server archive (with variants and metadata)
            return new ConfigArchive(createdAt, allEntries);
        }

        static (GameConfigBuildOutput sharedOutput, GameConfigBuildOutput serverOutput) FinalizeBuildStates(GameConfigBuildLog buildLog, MetaTime createdAt, GameConfigBuildState<ISharedGameConfig> shared, GameConfigBuildState<IServerGameConfig> server)
        {
            // \todo Handle MetaRefException during the traverse so can collect all failures instead of just one?

            GameConfigBuildOutput sharedOutput = default;
            try
            {
                sharedOutput = shared.Finalize(createdAt);
            }
            catch (MetaRefResolveError ex)
            {
                // Log the error into build log
                LogMetaRefResolveError(buildLog, ex, shared.SourceMappings);
            }

            GameConfigBuildOutput serverOutput = default;
            try
            {
                serverOutput = server.Finalize(createdAt);
            }
            catch (MetaRefResolveError ex)
            {
                // Log the error into build log
                LogMetaRefResolveError(buildLog, ex, server.SourceMappings);
            }

            return (sharedOutput, serverOutput);
        }

        /// <summary>
        /// Report the <paramref name="ex"/> into the <paramref name="buildLog"/>. Tries to find the
        /// source location of the error from the <paramref name="sourceMappings"/> on a best effort
        /// basis. If the source wasn't found, non-sourced error is still written to the build log.
        /// </summary>
        /// <param name="buildLog"></param>
        /// <param name="ex"></param>
        /// <param name="configItem"></param>
        /// <param name="sourceMappings"></param>
        static void LogMetaRefResolveError(GameConfigBuildLog buildLog, MetaRefResolveError ex, List<GameConfigSourceMapping> sourceMappings)
        {
            IGameConfigData configItem = ex.ConfigItem;
            // \todo Using the GameConfigEntryName for KeyValueObjects, need to clean it up
            string configKeyStr = configItem != null ? Util.ObjectToStringInvariant(GameConfigItemHelper.GetItemConfigKey(configItem)) : ex.GameConfigEntryName;

            foreach (GameConfigSourceMapping sourceMapping in sourceMappings)
            {
                // \todo Comparing spreadsheet name vs library name which is not correct
                if (sourceMapping.SourceInfo is GameConfigSpreadsheetSourceInfo spreadsheetInfo && ex.GameConfigEntryName == spreadsheetInfo.GetSheetName())
                {
                    // \todo Can we get a memberPathHint for the error (ie, where the MetaRef causing the error was a member of)?
                    if (sourceMapping.TryFindItemSource(configKeyStr, variantId: null, memberPathHint: null, out GameConfigSourceLocation itemLocation))
                    {
                        // \todo It's a bit wonky explicitly give itemLocation.SourceInfo -- we could rely on the itemLocation only as it knows its SourceInfo already?
                        buildLog.WithSource(itemLocation.SourceInfo).WithLocation(itemLocation).Error(Invariant($"Failed to resolve MetaRef in {configKeyStr}"), ex);
                        return;
                    }
                }
            }

            // Wasn't able to find proper source mapping for where this happened so report without the information
            buildLog.Error(Invariant($"Failed to resolve MetaRef in {configKeyStr}. Unable to resolve source location where this occurred."), ex);
        }

        void ExtractPatches(ConfigArchive parent,
            out Dictionary<ExperimentVariantPair, GameConfigPatch> serverPatches,
            out Dictionary<ExperimentVariantPair, GameConfigPatch> sharedPatches)
        {
            FullGameConfig parentFullConfig = FullGameConfig.CreateSoloUnpatched(parent);

            Dictionary<ExperimentVariantPair, FullGameConfigPatch> parentVariantPatches =
                parentFullConfig.ServerConfig.PlayerExperiments.Values
                .SelectMany(experiment => experiment.Variants.Values.Select(variant => (experiment, variant)))
                .ToDictionary(
                    ev => new ExperimentVariantPair(ev.experiment.ExperimentId, ev.variant.Id),
                    ev => ev.variant.ConfigPatch);

            sharedPatches = parentVariantPatches.ToDictionary(
                ev => ev.Key,
                ev => ev.Value.SharedConfigPatch);

            serverPatches = parentVariantPatches.ToDictionary(
                ev => ev.Key,
                ev => ev.Value.ServerConfigPatch);
        }
    }

    /// <summary>
    /// A GameConfig build implementation that exposes configuring the build per config entry.
    /// </summary>
    ///
    /// The implementation iterates over all of the registed config entries in Shared and Server game
    /// configs and allows derived classes to override the data fetching and building per named
    /// entry.
    ///
    /// A derived class can override the build of individual entries via overriding the <see cref="GetEntryBuilder"/>
    /// method called for each config entry in the shared and server game configs.
    ///
    /// The default implementation of entry building uses the overrideable <see cref="DefaultDataSource"/> property
    /// for supplying an appropriate entry builder. The intention is that most config entries are buildable
    /// via the default implementation and that the integration only needs to override entry builders for
    /// more advanced use cases.
    ///
    /// <typeparam name="TSharedConfig"></typeparam>
    /// <typeparam name="TServerConfig"></typeparam>
    /// <typeparam name="TBuildParameters"></typeparam>
    public abstract class GameConfigBuildTemplate<TSharedConfig, TServerConfig, TBuildParameters> : GameConfigBuild
        where TSharedConfig : ISharedGameConfig
        where TServerConfig : IServerGameConfig
        where TBuildParameters : GameConfigBuildParameters, new()
    {
        protected struct ConfigEntryBuilder
        {
            public Func<Task>                     FetchDataAsync;
            public Func<IGameConfigBuilder, Task> BuildAsync;

            public ConfigEntryBuilder(Func<Task> fetchData, Func<IGameConfigBuilder, Task> build)
            {
                FetchDataAsync = fetchData;
                BuildAsync     = build;
            }

            public static ConfigEntryBuilder Create<TConfigType>(Func<Task> fetchData, Action<IGameConfigBuilder<TConfigType>> build) where TConfigType : IGameConfig
            {
                return new ConfigEntryBuilder(
                    fetchData,
                    (builder) =>
                    {
                        build((IGameConfigBuilder<TConfigType>)builder);
                        return Task.CompletedTask;
                    });
            }
        }

        protected TBuildParameters BuildParameters { get; private set; }

        (Type ConfigType, GameConfigRepository.GameConfigEntryInfo Entry)? _currentConfigEntry = null;

        IEnumerable<GameConfigRepository.GameConfigEntryInfo> GetConfigEntriesToBuild(Type gameConfigType)
        {
            return GameConfigRepository.Instance.GetGameConfigTypeInfo(gameConfigType).Entries.Values;
        }

        ConfigEntryBuilder GetDefaultEntryBuilder(Type configType, string entryName)
        {
            MetaDebug.Assert(configType == _currentConfigEntry?.ConfigType, $"Called {nameof(GetDefaultEntryBuilder)} for config type '{configType}' when configuring config type '{_currentConfigEntry?.ConfigType}'");
            MetaDebug.Assert(entryName == _currentConfigEntry?.Entry.Name, $"Called {nameof(GetDefaultEntryBuilder)} for entry '{entryName}' when configuring entry '{_currentConfigEntry?.Entry.Name}'");

            if (_currentConfigEntry?.Entry.BuildParamsSourceProperty != null || BuildParameters.DefaultSource != null)
            {
                return GenericSpreadSheetEntryBuilder(entryName);
            }
            // Note: the below hacks will be removed
            else if (configType.ImplementsInterface(typeof(ISharedGameConfig)) && entryName == "Languages")
            {
                // Supply english as a default language
                return AssignLibraryItemsBuilder<LanguageId, LanguageInfo>(Enumerable.Repeat(new LanguageInfo(LanguageId.FromString("en"), "English"), 1));
            }
            else
            {
                return AssignEmptyEntryBuilder();
            }
        }

        IEnumerable<(IGameConfigBuilder, ConfigEntryBuilder)> GetConfigEntryBuilders(IEnumerable<IGameConfigBuilder> configBuilders)
        {
            List<(IGameConfigBuilder, ConfigEntryBuilder)> entryBuilders = new List<(IGameConfigBuilder, ConfigEntryBuilder)>();
            foreach ((IGameConfigBuilder configBuilder, GameConfigRepository.GameConfigEntryInfo entry) in configBuilders.SelectMany(x => GetConfigEntriesToBuild(x.ConfigType).Select(y => (x, y))))
            {
                ConfigEntryBuilder? builder = GetEntryBuilderWrapper(configBuilder.ConfigType, entry);
                if (builder.HasValue)
                    entryBuilders.Add((configBuilder, builder.Value));
            }
            return entryBuilders;
        }

        protected IGameConfigSourceData FetchSourceData(string itemName, GameConfigBuildSource source)
        {
            // \todo: error handling
            IGameConfigSourceFetcher fetcher = SourceFetcher(source);
            return fetcher.Fetch(itemName);
        }

        protected IGameConfigSourceData FetchSourceData(string itemName, string sourcePropertyName)
        {
            // \todo: error handling
            return FetchSourceData(itemName, _sourcesByMemberName[sourcePropertyName]);
        }

        protected Func<IGameConfigSourceData> GetGenericFetchFunc(string itemName, GameConfigBuildSource source = null)
        {
            if (source == null)
            {
                if (_currentConfigEntry?.Entry.BuildParamsSourceProperty is
                    { } sourceProperty)
                {
                    if (!_sourcesByMemberName.TryGetValue(sourceProperty, out source))
                    {
                        // check if the property by name given in attribute exists on the params type
                        if (Integration.GetBuildSourcesInBuildParameters(BuildParameters.GetType()).All(x => x.Name != sourceProperty))
                            throw new InvalidOperationException($"The config build for entry {_currentConfigEntry?.Entry.Name} refers to build source parameter {sourceProperty} that does not exist in parameters class {BuildParameters.GetType()}");

                        throw new InvalidOperationException($"The config build for entry {_currentConfigEntry?.Entry.Name} requires a build source to be declared in {BuildParameters.GetType()}.{sourceProperty}");
                    }
                }
                else
                {
                    source = BuildParameters.DefaultSource;
                }
            }

            return () => FetchSourceData(itemName, source);
        }

        protected ConfigEntryBuilder CustomEntryBuildSingleSource<TSourceDataType>(Func<IGameConfigSourceData> fetchFunc, Action<IGameConfigBuilder, TSourceDataType> buildFunc)
        {
            IGameConfigSourceData data = null;
            return new ConfigEntryBuilder()
            {
                FetchDataAsync = () =>
                {
                    data = fetchFunc();
                    return Task.CompletedTask;
                },
                BuildAsync = async (builder) =>
                {
                    object resolvedData = await data.Get();
                    buildFunc(builder, (TSourceDataType)resolvedData);
                }
            };
        }

        protected ConfigEntryBuilder CustomEntryBuildSingleSource<TSourceDataType>(string sourceItemName, Action<IGameConfigBuilder, TSourceDataType> buildFunc)
        {
            return CustomEntryBuildSingleSource(GetGenericFetchFunc(sourceItemName, null), buildFunc);
        }

        protected ConfigEntryBuilder GenericSpreadSheetEntryBuilder(string sourceItemName)
        {
            string entryName = _currentConfigEntry?.Entry.Name;
            return CustomEntryBuildSingleSource<SpreadsheetContent>(sourceItemName, (builder, sourceData) => builder.BuildGameConfigEntry(entryName, sourceData));
        }

        protected ConfigEntryBuilder GoogleSheetsEntryBuilder(string sheetName)
        {
            return GenericSpreadSheetEntryBuilder(sheetName);
        }

        protected ConfigEntryBuilder CsvEntryBuilder(string filePath)
        {
            return GenericSpreadSheetEntryBuilder(filePath);
        }

        protected ConfigEntryBuilder AssignEmptyEntryBuilder()
        {
            string entryName = _currentConfigEntry?.Entry.Name;
            return new ConfigEntryBuilder()
            {
                FetchDataAsync = null,
                BuildAsync = (builder) =>
                {
                    builder.AssignEmpty(entryName);
                    return Task.CompletedTask;
                },
            };
        }

        protected ConfigEntryBuilder AssignValueEntryBuilder(IGameConfigEntry value)
        {
            string entryName = _currentConfigEntry?.Entry.Name;
            return new ConfigEntryBuilder()
            {
                FetchDataAsync = null,
                BuildAsync = (builder) =>
                {
                    builder.AssignBaselineConfigEntryBuildResult(entryName, value, null);
                    return Task.CompletedTask;
                },
            };
        }

        protected ConfigEntryBuilder AssignLibraryItemsBuilder<TKey, TInfo>(IEnumerable<TInfo> values) where TInfo : class, IGameConfigData<TKey>, new()
        {
            string entryName = _currentConfigEntry?.Entry.Name;
            return new ConfigEntryBuilder()
            {
                FetchDataAsync = null,
                BuildAsync = (builder) =>
                {
                    builder.AssignLibraryBuildResult(entryName, values.Select(x => new VariantConfigItem<TKey, TInfo>(x, null, null, sourceLocation: null)).ToList(), null);
                    return Task.CompletedTask;
                },
            };
        }

        /// <summary>
        /// Configure the data fetching and config building for a config entry in the shared game config.
        /// </summary>
        ///
        /// Returning null from this function disables the building of the particular config entry altogether, resulting
        /// in the entry to remain at its default value or its previous value if this is an incremental build.
        ///
        /// For entries that don't require a custom entry builder, call the base implementation.
        ///
        /// <param name="configType"></param>
        /// <param name="entryName"></param>
        /// <returns></returns>
        protected virtual ConfigEntryBuilder? GetEntryBuilder(Type configType, string entryName)
        {
            return GetDefaultEntryBuilder(configType, entryName);
        }

        ConfigEntryBuilder? GetEntryBuilderWrapper(Type configType, GameConfigRepository.GameConfigEntryInfo entry)
        {
            MetaDebug.Assert(_currentConfigEntry == null, $"Re-entering {nameof(GetEntryBuilderWrapper)}");
            try
            {
                _currentConfigEntry = (configType, entry);
                return GetEntryBuilder(configType, entry.Name);
            }
            finally
            {
                _currentConfigEntry = null;
            }
        }

        protected sealed override Task BuildAsync(
            IGameConfigBuilderProvider<ISharedGameConfig> shared,
            IGameConfigBuilderProvider<IServerGameConfig> server,
            GameConfigBuildParameters buildParams)
        {
            return Build(shared.MakeBuilder<TSharedConfig>(),
                server.MakeBuilder<TServerConfig>(),
                (TBuildParameters)buildParams ?? new TBuildParameters());
        }

        /// <summary>
        /// The main GameConfig build entrypoint.
        /// </summary>
        /// Override this method to provide a completely custom implementation of the full shared and server GameConfig build.
        /// <param name="shared"></param>
        /// <param name="server"></param>
        /// <param name="buildParams"></param>
        /// <returns></returns>
        protected virtual async Task Build(
            IGameConfigBuilder<TSharedConfig> shared,
            IGameConfigBuilder<TServerConfig> server,
            TBuildParameters buildParams)
        {
            BuildParameters = buildParams;

            // Configure builders for config entries

            IEnumerable<(IGameConfigBuilder, ConfigEntryBuilder)> entryBuilders = GetConfigEntryBuilders(new IGameConfigBuilder[] { shared, server});

            // Execute "FetchData" for each entry that has one configured

            foreach (ConfigEntryBuilder entryBuilder in entryBuilders.Where(f => f.Item2.FetchDataAsync != null).Select(f => f.Item2))
                await entryBuilder.FetchDataAsync();

            // Execute "Build" for each entry

            foreach ((IGameConfigBuilder configBuilder, ConfigEntryBuilder entryBuilder) in entryBuilders)
                await entryBuilder.BuildAsync(configBuilder);
        }
    }

    public abstract class GameConfigBuildTemplate<TSharedConfig, TServerConfig> : GameConfigBuildTemplate<TSharedConfig, TServerConfig, DefaultGameConfigBuildParameters>
        where TSharedConfig : ISharedGameConfig
        where TServerConfig : IServerGameConfig
    {
    }

    public abstract class GameConfigBuildTemplate<TSharedConfig> : GameConfigBuildTemplate<TSharedConfig, ServerGameConfigBase, DefaultGameConfigBuildParameters>
        where TSharedConfig : ISharedGameConfig
    {
    }

    // The default game config implementation that doesn't require static knowledge of integration types. This implementation is used
    // when integration doesn't provide a GameConfigBuild implementation of its own.
    public class DefaultGameConfigBuild : GameConfigBuildTemplate<ISharedGameConfig, IServerGameConfig, DefaultGameConfigBuildParameters>
    {
    }

    // Integration hooks for the game config build.
    public class GameConfigBuildIntegration : IMetaIntegrationSingleton<GameConfigBuildIntegration>
    {
        public virtual UnknownConfigMemberHandling UnknownConfigItemMemberHandling => UnknownConfigMemberHandling.Error;

        // Create and initialize a custom GameConfigBuild instance.
        public virtual GameConfigBuild MakeGameConfigBuild(IGameConfigSourceFetcherConfig fetcherConfig, GameConfigBuildDebugOptions debugOpts)
        {
            // using legacy GameConfigBuild integration hook here
            GameConfigBuild impl = IntegrationRegistry.Create<GameConfigBuild>();
            impl.SourceFetcherProvider = MakeSourceFetcherProvider(fetcherConfig);
            impl.DebugOptions = debugOpts;
            return impl;
        }

        public virtual Type GetDefaultBuildParametersType()
        {
            IEnumerable<Type> paramsTypes = IntegrationRegistry.GetIntegrationClasses(typeof(GameConfigBuildParameters));
            if (paramsTypes.Count() <= 1)
                return paramsTypes.SingleOrDefault();

            // remove SDK default from the list
            paramsTypes = paramsTypes.Where(x => x != typeof(DefaultGameConfigBuildParameters));
            if (paramsTypes.Count() > 1)
                throw new InvalidOperationException($"Multiple {nameof(GameConfigBuildParameters)} integrations " +
                    $"found, override {nameof(GameConfigBuildIntegration)}.{nameof(GetDefaultBuildParametersType)} to specify which one to use as default");

            return paramsTypes.SingleOrDefault();
        }

        public IEnumerable<(string SourceProperty, IEnumerable<GameConfigBuildSource> Sources)> GetAllAvailableBuildSources(Type buildParamsType)
        {
            return GetBuildSourcesInBuildParameters(buildParamsType).Select(x => (x.Name, GetAvailableBuildSources(x.Name))).Where(x => x.Item2.Any());
        }

        // Retrieve `GameConfigBuildSource` members of a `GameConfigBuildParameters` type. The default implementation uses
        // reflection to simply enumerate all members of the appropriate type and should do the right thing in majority
        // of cases. Only override this function when your custom `GameConfigBuildParameters` has a more complicated
        // layout.
        public virtual IEnumerable<MemberInfo> GetBuildSourcesInBuildParameters(Type buildParamsType)
        {
            MetaDebug.Assert(typeof(GameConfigBuildParameters).IsAssignableFrom(buildParamsType), $"Type {buildParamsType} is not a {nameof(GameConfigBuildParameters)}");
            return buildParamsType
                         .EnumerateInstanceDataMembersInUnspecifiedOrder()
                         .Where(x => typeof(GameConfigBuildSource).IsAssignableFrom(x.GetDataMemberType()));
        }

        // List all known valid game config build source values for a given source member in `GameConfigBuildProperties`.
        // This is only used to populate config build source selector UIs in dashboard/editor, but the actual game config
        // build is not necessarily limited to these available options.
        public virtual IEnumerable<GameConfigBuildSource> GetAvailableBuildSources(string sourcePropertyInBuildParams)
        {
            return Enumerable.Empty<GameConfigBuildSource>();
        }

        // Create a custom IGameConfigSourceFetcherProvider instance based on the given fetcher config. Override this
        // if you have custom build source types that need a custom fetcher.
        public virtual IGameConfigSourceFetcherProvider MakeSourceFetcherProvider(IGameConfigSourceFetcherConfig config)
        {
            return new DefaultGameConfigSourceFetcherProvider((GameConfigSourceFetcherConfigCore)config);
        }
    }

    public static class StaticFullGameConfigBuilder
    {
        public static async Task<ConfigArchive> BuildArchiveAsync(
            MetaTime createdAt,
            MetaGuid parentId,
            ConfigArchive parent,
            GameConfigBuildParameters buildParams,
            IGameConfigSourceFetcherConfig fetcherConfig = null,
            GameConfigBuildDebugOptions debugOptions = null)
        {
            GameConfigBuild build = IntegrationRegistry.Get<GameConfigBuildIntegration>().MakeGameConfigBuild(fetcherConfig, debugOptions);
            return await build.CreateArchiveAsync(createdAt, buildParams, parentId, parent);
        }

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public static async Task DebugCompareBuildLegacyVsNewPipelineAsync(
            MetaGuid parentId,
            ConfigArchive parent,
            GameConfigBuildParameters buildParams,
            IGameConfigSourceFetcherConfig fetcherConfig)
        {
            MetaTime createdAt = MetaTime.Epoch;

            ConfigArchive builtWithLegacy;
            ConfigArchive builtWithNew;

            {
                GameConfigBuild build = IntegrationRegistry.Get<GameConfigBuildIntegration>().MakeGameConfigBuild(fetcherConfig, debugOpts: default(GameConfigBuildDebugOptions));
                build.DebugForceLegacyParsing = true;
                builtWithLegacy = await build.CreateArchiveAsync(createdAt, buildParams, parentId, parent);
            }

            {
                GameConfigBuild build = IntegrationRegistry.Get<GameConfigBuildIntegration>().MakeGameConfigBuild(fetcherConfig, debugOpts: default(GameConfigBuildDebugOptions));
                build.DebugForceLegacyParsing = false;
                builtWithNew = await build.CreateArchiveAsync(createdAt, buildParams, parentId, parent);
            }

            if (builtWithLegacy.Version == builtWithNew.Version)
                DebugLog.Info("Compared config archives built with legacy and new parsing pipeline, identical archives produced.");
            else
            {
                ConfigArchive legacyWithoutMetadata = new ConfigArchive(builtWithLegacy.CreatedAt, builtWithLegacy.Entries.Where(e => e.Name != "_metadata"));
                ConfigArchive newWithoutMetadata = new ConfigArchive(builtWithNew.CreatedAt, builtWithNew.Entries.Where(e => e.Name != "_metadata"));

                if (legacyWithoutMetadata.Version == newWithoutMetadata.Version)
                    DebugLog.Error($"Full archive hashes differ: {{Legacy}} vs {{New}}, but the difference is only in _metadata. Please build the archives individually, changing {nameof(ConfigBuildDevelopmentDebug)}.{nameof(ConfigBuildDevelopmentDebug.ForceLegacy)} as appropriate, and inspect the debug outputs manually.", builtWithLegacy.Version, builtWithNew.Version);
                else
                    DebugLog.Error($"Full archive hashes differ: {{Legacy}} vs {{New}}! Please build the archives individually, changing {nameof(ConfigBuildDevelopmentDebug)}.{nameof(ConfigBuildDevelopmentDebug.ForceLegacy)} as appropriate, and inspect the debug outputs manually.", builtWithLegacy.Version, builtWithNew.Version);
            }
        }
#endif
    }
}
