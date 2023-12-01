// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Base class for game config build operation source info. The source can be, for example,
    /// a CSV or JSON file, a Google Sheet, a Unity ScriptableObject, or other similar source.
    /// </summary>
    public abstract class GameConfigSourceInfo
    {
        /// <summary>
        /// Returns a short, human-readable description of the source. Used in the game config build log messages.
        /// </summary>
        /// <returns></returns>
        public abstract string GetShortName();

        /// <summary>
        /// Convert a specific location within the source to an URL.
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        public abstract string GetLocationUrl(GameConfigSourceLocation location);

        public override bool Equals(object obj) =>
            throw new NotImplementedException("Concrete classes inheriting GameConfigSourceInfo must implement Equals()!");

        public override int GetHashCode() =>
            throw new NotImplementedException("Concrete classes inheriting GameConfigSourceInfo must implement GetHashCode()!");
    }

    /// <summary>
    /// Base class for general spreadsheet (eg, from Google Sheets, from a CSV file, or other
    /// spreadsheet source). Contains some general utilies for handling row and colunm offsets, etc.
    /// Specific spreadsheet sources can derive their own classes from this one.
    /// </summary>
    public abstract class GameConfigSpreadsheetSourceInfo : GameConfigSourceInfo
    {
        public abstract string GetSheetName(); // \todo Temporary solution to allow GameConfigValidationResult to match sheetNames -- it only has the stringified name of the sheet

        /// <summary>
        /// Convert a 0-based column index into a Excel/Google Sheet -compatible string format. Values 0..25
        /// are converted to characters A..Z, and values beyond that get multi-character strings, starting with
        /// AA for 26, AB for 27, and so on.
        /// </summary>
        /// <param name="colNdx"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static string ColumnIndexToString(int colNdx)
        {
            if (colNdx < 0)
                throw new ArgumentException("Column index must be non-negative", nameof(colNdx));
            if (colNdx == int.MaxValue)
                throw new ArgumentException("Column index must be smaller than int.MaxValue", nameof(colNdx));

            // Compute characters (in reverse order)
            const int vocabSize = 26;
            Span<char> chars = stackalloc char[32];
            int remaining = colNdx + 1;
            int outNdx = 0;
            for (; ; )
            {
                int charNdx = (remaining - 1) % vocabSize;
                chars[outNdx++] = (char)('A' + charNdx);
                remaining = (remaining - charNdx) / vocabSize;

                if (remaining == 0)
                    break;
            }

            // Reverse the chars & return as string
            Span<char> active = chars.Slice(0, outNdx);
            active.Reverse();
            return new string(active);
        }

        /// <summary>
        /// Convert a spreadsheet location (range of cells) to string representation, in format that is compatible
        /// with Google Sheets links.
        /// </summary>
        /// <example>
        /// Full document: null
        /// Single cell:   C12
        /// Row range:     5:5, 5:10
        /// Column range:  B:B, B:F
        /// Cell range:    C12:F12, C12:F21
        /// </example>
        /// <param name="location"></param>
        /// <returns></returns>
        public static string CellRangeToString(GameConfigSpreadsheetLocation location)
        {
            GameConfigSpreadsheetLocation.CellRange rows = location.Rows;
            GameConfigSpreadsheetLocation.CellRange cols = location.Columns;

            // \note Google Sheets range end is inclusive, but it uses 1-based row indices.
            if (rows.IsFullRange && cols.IsFullRange) // whole sheet
                return null;
            else if (rows.IsFullRange) // columns (one or more)
                return $"{ColumnIndexToString(cols.Start)}:{ColumnIndexToString(cols.End - 1)}";
            else if (cols.IsFullRange) // rows (one or more)
                return Invariant($"{rows.Start + 1}:{rows.End + 1 - 1}");
            else if (cols.IsRange || rows.IsRange) // range of cells
                return Invariant($"{ColumnIndexToString(cols.Start)}{rows.Start + 1}:{ColumnIndexToString(cols.End - 1)}{rows.End + 1 - 1}");
            else // single-cell
                return Invariant($"{ColumnIndexToString(cols.Start)}{rows.Start + 1}");
        }
    }

    public class SpreadsheetFileSourceInfo : GameConfigSpreadsheetSourceInfo
    {
        public readonly string FilePath;

        public SpreadsheetFileSourceInfo(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public override string GetShortName() => $"file {FilePath}";

        public override string GetSheetName() => FilePath.Replace(".csv", ""); // \todo Kludgy way to get this to match with gameConfigEntryName when reporting MetaRefResolveErrors

        public override string ToString() => $"SpreadsheetFile:{FilePath}";

        public override string GetLocationUrl(GameConfigSourceLocation location)
        {
            if (location is not GameConfigSpreadsheetLocation sheetLocation)
                throw new ArgumentException($"Invalid location type {location.GetType().ToGenericTypeString()}, only {typeof(GameConfigSpreadsheetLocation).ToGenericTypeString()} is supported", nameof(location));

            // \todo Implement properly: support single cells, full rows/columns, ranges of cells, ranges of rows/columns, ..
            return $"{FilePath}:{CellRangeToString(sheetLocation)}";
        }

        public override bool Equals(object obj)
        {
            if (obj is not SpreadsheetFileSourceInfo other)
                return false;

            return Equals(FilePath, other.FilePath);
        }

        public override int GetHashCode() =>
            FilePath.GetHashCode();
    }

    public class GoogleSheetSourceInfo : GameConfigSpreadsheetSourceInfo
    {
        public readonly string  SpreadsheetId;              // Identity of the spreadsheet document (contains multiple tabs).
        public readonly string  SheetName;                 // Human-readable name of the specific sheet (tab) on the spreadsheet.
        public readonly int     SheetId;                    // Identity of the specific sheet (tab) on the spreadsheet.

        public GoogleSheetSourceInfo(string spreadsheetId, string sheetName, int sheetId)
        {
            SpreadsheetId = spreadsheetId;
            SheetName = sheetName;
            SheetId = sheetId;
        }

        public override string GetShortName() => $"sheet '{SheetName}'";

        public override string GetSheetName() => SheetName;

        public override string ToString()
        {
            return Invariant($"GoogleSheet:{SpreadsheetId}, {SheetName} (gid={SheetId})");
        }

        public override string GetLocationUrl(GameConfigSourceLocation location)
        {
            if (location is not GameConfigSpreadsheetLocation sheetLocation)
                throw new ArgumentException($"Invalid location type {location.GetType().ToGenericTypeString()}, only {typeof(GameConfigSpreadsheetLocation).ToGenericTypeString()} is supported", nameof(location));

            // Combine the final url to spreadsheet, with sheetId and cell range
            // \note if location represents whole document, the 'range=' ends up being empty
            return Invariant($"https://docs.google.com/spreadsheets/d/{SpreadsheetId}/edit#gid={SheetId}&range={CellRangeToString(sheetLocation)}");
        }

        public override bool Equals(object obj)
        {
            if (obj is not GoogleSheetSourceInfo other)
                return false;

            return Equals(SpreadsheetId, other.SpreadsheetId)
                && Equals(SheetName, other.SheetName)
                && Equals(SheetId, other.SheetId);
        }

        public override int GetHashCode() =>
            Util.CombineHashCode(SpreadsheetId.GetHashCode(), SheetName.GetHashCode(), SheetId.GetHashCode());
    }
}
