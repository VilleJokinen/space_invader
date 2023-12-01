// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using NUnit.Framework;

namespace Cloud.Tests
{
    class GameConfigLocationTests
    {
        [Test]
        public void CellRangeToString()
        {
            SpreadsheetFileSourceInfo sourceInfo = new SpreadsheetFileSourceInfo("Dummy.csv");

            // Single cell, columns
            Assert.AreEqual("A1", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 0, column: 0)));
            Assert.AreEqual("B2", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 1, column: 1)));
            Assert.AreEqual("Z3", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 2, column: 25)));
            Assert.AreEqual("AA4", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 3, column: 26)));
            Assert.AreEqual("AZ5", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 4, column: 51)));

            // Single cell, rows
            Assert.AreEqual("A1", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 0, column: 0)));
            Assert.AreEqual("C10", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 9, column: 2)));
            Assert.AreEqual("E21", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromCoords(sourceInfo, row: 20, column: 4)));

            // Full column ranges
            Assert.AreEqual("A:A", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromColumns(sourceInfo, colStart: 0, colEnd: 1)));
            Assert.AreEqual("A:Z", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromColumns(sourceInfo, colStart: 0, colEnd: 26)));
            Assert.AreEqual("AA:AZ", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromColumns(sourceInfo, colStart: 26, colEnd: 52)));

            // Full row ranges
            Assert.AreEqual("1:1", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromRows(sourceInfo, rowStart: 0, rowEnd: 1)));
            Assert.AreEqual("1:10", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromRows(sourceInfo, rowStart: 0, rowEnd: 10)));
            Assert.AreEqual("11:21", GameConfigSpreadsheetSourceInfo.CellRangeToString(GameConfigSpreadsheetLocation.FromRows(sourceInfo, rowStart: 10, rowEnd: 21)));

            // Cell ranges
            Assert.AreEqual("A1:B1", GameConfigSpreadsheetSourceInfo.CellRangeToString(new GameConfigSpreadsheetLocation(sourceInfo, rows: new GameConfigSpreadsheetLocation.CellRange(0, 1), columns: new GameConfigSpreadsheetLocation.CellRange(0, 2))));
            Assert.AreEqual("A1:A2", GameConfigSpreadsheetSourceInfo.CellRangeToString(new GameConfigSpreadsheetLocation(sourceInfo, rows: new GameConfigSpreadsheetLocation.CellRange(0, 2), columns: new GameConfigSpreadsheetLocation.CellRange(0, 1))));
            Assert.AreEqual("E8:AA12", GameConfigSpreadsheetSourceInfo.CellRangeToString(new GameConfigSpreadsheetLocation(sourceInfo, rows: new GameConfigSpreadsheetLocation.CellRange(7, 12), columns: new GameConfigSpreadsheetLocation.CellRange(4, 27))));
        }
    }
}
