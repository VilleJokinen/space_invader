// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Value (unparsed raw string) and coordinates of a single spreadsheet cell. Agnostic
    /// to the source of the data (can be a CSV file, a Google Sheet, or something else).
    /// </summary>
    public readonly struct SpreadsheetCell
    {
        readonly string _value;
        public   string Value => _value ?? string.Empty;

        /// <summary>
        /// Zero indexed row index based on the downloaded sheet data
        /// </summary>
        public readonly int Row;

        /// <summary>
        /// Zero indexed column index based on the downloaded sheet data
        /// </summary>
        public readonly int Column;

        public SpreadsheetCell(string value, int row, int column)
        {
            _value    = value;
            Row       = row;
            Column    = column;
        }

        public bool HasValues()
        {
            return _value != null || Row != 0 || Column != 0;
        }

        public bool Equals(SpreadsheetCell other)
        {
            return _value == other._value && Row == other.Row && Column == other.Column;
        }

        public override bool Equals(object obj)
        {
            return obj is SpreadsheetCell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (_value != null ? _value.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Row;
                hashCode = (hashCode * 397) ^ Column;
                return hashCode;
            }
        }

        public override string ToString() => Invariant($"SpreadsheetCell=(row={Row}, column={Column}, value='{Value}')");
    }

    /// <summary>
    /// Simple utility class for dealing with spreadsheet-like data. Can be used when fetching
    /// data from Google Sheets or when reading data from a .csv file.
    /// </summary>
    public class SpreadsheetContent
    {
        public readonly string                          Name;
        public readonly List<List<SpreadsheetCell>>     Cells;
        public readonly GameConfigSpreadsheetSourceInfo SourceInfo;

        public SpreadsheetContent(string name, List<List<string>> cells, GameConfigSpreadsheetSourceInfo sourceInfo)
            : this(name, ContentToCells(cells), sourceInfo)
        {
        }

        public SpreadsheetContent(string name, List<List<SpreadsheetCell>> cells, GameConfigSpreadsheetSourceInfo sourceInfo)
        {
            Name        = name ?? throw new ArgumentNullException(nameof(name));
            Cells       = cells ?? throw new ArgumentNullException(nameof(cells));
            SourceInfo  = sourceInfo ?? throw new ArgumentNullException(nameof(sourceInfo));
        }

        static List<List<SpreadsheetCell>> ContentToCells(List<List<string>> cells)
        {
            List<List<SpreadsheetCell>> newCells = new List<List<SpreadsheetCell>>(cells.Count);
            for (var row = 0; row < cells.Count; row++)
            {
                List<SpreadsheetCell> rowCells = new List<SpreadsheetCell>(cells[row].Count);
                for (var col = 0; col < cells[row].Count; col++)
                    rowCells.Add(new SpreadsheetCell(cells[row][col], row, col));
                newCells.Add(rowCells);
            }
            return newCells;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(1024);

            foreach (List<SpreadsheetCell> row in Cells)
                sb.Append(string.Join(",", row.Select(cell => EscapeValue(cell.Value))) + "\n");

            return sb.ToString();
        }

        static string EscapeValue(string str)
        {
            // Anything with a quotes or commas needs to be quoted
            if (str.Contains("\"") || str.Contains(","))
                return "\"" + str.Replace("\"", "\"\"") + "\"";
            else
                return str;
        }

        public SpreadsheetContent FilterRows(Func<List<string>, int, bool> predicate)
        {
            List<List<SpreadsheetCell>> outRows = new List<List<SpreadsheetCell>>();
            for (int rowNdx = 0; rowNdx < Cells.Count; rowNdx++)
            {
                if (predicate(Cells[rowNdx].Select(x => x.Value).ToList(), rowNdx))
                    outRows.Add(Cells[rowNdx]);
            }

            return new SpreadsheetContent(Name, outRows, SourceInfo);
        }

        public SpreadsheetContent FilterColumns(Func<string, int, bool> predicate)
        {
            List<bool> keepColumns = Cells[0].Select((SpreadsheetCell cell, int ndx) => predicate(cell.Value, ndx)).ToList();

            List<List<SpreadsheetCell>> outRows = new List<List<SpreadsheetCell>>();
            foreach (List<SpreadsheetCell> inRow in Cells)
            {
                List<SpreadsheetCell> outCells = new List<SpreadsheetCell>();
                for (int colNdx = 0; colNdx < keepColumns.Count; colNdx++)
                {
                    // Filtered column
                    if (!keepColumns[colNdx])
                        continue;
                    // Row has already ended.
                    if (colNdx >= inRow.Count)
                        continue;
                    outCells.Add(inRow[colNdx]);
                }
                outRows.Add(outCells);
            }

            return new SpreadsheetContent(Name, outRows, SourceInfo);
        }

        public string GetCellOrDefault(int rowNdx, int columnNdx, string defaultValue = null)
        {
            if (rowNdx < 0 || rowNdx >= Cells.Count)
                return defaultValue;

            List<SpreadsheetCell> row = Cells[rowNdx];
            if (columnNdx < 0 || columnNdx >= row.Count)
                return defaultValue;

            return row[columnNdx].Value;
        }
    }
}
