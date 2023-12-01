// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloud.Tests
{
    /// <summary>
    /// Various helpers and utilities to ease writing game config related tests.
    /// </summary>
    public static class GameConfigTestHelper
    {
        public static SpreadsheetContent ParseSpreadsheet(string sheetName, IEnumerable<IEnumerable<string>> rows)
        {
            // Spoof a file source
            SpreadsheetFileSourceInfo sourceInfo = new SpreadsheetFileSourceInfo($"{sheetName}.csv");
            return new SpreadsheetContent(sheetName, rows.Select(row => row.ToList()).ToList(), sourceInfo);
        }

        public static void CheckNoErrorsOrWarnings(GameConfigBuildLog buildLog)
        {
            if (buildLog.HasErrors() || buildLog.HasWarnings())
            {
                int numErrors = buildLog.Messages.Count(msg => msg.Level == GameConfigLogLevel.Error);
                int numWarnings = buildLog.Messages.Count(msg => msg.Level == GameConfigLogLevel.Warning);
                throw new InvalidOperationException($"Got {numErrors} errors and {numWarnings} warnings while parsing the spreadsheet data:\n{string.Join("\n", buildLog.Messages)}");
            }
        }

        public static void CheckNoAliases<TKey, TItem>(VariantConfigItem<TKey, TItem>[] items) where TItem : IGameConfigData<TKey>
        {
            if (items.Any(item => item.Aliases != null))
                throw new InvalidOperationException("Item aliases contained in the parsed result!");
        }

        public static void CheckNoVariants<TKey, TItem>(VariantConfigItem<TKey, TItem>[] items) where TItem : IGameConfigData<TKey>
        {
            if (items.Any(item => item.VariantIdMaybe != null))
                throw new InvalidOperationException("Variant items contained in the parsed result!");
        }

        public static VariantConfigItem<TKey, TItem>[] ParseItems<TKey, TItem>(GameConfigBuildLog buildLog, GameConfigParseLibraryPipelineConfig config, SpreadsheetContent spreadsheet) where TItem : IGameConfigData<TKey>, new()
        {
            VariantConfigItem<TKey, TItem>[] items = GameConfigParsePipeline.ProcessSpreadsheetLibrary<TKey, TItem>(buildLog, config, spreadsheet);

            // Check that no errors/warnings occurred during parsing
            CheckNoErrorsOrWarnings(buildLog);

            return items;
        }

        public static (List<TKey> Aliases, TItem Item)[] ParseItemsWithAliases<TKey, TItem>(GameConfigParseLibraryPipelineConfig config, SpreadsheetContent spreadsheet) where TItem : IGameConfigData<TKey>, new()
        {
            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(spreadsheet.Name);
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);

            VariantConfigItem<TKey, TItem>[] items = ParseItems<TKey, TItem>(buildLog, config, spreadsheet);

            CheckNoVariants(items);

            return items.Select(item => (item.Aliases, item.Item)).ToArray();
        }

        public static (string VariantIdMaybe, TItem Item)[] ParseItemsWithVariants<TKey, TItem>(GameConfigParseLibraryPipelineConfig config, SpreadsheetContent spreadsheet) where TItem : IGameConfigData<TKey>, new()
        {
            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(spreadsheet.Name);
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);

            VariantConfigItem<TKey, TItem>[] items = ParseItems<TKey, TItem>(buildLog, config, spreadsheet);

            CheckNoAliases(items);

            return items.Select(item => (item.VariantIdMaybe, item.Item)).ToArray();
        }

        public static (List<TKey> Aliases, string VariantIdMaybe, TItem Item)[] ParseItemsWithAliasesAndVariants<TKey, TItem>(GameConfigParseLibraryPipelineConfig config, SpreadsheetContent spreadsheet) where TItem : IGameConfigData<TKey>, new()
        {
            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(spreadsheet.Name);
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);

            VariantConfigItem<TKey, TItem>[] items = ParseItems<TKey, TItem>(buildLog, config, spreadsheet);

            return items.Select(item => (item.Aliases, item.VariantIdMaybe, item.Item)).ToArray();
        }

        public static TItem[] ParseNonVariantItems<TKey, TItem>(GameConfigParseLibraryPipelineConfig config, SpreadsheetContent spreadsheet) where TItem : IGameConfigData<TKey>, new()
        {
            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(spreadsheet.Name);
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);

            VariantConfigItem<TKey, TItem>[] items = ParseItems<TKey, TItem>(buildLog, config, spreadsheet);

            // Check that no variant items or aliases were returned
            CheckNoVariants(items);
            CheckNoAliases(items);

            // Return just the items
            return items.Select(item => item.Item).ToArray();
        }

        // \todo [nuutti] This just returns the parsed members. Chould also test the final TKeyValue instance created?
        //       That currently happens outside the parsing code, in GameConfigBuild.cs, AssignConfigKeyValueStructureBuildResult.
        //       One related thing is that the individual members are needed for the patch, which is per-top-level-member.
        public static (string VariantIdMaybe, string Name, object Value)[] ParseKeyValueMembers<TKeyValue>(GameConfigBuildLog buildLog, GameConfigParseKeyValuePipelineConfig config, SpreadsheetContent spreadsheet)
        {
            VariantConfigStructureMember[] members = GameConfigParsePipeline.ProcessSpreadsheetKeyValue<TKeyValue>(buildLog, config, spreadsheet);

            // Check that no errors/warnings occurred during parsing
            CheckNoErrorsOrWarnings(buildLog);

            return members.Select(member => (member.VariantIdMaybe, member.Member.MemberInfo.Name, member.Member.MemberValue)).ToArray();
        }

        public static (string Name, object Value)[] ParseNonVariantKeyValueMembers<TKeyValue>(GameConfigParseKeyValuePipelineConfig config, SpreadsheetContent spreadsheet)
        {
            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(spreadsheet.Name);
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);

            (string VariantIdMaybe, string Name, object Value)[] members = ParseKeyValueMembers<TKeyValue>(buildLog, config, spreadsheet);

            // Check that no variant members were returned
            if (members.Any(item => item.VariantIdMaybe != null))
                throw new InvalidOperationException("Variant members contained in the parsed result!");

            return members.Select(member => (member.Name, member.Value)).ToArray();
        }
    }
}
