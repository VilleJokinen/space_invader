// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Globalization;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Abstract base class to represent a source location for game config input data.
    /// The location can be for example a cell (or a cell range) within a spreadsheet
    /// (see <see cref="GameConfigSpreadsheetLocation"/>).
    /// </summary>
    public abstract class GameConfigSourceLocation
    {
        public readonly GameConfigSourceInfo SourceInfo; // Link to source that this location is part of

        protected GameConfigSourceLocation(GameConfigSourceInfo sourceInfo)
        {
            SourceInfo = sourceInfo ?? throw new ArgumentNullException(nameof(sourceInfo));
        }

        /// <summary>
        /// Try to narrow this location with another location. For example, if this is the location of an item
        /// (eg, a full row on a spreadsheet), we can narrow it down to a given member/column (<paramref name="subLocation"/>)
        /// to get the the specific cell where the item's given member is located (ie, the matching column on the item's
        /// row).
        /// </summary>
        /// <param name="subLocation">The location to narrow down to (eg, member column on a spreadsheet or a sub-path for tree-like inputs).</param>
        /// <param name="narrowedLocation">Resulting narrowed location from the operation, if successful.</param>
        /// <returns>True if was able to narrow the location, false otherwise.</returns>
        public abstract bool TryNarrowToLocation(GameConfigSourceLocation subLocation, out GameConfigSourceLocation narrowedLocation);
    }

    /// <summary>
    /// Represents a location in a spreadsheet. Can be either a single cell, a finite
    /// range of rows or columns, or an infinite range to represent full rows or columns.
    /// </summary>
    public class GameConfigSpreadsheetLocation : GameConfigSourceLocation
    {
        /// <summary>
        /// Range of rows or columns in a spreadsheet. Allows representing either a finite range [Start, End) or a
        /// range containing all the rows or columns <see cref="All"/>. Uses 0-based indexing. Note that Google Sheets
        /// and Excel use 1-based indexing.
        /// </summary>
        public readonly struct CellRange
        {
            public readonly int Start;
            public readonly int End;    // \note Uses int.MaxValue to represent full range

            /// <summary> Is this a single-cell range? </summary>
            public readonly bool IsSingle => End - Start == 1;

            /// <summary> Is this a range with multiple cells? </summary>
            public readonly bool IsRange => End - Start != 1; // \note Also handles full range (end == int.MaxValue)

            /// <summary> Is this a range that includes all the cells? </summary>
            public readonly bool IsFullRange => End == int.MaxValue; // End == int.MaxValue marks the full range

            /// <summary> Get the number of cells in the range (returns int.MaxValue for full ranges) </summary>
            public readonly int  Length => End - Start;

            /// <summary> Full range of cells from 0 to "infinity" (any sized input). </summary>
            public static readonly CellRange All = new CellRange(0, int.MaxValue);

            public CellRange(int start, int end)
            {
                if (start < 0)
                    throw new ArgumentException($"Start must be a non-negative value, got {start}");
                if (end < 0)
                    throw new ArgumentException($"End must be a non-negative value, got {end}");
                if (start == end)
                    throw new ArgumentException($"Empty ranges are not supported (end must be greater than start, got {start} and {end})");
                if (end < start)
                    throw new ArgumentException($"Negative ranges are not supported (end must be greater than start, got {start} and {end})");

                Start = start;
                End = end;
            }

            /// <summary>
            /// Try to find the intersection of two ranges. If the two ranges don't overlap, there is no
            /// legit intersection.
            /// </summary>
            /// <param name="a"></param>
            /// <param name="b"></param>
            /// <param name="result">The intersection range, only meaningful if the input ranges overlap.</param>
            /// <returns>True if the two overlapped and there is a legit intersection, false otherwise.</returns>
            public static bool TryIntersect(CellRange a, CellRange b, out CellRange result)
            {
                int start = System.Math.Max(a.Start, b.Start);
                int end = System.Math.Min(a.End, b.End);
                if (start < end)
                {
                    result = new CellRange(start, end);
                    return true;
                }
                else
                {
                    result = default;
                    return false;
                }
            }

            public override string ToString()
            {
                if (IsSingle)
                    return Start.ToString(CultureInfo.InvariantCulture);
                else if (IsFullRange)
                    return Invariant($"{Start}:<End>");
                else
                    return Invariant($"{Start}:{End}");
            }
        }

        public readonly CellRange Rows;
        public readonly CellRange Columns;

        public GameConfigSpreadsheetLocation(GameConfigSourceInfo sourceInfo, CellRange rows, CellRange columns) : base(sourceInfo)
        {
            if (sourceInfo is not GameConfigSpreadsheetSourceInfo)
                throw new ArgumentException($"Spreadsheet locations can only have SourceInfo of type {typeof(GameConfigSpreadsheetSourceInfo).Name}", nameof(sourceInfo));

            Rows    = rows;
            Columns = columns;
        }

        public static GameConfigSpreadsheetLocation FromCoords(GameConfigSourceInfo sourceInfo, int row, int column) =>
            new GameConfigSpreadsheetLocation(sourceInfo, new CellRange(row, row + 1), new CellRange(column, column + 1));

        public static GameConfigSpreadsheetLocation FromCell(GameConfigSourceInfo sourceInfo, SpreadsheetCell cell) =>
            new GameConfigSpreadsheetLocation(sourceInfo, new CellRange(cell.Row, cell.Row + 1), new CellRange(cell.Column, cell.Column + 1));

        public static GameConfigSpreadsheetLocation FromRows(GameConfigSourceInfo sourceInfo, int rowStart, int rowEnd) =>
            new GameConfigSpreadsheetLocation(sourceInfo, new CellRange(rowStart, rowEnd), CellRange.All);

        public static GameConfigSpreadsheetLocation FromColumns(GameConfigSourceInfo sourceInfo, int colStart, int colEnd) =>
            new GameConfigSpreadsheetLocation(sourceInfo, CellRange.All, new CellRange(colStart, colEnd));

        public static GameConfigSpreadsheetLocation FromFullSheet(GameConfigSourceInfo sourceInfo) =>
            new GameConfigSpreadsheetLocation(sourceInfo, CellRange.All, CellRange.All);

        public override bool TryNarrowToLocation(GameConfigSourceLocation dstLocation, out GameConfigSourceLocation narrowedLocation)
        {
            // Can only narrow between matching types
            if (dstLocation is not GameConfigSpreadsheetLocation dstSheetLocation)
                throw new ArgumentException($"Can only narrow {GetType().Name} to other {GetType().Name}, got {dstLocation.GetType().Name}");

            // If source infos don't match, bail out
            if (!Equals(dstSheetLocation.SourceInfo, SourceInfo))
            {
                narrowedLocation = null;
                return false;
            }

            // Try to intersect the rows and columns
            if (!CellRange.TryIntersect(Rows, dstSheetLocation.Rows, out CellRange narrowedRows) || !CellRange.TryIntersect(Columns, dstSheetLocation.Columns, out CellRange narrowedCols))
            {
                // There was no overlap between the cell ranges (the item location should still be good enough)
                narrowedLocation = null;
                return false;
            }

            // Got a legit intersection, return it
            narrowedLocation = new GameConfigSpreadsheetLocation(SourceInfo, narrowedRows, narrowedCols);
            return true;
        }

        public override string ToString() =>
            $"GameConfigSpreadsheetLocation(row:{Rows}, column:{Columns})";
    }
}
