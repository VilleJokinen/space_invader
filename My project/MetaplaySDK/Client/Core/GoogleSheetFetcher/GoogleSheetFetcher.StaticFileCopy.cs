// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Json;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    partial class GoogleSheetFetcher
    {
        /// <summary>
        /// Note: only for local debugging! Don't commit with this enabled!
        ///
        /// <para>
        /// If enabled, the fetcher will save the downloaded sheets (as json) in <see cref="DebugStaticFileCopyDirectoryPath"/>
        /// on the first run, and then subsequently use those files instead of fetching them.
        /// Note that it will never automatically refresh those files, you need to delete them manually to do that.
        /// Therefore this is only suitable for local debugging.
        /// </para>
        /// <para>
        /// This is useful for getting a fixed copy of the configs (when making a copy in Google Sheets is not practical)
        /// in case the source sheets are simultaneously being modified by other people, for the purpose of getting
        /// reproducible config build results for checking for regressions.
        /// </para>
        /// </summary>
        readonly static bool DebugEnableStaticFileCopy = false;
        readonly static string DebugStaticFileCopyDirectoryPath = "GoogleSheetFetcherStaticCopy";

        static string GetSpreadsheetContentStaticCopyFilePath(string spreadsheetId, string sheetName)
        {
            return $"{DebugStaticFileCopyDirectoryPath}/sheet-{spreadsheetId}-{sheetName}.json";
        }

        // \note Lazy to avoid unnecessarily creating the serializer when file copies are disabled.
        static readonly Lazy<JsonSerializer> StaticFileCopySerializer = new Lazy<JsonSerializer>(() => JsonSerializer.CreateDefault(new JsonSerializerSettings{ Formatting = Formatting.Indented }));

        static async Task<SpreadsheetContent> TryLoadSheetFromStaticFileCopyAsync(string spreadsheetId, string sheetName)
        {
            string path = GetSpreadsheetContentStaticCopyFilePath(spreadsheetId, sheetName);
            string json;
            try
            {
                json = await FileUtil.ReadAllTextAsync(path);
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            SpreadsheetContentSerializationProxy sheetProxy = JsonSerialization.Deserialize<SpreadsheetContentSerializationProxy>(json, StaticFileCopySerializer.Value);
            SpreadsheetContent sheet = sheetProxy.ToSheet();
            return sheet;
        }

        static Task SaveSheetToStaticFileCopyAsync(string spreadsheetId, SpreadsheetContent sheet)
        {
            SpreadsheetContentSerializationProxy sheetProxy = SpreadsheetContentSerializationProxy.FromSheet(sheet);
            string json = JsonSerialization.SerializeToString(sheetProxy, StaticFileCopySerializer.Value);
            string path = GetSpreadsheetContentStaticCopyFilePath(spreadsheetId, sheet.Name);
            Directory.CreateDirectory(DebugStaticFileCopyDirectoryPath);
            return FileUtil.WriteAllTextAsync(path, json);
        }

        /// <summary>
        /// A json-serializable (mostly) equivalent of <see cref="SpreadsheetContent"/>.
        /// ("Mostly" because this makes some extra assumptions, see <see cref="FromSheet"/> implementation.)
        /// This way we avoid making compromises in <see cref="SpreadsheetContent"/> to make it json-serializable
        /// just for this debug feature, and also we can make this one a bit more suitable for serialization
        /// (<see cref="SpreadsheetContent"/> has some redundancy, e.g. each cell points to <see cref="SpreadsheetContent.DebugInfo"/>).
        /// </summary>
        class SpreadsheetContentSerializationProxy
        {
            public string               Name;
            public string               SpreadsheetId;
            public int                  SheetId;
            public List<List<string>>   Cells;

            public SpreadsheetContent ToSheet()
            {
                return new SpreadsheetContent(Name, Cells, new GoogleSheetSourceInfo(SpreadsheetId, Name, SheetId));
            }

            public static SpreadsheetContentSerializationProxy FromSheet(SpreadsheetContent sheet)
            {
                SpreadsheetContentSerializationProxy proxy = new SpreadsheetContentSerializationProxy();
                GoogleSheetSourceInfo sourceInfo = (sheet.SourceInfo as GoogleSheetSourceInfo) ?? throw new InvalidOperationException($"Expected a {nameof(GoogleSheetFetcher)}-downloaded sheet");
                proxy.Name = sheet.Name;
                proxy.SpreadsheetId = sourceInfo.SpreadsheetId;
                proxy.SheetId = sourceInfo.SheetId;

                int numRows = sheet.Cells.Count;
                proxy.Cells = new List<List<string>>(capacity: numRows);
                for (int rowNdx = 0; rowNdx < numRows; rowNdx++)
                {
                    List<SpreadsheetCell> sourceRow = sheet.Cells[rowNdx];
                    List<string> row = new List<string>(capacity: sourceRow.Count);
                    for (int colNdx = 0; colNdx < sourceRow.Count; colNdx++)
                    {
                        SpreadsheetCell cell = sourceRow[colNdx];
                        if (cell.Row != rowNdx || cell.Column != colNdx)
                            throw new InvalidOperationException($"Expected unmodified source sheet, but cell at {{ row={rowNdx}, col={colNdx} }} has source location {{ row={cell.Row}, col={cell.Column} }}");
                        row.Add(cell.Value);
                    }
                    proxy.Cells.Add(row);
                }

                return proxy;
            }
        }

        class StaticFileCopyFetcher : IGoogleSheetFetcherImpl
        {
            IGoogleSheetFetcherImpl _realFetcher;

            public StaticFileCopyFetcher(IGoogleSheetFetcherImpl realFetcher)
            {
                _realFetcher = realFetcher;
            }

            public async Task<IReadOnlyList<SpreadsheetContent>> FetchSheetsAsync(object credentials, string spreadsheetId, IEnumerable<string> sheetNames, CancellationToken ct)
            {
                DebugLog.Warning("Using static file copies for sheet fetcher!");

                // Try to get each sheet from the file copy.

                List<SpreadsheetContent> sheets = (await Task.WhenAll(sheetNames.Select(name => TryLoadSheetFromStaticFileCopyAsync(spreadsheetId, name)))).ToList();

                // Check which ones were missing file copies.

                List<(int Index, string Name)> missingSheets = new List<(int Index, string Name)>();
                foreach ((string sheetName, int index) in sheetNames.ZipWithIndex())
                {
                    if (sheets[index] == null)
                        missingSheets.Add((index, sheetName));
                }

                DebugLog.Info("{NumSheetsFromFiles} sheets found from static file copy ({DirectoryPath}): {SheetNames}.", sheets.Count(s => s != null), DebugStaticFileCopyDirectoryPath, string.Join(", ", sheets.Where(s => s != null).Select(s => s.Name)));
                DebugLog.Info("{NumMissingSheets} sheets are missing static file copies and will be fetched.", missingSheets.Count);

                // Fetch all the missing sheets and save to files.

                if (missingSheets.Count > 0)
                {
                    IReadOnlyList<SpreadsheetContent> fetchedSheets = await _realFetcher.FetchSheetsAsync(credentials, spreadsheetId, missingSheets.Select(s => s.Name), ct);

                    await Task.WhenAll(fetchedSheets.Select(sheet => SaveSheetToStaticFileCopyAsync(spreadsheetId, sheet)));

                    foreach (((int index, string name), SpreadsheetContent sheet) in missingSheets.Zip(fetchedSheets, (x, y) => (x, y)))
                        sheets[index] = sheet;
                }

                MetaDebug.Assert(!sheets.Contains(null), "All sheets should be present at this point");

                return sheets;
            }

            public Task<object> CredentialsFromFileAsync(string path, CancellationToken ct)
            {
                return _realFetcher.CredentialsFromFileAsync(path, ct);
            }

            public Task<object> CredentialsFromUserInputAsync(string clientId, string clientSecret, CancellationToken ct)
            {
                return _realFetcher.CredentialsFromUserInputAsync(clientId, clientSecret, ct);
            }

            public object CredentialsFromJsonString(string json)
            {
                return _realFetcher.CredentialsFromJsonString(json);
            }

            public Task<GoogleSpreadsheetMetadata> FetchSpreadsheetMetadataAsync(object credentials, string spreadsheetId, CancellationToken ct)
            {
                // \todo: cache metadata as well?
                return _realFetcher.FetchSpreadsheetMetadataAsync(credentials, spreadsheetId, ct);
            }
        }
    }
}
