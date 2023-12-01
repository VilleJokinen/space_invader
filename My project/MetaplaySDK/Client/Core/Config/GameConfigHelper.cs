// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Helper methods for parsing and validating GameConfig contents.
    /// </summary>
    public static class GameConfigHelper
    {
        /// <summary>
        /// Parse UTF-8 encoded comma-separated values (.csv) payload to a <see cref="SpreadsheetContent"/>.
        /// </summary>
        public static SpreadsheetContent ParseCsvToSpreadsheet(string filePath, byte[] bytes)
        {
            SpreadsheetFileSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(filePath);

            // \todo [petri] quite wasteful parsing
            using (IOReaderStream ioreader = new IOReader(bytes).ConvertToStream())
            using (CsvStream reader = new CsvStream(filePath, ioreader))
            {
                List<List<string>> cells = new List<List<string>>();
                foreach (string[] row in reader)
                {
                    // Don't include trailing empty cells.
                    // The purpose of this is to produce similar content as Google Sheet fetching,
                    // which does not produce trailing empty cells.
                    int count = row.Length;
                    while (count > 0 && row[count - 1] == "")
                        count--;
                    cells.Add(row.Take(count).ToList());
                }
                return new SpreadsheetContent(filePath, cells, sourceInfo);
            }
        }

        /// <summary>
        /// Validate that the sheet contains no Google Sheet error values ('#REF!', '#NULL!', etc.) fields.
        /// </summary>
        /// <param name="sheet"></param>
        public static void ValidateNoGoogleSheetErrors(SpreadsheetContent sheet)
        {
            for (int rowNdx = 0; rowNdx < sheet.Cells.Count; rowNdx++)
            {
                List<SpreadsheetCell> row = sheet.Cells[rowNdx];
                for (int colNdx = 0; colNdx < row.Count; colNdx++)
                {
                    string value = row[colNdx].Value;
                    if (IsCellValueGoogleSheetError(value))
                        throw new Exception($"Sheet {sheet.Name} has an error value '{value}' in row {rowNdx}, column {colNdx}");
                }
            }
        }

        /// <summary>
        /// Determines if a cell value is a well-known error or invalid value. This means the cell formula errored out and did not compute a valid value for the cell.
        /// See: https://infoinspired.com/google-docs/spreadsheet/different-error-types-in-google-sheets/
        /// </summary>
        static bool IsCellValueGoogleSheetError(string value)
        {
            return value == "#NULL!" || value == "#DIV/0!" || value == "#VALUE!" || value == "#REF!" || value == "#NAME?" || value == "#NUM!" || value == "#N/A";
        }

        /// <summary>
        /// Filter out any fully empty rows.
        /// </summary>
        /// <param name="sheet"></param>
        /// <returns></returns>
        public static SpreadsheetContent FilterEmptyRows(SpreadsheetContent sheet)
        {
            List<List<SpreadsheetCell>> newRows = new List<List<SpreadsheetCell>>();
            foreach (List<SpreadsheetCell> inRow in sheet.Cells)
            {
                if (inRow.Count > 0)
                    newRows.Add(inRow);
            }

            return new SpreadsheetContent(sheet.Name, newRows, sheet.SourceInfo);
        }

        /// <summary>
        /// Filter out any rows where the first cell starts with '//', marking it as comment.
        /// </summary>
        /// <param name="sheet"></param>
        /// <returns></returns>
        public static SpreadsheetContent FilterCommentRows(SpreadsheetContent sheet)
        {
            return sheet.FilterRows((List<string> cells, int _rowNdx) => cells.Count == 0 || !cells[0].StartsWith("//", StringComparison.Ordinal));
        }

        /// <summary>
        /// Filter out all rows not matching the specified environment. Columns names starting with '$' (eg, '$dev' or '$prod')
        /// are considered as meta-columns marking which build configuration each of the rows should be included in.
        /// </summary>
        /// <param name="columnName"></param>
        /// <param name="sheet"></param>
        /// <returns></returns>
        public static SpreadsheetContent FilterSelectedEnvironment(string columnName, SpreadsheetContent sheet)
        {
            // Resolve columns that start with '$'
            Dictionary<string, int> envColumns =
                sheet.Cells[0]
                .Select((SpreadsheetCell cell, int colNdx) => new { Name = cell.Value, Ndx = colNdx })
                .Where(col => col.Name.StartsWith("$", StringComparison.Ordinal))
                .ToDictionary(col => col.Name.Substring(1), col => col.Ndx);

            // Resolve env column to use
            if (!envColumns.TryGetValue(columnName, out int useColNdx))
                throw new InvalidOperationException($"No environment column named '${columnName}' found in sheet {sheet.Name}");

            // Filter out any rows not matching the selected environment column
            sheet = sheet.FilterRows((List<string> cells, int rowNdx) => cells[useColNdx] != "");

            // Filter out all environment columns from sheet
            return sheet.FilterColumns((string colName, int colNdx) => envColumns.Values.Contains(colNdx));
        }

        public const string VariantColumnName = "/Variant";
        public const string AliasColumnName = "/Aliases";

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        struct RowWithVariants
        {
            public List<SpreadsheetCell>                     Row;
            public Dictionary<string, List<SpreadsheetCell>> VariantOverrides;

            public RowWithVariants(bool initVariants)
            {
                Row              = new List<SpreadsheetCell>();
                VariantOverrides = initVariants ? new Dictionary<string, List<SpreadsheetCell>>() : null;
            }
        }
#endif

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public class RowWithVariantAssociation
        {
            public List<SpreadsheetCell>     Row        { get; }
            public string                    VariantKey { get; }
            public RowWithVariantAssociation Baseline   { get; private set; }

            public RowWithVariantAssociation(List<SpreadsheetCell> row, string variantKey = null, RowWithVariantAssociation baseline = null)
            {
                Row        = row;
                VariantKey = variantKey;
                Baseline   = baseline;
            }

            public void UpdateBaseline(RowWithVariantAssociation newBaseline)
            {
                Baseline = newBaseline;
            }

            public void ExpandFromBaseline(int aliasColumn)
            {
                if (Baseline == null)
                    return;

                for (var i = 0; i < Row.Count; i++)
                {
                    if (i != aliasColumn && string.IsNullOrWhiteSpace(Row[i].Value))
                        Row[i] = Baseline.Row[i];
                }
            }
        }
#endif

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        struct ColumnInfo
        {
            public bool IsAliasColumn;
            public bool IsVariantIdColumn;
            public List<string> ColumnOverrideVariantIds;
            public int ColumnOverrideTargetIndex;
            public bool IsColumnVariantOverride => ColumnOverrideVariantIds != null;
        }
#endif

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        class VariantExpandRowReader
        {
            // The "schema" of the sheet, parsed from the header row. Contains a ColumnInfo entry for each of the (input) columns,
            // with information about special interpretations of the column.
            List<ColumnInfo> _columns = new List<ColumnInfo>();
            // The output header row. Can be smaller than the input header row, when column variant overrides are present.
            List<SpreadsheetCell> _resultHeaderRow = new List<SpreadsheetCell>();
            // The parsed rows along with possible variants for it (both from row and column variant overrides).
            List<RowWithVariantAssociation> _resultRows = new List<RowWithVariantAssociation>();

            int  InputRowLen  => _columns.Count;
            int  ResultRowLen => _resultHeaderRow.Count;

            bool _hasVariantColumns  = false;
            int  _variantIdColumnNdx = -1;
            int  _aliasColumnNdx     = -1;

            public VariantExpandRowReader(List<SpreadsheetCell> headerRow, int maxRowLen)
            {
                _aliasColumnNdx = headerRow.FindIndex(x => x.Value.Equals(AliasColumnName, StringComparison.InvariantCultureIgnoreCase));
                _variantIdColumnNdx = headerRow.FindIndex(x=> x.Value.Equals(VariantColumnName, StringComparison.InvariantCultureIgnoreCase));
                int curOverrideTargetIndex = -1;

                foreach ((SpreadsheetCell header, int index) in PadInput(headerRow, maxRowLen).ZipWithIndex())
                {
                    ColumnInfo col = new ColumnInfo();

                    if (index == _variantIdColumnNdx)
                    {
                        col.IsVariantIdColumn = true;
                    }
                    else if (index == _aliasColumnNdx)
                    {
                        col.IsAliasColumn = true;
                    }
                    else if (TryParseVariantColumnHeader(header, out IEnumerable<string> variantIds))
                    {
                        if (curOverrideTargetIndex == -1)
                            throw new ConfigParserException($"Variant column {header} does not have an associated non-variant column", header);
                        col.ColumnOverrideVariantIds = variantIds.ToList();
                        col.ColumnOverrideTargetIndex = curOverrideTargetIndex;
                        _hasVariantColumns = true;
                    }
                    else if (!string.IsNullOrEmpty(header.Value))
                    {
                        // only regular columns with non-empty header string can be targeted by column overrides
                        curOverrideTargetIndex = _resultHeaderRow.Count;
                    }

                    if (!col.IsColumnVariantOverride)
                        _resultHeaderRow.Add(header);

                    _columns.Add(col);
                }
            }

            static IEnumerable<string> ParseVariantIdList(string input)
            {
                return input.Split(',').Select(x => x.Trim()).Where(x => x != string.Empty);
            }

            static bool TryParseVariantColumnHeader(SpreadsheetCell cell, out IEnumerable<string> variantIds)
            {
                if (cell.Value.StartsWith("/:", StringComparison.Ordinal))
                {
                    IEnumerable<string> ret = ParseVariantIdList(cell.Value.Substring(2));
                    if (!ret.Any())
                        throw new InvalidOperationException($"Malformed column override header {cell}");
                    variantIds = ret;
                    return true;
                }
                else
                {
                    variantIds = null;
                    return false;
                }
            }

            IEnumerable<SpreadsheetCell> PadInput(List<SpreadsheetCell> input, int len)
            {
                return input.Concat(Enumerable.Repeat(new SpreadsheetCell(), len)).Take(len);
            }

            IEnumerable<RowWithVariantAssociation> ReadNonVariantRow(IEnumerable<SpreadsheetCell> row)
            {
                RowWithVariants result = new RowWithVariants(_hasVariantColumns || _variantIdColumnNdx != -1);

                int index = 0;
                foreach (SpreadsheetCell contents in row)
                {
                    ColumnInfo info = _columns[index];
                    if (!info.IsColumnVariantOverride)
                    {
                        // Regular column, copy to result
                        result.Row.Add(contents);
                    }
                    else if (contents.Value != "")
                    {
                        foreach (string variantId in info.ColumnOverrideVariantIds)
                        {
                            // Variant column with override contents, add to existing variant row or generate new
                            if (!result.VariantOverrides.TryGetValue(variantId, out List<SpreadsheetCell> variantRow))
                            {
                                variantRow = Enumerable.Repeat(new SpreadsheetCell(), ResultRowLen).ToList();
                                result.VariantOverrides.Add(variantId, variantRow);
                            }

                            // Check for conflicts
                            int targetIndex = info.ColumnOverrideTargetIndex;

                            if (variantRow[targetIndex].Value != "")
                                throw new ConfigParserException(
                                    $"Multiple variant override values provided for variant {variantId}",
                                    new List<SpreadsheetCell>()
                                    {
                                        variantRow[targetIndex],
                                        contents
                                    });

                            // Override value
                            variantRow[targetIndex] = contents;
                        }
                    }

                    index++;
                }


                if (_variantIdColumnNdx >= 0 && _variantIdColumnNdx < result.Row.Count)
                    result.Row.RemoveAt(_variantIdColumnNdx);

                RowWithVariantAssociation baseline = new RowWithVariantAssociation(result.Row);
                yield return baseline;

                if (result.VariantOverrides != null)
                {
                    foreach ((string key, List<SpreadsheetCell> value) in result.VariantOverrides)
                    {
                        if (_variantIdColumnNdx >= 0 && _variantIdColumnNdx < value.Count)
                            value.RemoveAt(_variantIdColumnNdx);

                        RowWithVariantAssociation rowWithVariantAssociation = new RowWithVariantAssociation(value, key, baseline);
                        rowWithVariantAssociation.ExpandFromBaseline(_aliasColumnNdx);
                        yield return rowWithVariantAssociation;
                    }
                }
            }

            RowWithVariantAssociation ReadVariantRow(RowWithVariantAssociation baseRow, string variantId, IEnumerable<SpreadsheetCell> input)
            {
                var variantRow = Enumerable.Repeat(new SpreadsheetCell(), ResultRowLen).ToList();
                int outIdx = 0;
                foreach ((SpreadsheetCell contents, ColumnInfo info) cell in input.Zip(_columns, (x, y) => (x, y)))
                {
                    if (cell.contents.Value != "")
                    {
                        if (cell.info.IsAliasColumn)
                            throw new ConfigParserException($"Variant row {variantId} has alias entry, which is not supported.", cell.contents);
                        if (cell.info.IsColumnVariantOverride)
                            throw new ConfigParserException($"Variant row {variantId} has per-column variant overrides, which is not supported.", cell.contents);
                        if (variantRow[outIdx].Value != "")
                            throw new ConfigParserException($"Conflicting overrides for variant row {variantId}.", cell.contents);
                        if (!cell.info.IsVariantIdColumn)
                            variantRow[outIdx] = cell.contents;
                    }

                    if (!cell.info.IsColumnVariantOverride)
                        outIdx++;
                }

                if (_variantIdColumnNdx >= 0 && _variantIdColumnNdx < variantRow.Count)
                    variantRow.RemoveAt(_variantIdColumnNdx);

                return new RowWithVariantAssociation(variantRow, variantId, baseRow);
            }

            public void Read(List<SpreadsheetCell> row)
            {
                if (_variantIdColumnNdx < 0 || _variantIdColumnNdx >= row.Count || string.IsNullOrEmpty(row[_variantIdColumnNdx].Value))
                {
                    // Regular non-variant row.
                    foreach (RowWithVariantAssociation rowWithVariantAssociation in ReadNonVariantRow(PadInput(row, InputRowLen)))
                        _resultRows.Add(rowWithVariantAssociation);
                }
                else
                {
                    // Variant row.
                    string variantIdsMaybe = row[_variantIdColumnNdx].Value;

                    foreach (string variantId in ParseVariantIdList(variantIdsMaybe))
                    {
                        RowWithVariantAssociation rowWithVariantAssociation = _resultRows.LastOrDefault(x => x.VariantKey == null);
                        _resultRows.Add(ReadVariantRow(rowWithVariantAssociation, variantId, PadInput(row, InputRowLen)));
                    }
                }
            }

            public IEnumerable<RowWithVariantAssociation> GenerateResults()
            {
                // If input didn't contain a variant ID column and sheet uses per-column overrides,
                // we synthesize the variant ID column in the output as the first column
                bool addVariantIdColumn = _variantIdColumnNdx == -1 && _hasVariantColumns;
                int  variantIdColumnIdx = addVariantIdColumn ? -1 : _resultHeaderRow.FindIndex(x => x.Value.Equals(VariantColumnName, StringComparison.InvariantCultureIgnoreCase));

                // Header row
                var headerRow = _resultHeaderRow;
                if (variantIdColumnIdx >= 0 && variantIdColumnIdx < _resultHeaderRow.Count)
                    headerRow = headerRow.Skip(1).ToList();

                yield return new RowWithVariantAssociation(headerRow);

                // Content rows with variants expanded into separate rows
                foreach (RowWithVariantAssociation row in _resultRows)
                {
                    yield return row;
                }
            }
        }
#endif

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        /// <summary>
        /// Transpose column variant specifications into full rows.
        /// Specifically: If there is a column named /Variant, then on rows which
        /// have a nonempty /Variant column, empty cells are populated by using
        /// the value in the same column from the previous non-variant row. If the
        /// variant column contains multiple comma-separated variant identifiers then
        /// the row is duplicated. If the sheet contains columns that specify variants
        /// overrides using the '/:[variantIdList]' syntax then these columns are omitted
        /// from the output and corresponding variant rows are added for any rows on which
        /// the column contents are non-empty.
        /// </summary>
        /// <remarks>
        /// Returns a list of rows containing the row data and a reference to the expected baseline row.
        /// </remarks>
        public static List<RowWithVariantAssociation> PreprocessVariants(SpreadsheetContent sheet)
        {
            int maxRowLen = sheet.Cells.Select(x => x.Count).Max();

            VariantExpandRowReader reader = new VariantExpandRowReader(sheet.Cells[0], maxRowLen);

            foreach (List<SpreadsheetCell> row in sheet.Cells.Skip(1))
                reader.Read(row);

            return reader.GenerateResults().ToList();
        }
#endif

        /// <summary>
        /// Split a single sheet with multiple languages to multiple sheets with only a single language in them.
        /// The output sheets are named according to the columns in the input sheet.
        /// </summary>
        /// <param name="sheet">Sheet to parse individual language sheets from</param>
        /// <param name="allowMissingTranslations">If false, any missing translations will throw a ParseError</param>
        /// <returns></returns>
        public static List<SpreadsheetContent> SplitLanguageSheets(SpreadsheetContent sheet, bool allowMissingTranslations = false)
        {
            List<SpreadsheetCell>              header         = sheet.Cells[0];
            IEnumerable<List<SpreadsheetCell>> content        = sheet.Cells.Skip(1);
            Dictionary<string, int>            languageColumn = new Dictionary<string, int>();

            if (header[0].Value != "TranslationId")
                throw new ParseError($"Expected first column name to be 'TranslationId', but it's '{header[0]}'");

            List<SpreadsheetContent> languageSheets = new List<SpreadsheetContent>();
            for (int colNdx = 1; colNdx < header.Count; colNdx++)
            {
                string languageCode = header[colNdx].Value;

                if (languageColumn.TryGetValue(languageCode, out int preexistingColumn))
                    throw new ParseError(Invariant($"Language '{languageCode}' is defined multiple times. It is defined on columns {preexistingColumn+1} and {colNdx+1}."));
                languageColumn[languageCode] = colNdx;

                List<List<SpreadsheetCell>> newCells =
                    content
                    .Select(row =>
                    {
                        SpreadsheetCell translationId = row[0];
                        SpreadsheetCell valueCell     = (colNdx < row.Count) ? row[colNdx] : new SpreadsheetCell();

                        // Handle missing values
                        if (string.IsNullOrEmpty(valueCell.Value))
                        {
                            if (!allowMissingTranslations)
                                throw new ParseError($"Missing translation for '{translationId}' in language '{languageCode}'");
                            else
                                valueCell = new SpreadsheetCell($"#missing#{translationId}", valueCell.Row, valueCell.Column); // use prefix to distinguish between missing translations and untranslated text
                        }

                        return new List<SpreadsheetCell> { translationId, valueCell };
                    }).ToList();

                languageSheets.Add(new SpreadsheetContent(languageCode, newCells, sheet.SourceInfo));
            }

            return languageSheets;
        }

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        /// <summary>
        /// Encode a sheet as UTF-8 comma-separated values (.csv) file.
        /// </summary>
        /// <param name="sheet"></param>
        /// <returns></returns>
        public static ConfigArchiveEntry EncodeAsCSV(SpreadsheetContent sheet)
        {
            byte[] payload = Encoding.UTF8.GetBytes(sheet.ToString());
            return ConfigArchiveEntry.FromBlob($"{sheet.Name}.csv", payload);
        }
#endif
    }
}
