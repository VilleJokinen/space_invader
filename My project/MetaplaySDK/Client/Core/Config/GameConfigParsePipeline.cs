// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

//#define VERBOSE_PIPELINE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static Metaplay.Core.Config.GameConfigSyntaxTree;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    // Placeholder example of configuring the parsing pipeline for a game config library.
    public class GameConfigParseLibraryPipelineConfig
    {
        public readonly GameConfigSyntaxAdapterAttribute[] SyntaxAdapterAttribs;
        public readonly UnknownConfigMemberHandling        UnknownMemberHandling;

        public GameConfigParseLibraryPipelineConfig(
            GameConfigSyntaxAdapterAttribute[] syntaxAdapterAttribs = null,
            UnknownConfigMemberHandling unknownMemberHandling = UnknownConfigMemberHandling.Error)
        {
            SyntaxAdapterAttribs = syntaxAdapterAttribs;
            UnknownMemberHandling = unknownMemberHandling;
        }
    }

    /// <summary>
    /// How to treat nonexistent members in config input data, e.g. when a config sheet has
    /// a mistyped column name that does not map to any member in the C# config item type.
    /// </summary>
    public enum UnknownConfigMemberHandling
    {
        /// <summary>
        /// Allow and ignore unknown members.
        /// </summary>
        Ignore,
        /// <summary>
        /// Produce build log warnings about unknown members.
        /// </summary>
        Warning,
        /// <summary>
        /// Produce build log errors about unknown members, causing the config build to fail.
        /// </summary>
        Error,
    }

    // Placeholder example of configuring the parsing pipeline for a key-value object.
    public class GameConfigParseKeyValuePipelineConfig
    {
        public GameConfigParseKeyValuePipelineConfig()
        {
        }
    }

    /// <summary>
    /// Pipeline for parsing game config input sources (eg, spreadsheets) into syntax tree objects.
    /// </summary>
    public static class GameConfigParsePipeline
    {
        public static void PreprocessSpreadsheet(GameConfigBuildLog buildLog, SpreadsheetContent spreadsheetContent, bool isLibrary)
        {
            List<List<SpreadsheetCell>> rows = spreadsheetContent.Cells;

            // Fail on empty sheet
            if (rows.Count == 0)
            {
                GameConfigSourceLocation location = GameConfigSpreadsheetLocation.FromFullSheet(buildLog.SourceInfo);
                buildLog.WithLocation(location).Error("Input sheet is completely empty");
                return;
            }

            // Fail on empty header row (only for libraries)
            if (isLibrary && rows[0].Count == 0)
            {
                GameConfigSourceLocation location = GameConfigSpreadsheetLocation.FromRows(buildLog.SourceInfo, 0, 1);
                buildLog.WithLocation(location).Error("Input sheet header row is empty");
                return;
            }

            // Filter out completely empty rows
            // \todo [petri] This should not be needed, empty rows should generally be handled correctly by the multi-row item parser
            // \todo [nuutti] True, but the below "pad all rows" doesn't work on empty rows. It needs the .Row from the first cell.
            //       So this is enabled for now, but things should be fixed so that this indeed isn't needed.
            //       Maybe it is a flaw in SpreadsheetContent that it does not carry row info for empty rows. Maybe SpreadsheetContent
            //       should have per-row information in addition to per-cell information?
            rows.RemoveAll(row => row.Count == 0);

            // Filter out commented rows (start with '//')
            // \todo [petri] replace with a special "#exclude" column or similar?
            // \todo [nuutti] Still enabled for now, so that old sheets can be parsed. Can this be handled with a syntax adapter shim of some kind?
            rows.RemoveAll(row => row[0].Value.StartsWith("//", StringComparison.Ordinal));

            // Pad all rows with empty cells to equal length
            int maxColumns = rows.Max(row => row.Count);
            foreach (List<SpreadsheetCell> row in rows)
            {
                while (row.Count < maxColumns)
                    row.Add(new SpreadsheetCell("", row: row[0].Row, column: row.Count));
            }
        }

        /// <summary>
        /// Apply any necessary syntax modifications to spreadsheet to convert from old legacy syntax to the
        /// syntax that the new game config builder understands.
        /// </summary>
        static void ApplySyntaxAdapters(GameConfigSyntaxAdapterAttribute[] attribs, SpreadsheetContent content)
        {
            List<SpreadsheetCell> headerRow = content.Cells[0];

            // Apply all adapters
            foreach (GameConfigSyntaxAdapterAttribute attrib in attribs)
            {
                // Apply all replace rules to the header row
                // \note We don't care if a rule doesn't match such that the rules work on already-converted source data
                foreach (GameConfigSyntaxAdapterAttribute.ReplaceRule rule in attrib.HeaderReplaces)
                {
                    for (int ndx = 0; ndx < headerRow.Count; ndx++)
                    {
                        SpreadsheetCell cell = headerRow[ndx];
                        string value = cell.Value;

                        // Replace cell value if matches the rule
                        if (value == rule.From)
                            headerRow[ndx] = new SpreadsheetCell(rule.To, cell.Row, cell.Column);
                    }
                }

                // Apply all prefix replace rules to the header row
                // \note We don't care if a rule doesn't match such that the rules work on already-converted source data
                foreach (GameConfigSyntaxAdapterAttribute.ReplaceRule rule in attrib.HeaderPrefixReplaces)
                {
                    for (int ndx = 0; ndx < headerRow.Count; ndx++)
                    {
                        SpreadsheetCell cell = headerRow[ndx];
                        string value = cell.Value;

                        // Replace cell value if matches the rule
                        if (value.StartsWith(rule.From, StringComparison.Ordinal))
                        {
                            string replaced = rule.To + value.Substring(rule.From.Length);
                            headerRow[ndx] = new SpreadsheetCell(replaced, cell.Row, cell.Column);
                        }
                    }
                }
            }

            // Convert all '[Array]' notation to 'Array[<ndx>]'
            Dictionary<string, int> arrayIndexCounters = new Dictionary<string, int>();
            for (int ndx = 0; ndx < headerRow.Count; ndx++)
            {
                SpreadsheetCell cell = headerRow[ndx];
                string value = cell.Value;

                if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                {
                    // Resolve index for this value
                    int index = arrayIndexCounters.GetValueOrDefault(value, 0);

                    // Update cell to 'Name[<ndx>]'
                    string name = value.Substring(1, value.Length - 2);
                    headerRow[ndx] = new SpreadsheetCell(Invariant($"{name}[{index}]"), cell.Row, cell.Column);

                    // Update the index counter
                    arrayIndexCounters[value] = index + 1;
                }
            }
        }

        // This is standard to resemble the "standard" pipeline that we could offer out-of-the-box.
        // A bunch of configuration options (or metadata) is needed to customize the behavior for
        // most games' needs. Fully customizing the pipeline should also be supported as an escape
        // hatch for the features we don't have. Perhaps also some injecting of custom phases of
        // processing.
        static VariantConfigItem<TKey, TItem>[] ProcessSpreadsheetLibraryImpl<TKey, TItem>(GameConfigBuildLog buildLog, GameConfigParseLibraryPipelineConfig config, SpreadsheetContent sheet) where TItem : IGameConfigKey<TKey>, new()
        {
#if VERBOSE_PIPELINE
            Console.WriteLine("Parsing spreadsheet:\n{0}", sheet);
#endif

            // Preprocess spreadsheet & bail on error
            PreprocessSpreadsheet(buildLog, sheet, isLibrary: true);
            if (buildLog.HasErrors())
                return null;

            // If syntax conversion enabled, apply it
            if (config.SyntaxAdapterAttribs != null && config.SyntaxAdapterAttribs.Length > 0)
                ApplySyntaxAdapters(config.SyntaxAdapterAttribs, sheet);

            // Transform the spreadsheet into (sparse) syntax tree objects, with only structural/layout changes.
            List<GameConfigSyntaxTree.RootObject> objects = GameConfigSpreadsheetReader.TransformLibrarySpreadsheet(buildLog, sheet);

#if VERBOSE_PIPELINE
            // \note objects can be null if early errors happened. But also can be non-null if individual items errored.
            if (objects != null)
            {
                for (int ndx = 0; ndx < objects.Count; ndx++)
                    Console.WriteLine("Object #{0}:\n{1}", ndx, objects[ndx].Node.TreeToString(depth: 1));
            }
#endif

            // If any errors, bail out
            if (buildLog.HasErrors())
                return null;

            // Canonicalize "/Aliases" annotations into RootObject.Aliases.
            // Also canonicalize variant annotations into root-level objects with their VariantId assigned.
            // This may produce more root objects due to multiple variants being specified within one input item.
            objects = objects.SelectMany(obj =>
            {
                obj = GameConfigSyntaxTreeUtil.ExtractAliases(obj);
                return GameConfigSyntaxTreeUtil.ExtractVariants(obj);
            }).ToList();


            // Detect duplicate objects: (ItemId, VariantId) pair must be unique
            GameConfigSyntaxTreeUtil.DetectDuplicateObjects(buildLog, objects);
            if (buildLog.HasErrors())
                return null;

            // Fill in missing ids first, because variant inheriting relies on ids.
            // \todo [nuutti] Add support for inheriting empty cells from above where sensible (needs design)
            GameConfigSyntaxTreeUtil.InheritVariantValuesFromBaseline(objects);

            // Parse the final output types from the syntax tree.
            List<VariantConfigItem<TKey, TItem>> items = new List<VariantConfigItem<TKey, TItem>>();
            foreach (GameConfigSyntaxTree.RootObject obj in objects)
                items.Add(GameConfigOutputItemParser.ParseItem<TKey, TItem>(config, buildLog, obj));

#if VERBOSE_PIPELINE
            Console.WriteLine("Parsed (variant) items:");
            for (int ndx = 0; ndx < items.Count; ndx++)
            {
                VariantConfigItem<TKey, TItem> item = items[ndx];
                string variantTextMaybe = item.VariantIdMaybe == null ? "" : $" (variant {item.VariantIdMaybe})";
                Console.WriteLine("#{0}{1}: {2}", ndx, variantTextMaybe, PrettyPrint.Verbose(item.Item));
            }
#endif

            // If any errors, bail out
            if (buildLog.HasErrors())
                return null;

            return items.ToArray();
        }

        public static VariantConfigItem<TKey, TItem>[] ProcessSpreadsheetLibrary<TKey, TItem>(GameConfigBuildLog buildLog, GameConfigParseLibraryPipelineConfig config, SpreadsheetContent sheet) where TItem : IGameConfigKey<TKey>, new()
        {
            VariantConfigItem<TKey, TItem>[] items = ProcessSpreadsheetLibraryImpl<TKey, TItem>(buildLog, config, sheet);

#if VERBOSE_PIPELINE
            // Report errors if any
            if (buildLog.HasErrors())
            {
                Console.WriteLine("ERRORS OCCURRED DURING BUILD:");
                foreach (GameConfigBuildMessage msg in buildLog.Messages)
                    Console.WriteLine("{0}", msg);
                Console.WriteLine("");
            }
#endif

            return items;
        }

        // \todo [nuutti] How much can and should this share code with the library parsing pipeline?
        static VariantConfigStructureMember[] ProcessSpreadsheetKeyValueImpl<TKeyValue>(GameConfigBuildLog buildLog, GameConfigParseKeyValuePipelineConfig config, SpreadsheetContent sheet)
        {
#if VERBOSE_PIPELINE
            Console.WriteLine("Parsing spreadsheet:\n{0}", sheet);
#endif

            // Preprocess spreadsheet
            PreprocessSpreadsheet(buildLog, sheet, isLibrary: false);

            // If any errors, bail out
            if (buildLog.HasErrors())
                return null;

            // Transform the spreadsheet into (sparse) root objects, with only structural/layout changes.
            List<GameConfigSyntaxTree.RootObject> objects = GameConfigSpreadsheetReader.TransformKeyValueSpreadsheet(buildLog, sheet);

#if VERBOSE_PIPELINE
            // \note objects can be null if early errors happened. But also can be non-null if individual items errored.
            if (objects != null)
            {
                foreach (GameConfigSyntaxTree.RootObject obj in objects)
                    Console.WriteLine("Object node:\n{0}", obj.Node.ToString());
            }
#endif

            // If any errors, bail out
            if (buildLog.HasErrors())
                return null;

            // Canonicalize variant annotations into top-level RootObjects with their VariantId assigned.
            // This may produce more RootObjects due to multiple variants being specified within one input item.
            // \todo Key-value config doesn't have column overrides so this should be unnecessary.
            objects = objects.SelectMany(GameConfigSyntaxTreeUtil.ExtractVariants).ToList();

            // Fill in missing ids first, because variant inheriting relies on ids.
            GameConfigSyntaxTreeUtil.InheritVariantValuesFromBaseline(objects);

            // Parse the final output members from the syntax tree.
            List<VariantConfigStructureMember> members = new List<VariantConfigStructureMember>();
            foreach (GameConfigSyntaxTree.RootObject obj in objects)
            {
                string variantId = obj.VariantId;
                (MemberInfo, object)? parsedMember = GameConfigOutputItemParser.TryParseKeyValueStructureMember<TKeyValue>(buildLog, variantId: variantId, obj.Node);
                if (!parsedMember.HasValue)
                {
                    // Error has been recorded in `buildLog`.
                    continue;
                }

                (MemberInfo memberInfo, object memberValue) = parsedMember.Value;
                members.Add(new VariantConfigStructureMember(
                    new ConfigStructureMember(memberInfo, memberValue),
                    variantId,
                    obj.Location));
            }

#if VERBOSE_PIPELINE
            Console.WriteLine("Parsed (variant) members:");
            for (int ndx = 0; ndx < members.Count; ndx++)
            {
                VariantConfigStructureMember member = members[ndx];
                string variantTextMaybe = member.VariantIdMaybe == null ? "" : $" (variant {member.VariantIdMaybe})";
                Console.WriteLine("#{0}{1}: {2} = {3}", ndx, variantTextMaybe, member.Member.MemberInfo.Name, PrettyPrint.Verbose(member.Member.MemberValue));
            }
            Console.WriteLine();
#endif

            // If any errors, bail out
            if (buildLog.HasErrors())
                return null;

            return members.ToArray();
        }

        public static VariantConfigStructureMember[] ProcessSpreadsheetKeyValue<TKeyValue>(GameConfigBuildLog buildLog, GameConfigParseKeyValuePipelineConfig config, SpreadsheetContent sheet)
        {
            VariantConfigStructureMember[] members = ProcessSpreadsheetKeyValueImpl<TKeyValue>(buildLog, config, sheet);

#if VERBOSE_PIPELINE
            // Report errors if any
            if (buildLog.HasErrors())
            {
                Console.WriteLine("ERRORS OCCURRED DURING BUILD:");
                foreach (GameConfigBuildMessage msg in buildLog.Messages)
                    Console.WriteLine("{0}", msg);
                Console.WriteLine("");
            }
#endif

            return members;
        }
    }
}
