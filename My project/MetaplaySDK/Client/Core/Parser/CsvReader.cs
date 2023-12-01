// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Metaplay.Core
{
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
    /// <summary>
    /// Specifies how references to non-existent class members are handled when encountered while parsing config data.
    /// </summary>
    public enum UnknownMemberHandling
    {
        Ignore,     // References to unknown fields are ignored.
        Error,      // Throw an error on encountering a reference to an unknown field.
    }

    /// <summary>
    /// Specifies how csv data rows with more cells than the header has are handled.
    /// </summary>
    public enum ExtraCellHandling
    {
        Ignore,     // Ignore any additional cells beyond the header row.
        Error,      // Throw on any additional cells beyond the header row.
    }

    /// <summary>
    /// Specifies additional rules for various cases in .csv parsing.
    /// </summary>
    public class CsvParseOptions
    {
        public static readonly CsvParseOptions Default = new CsvParseOptions();

        public string                   IgnoreMemberPrefix      = null;                             // Class members with name starting with this prefix are ignored.
        public UnknownMemberHandling    UnknownMemberHandling   = UnknownMemberHandling.Error;      // Specify how references to non-existent class members are handled (ignore or throw).
        public ExtraCellHandling        ExtraCellHandling       = ExtraCellHandling.Error;          // Specify how extraneous columns on a row are handled (ignore or throw)
    }
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE

    /// <summary>
    /// A read-only stream of .csv file, pre-split into rows and cells.
    ///
    /// Support auto-detection of separator (checks whether comma or semi-colon is more common on first line).
    /// </summary>
    public class CsvStream : IDisposable, IEnumerable<string[]>
    {
        public readonly string  FilePath;

        private StreamReader    _reader;
        private char            _separator = '\0';

        public CsvStream(string filePath, Stream input, char separator = '\0')
        {
            FilePath = filePath;
            _reader = new StreamReader(input, Encoding.UTF8);
            _separator = separator;
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<string[]> GetEnumerator()
        {
            while (!_reader.EndOfStream)
            {
                string line = _reader.ReadLine();

                // Skip comments (lines starting with '//')
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    // Comments parse as empty lines (needed for row counters to stay in sync)
                    yield return new string[] { };
                }
                else
                {
                    // If no separator given, detect one (only from non-empty lines)
                    if (_separator == '\0')
                        _separator = DetectSeparator(line);

                    yield return SplitLine(line);
                }
            }
        }

        char DetectSeparator(string line)
        {
            // Count number of commas and semicolons on line
            int numCommas = 0;
            int numSemicolons = 0;
            foreach (char ch in line)
            {
                if (ch == ',') numCommas++;
                if (ch == ';') numSemicolons++;
            }

            // If no commas or semicolons, assume comma to be separator (single-column file)
            if (numCommas == 0 && numSemicolons == 0)
                return ',';

            if (numCommas == numSemicolons)
                throw new ParseError($"Failed to detect character separator in {FilePath} due to same number of commas and semicolons on first line");

            return (numCommas > numSemicolons) ? ',' : ';';
        }

        string ParseQuoted(string str)
        {
            if (str.Length >= 2 && str[0] == '"' && str[str.Length - 1] == '"')
            {
                char[] dst = new char[str.Length];
                int dstNdx = 0;
                for (int srcNdx = 1; srcNdx < str.Length - 1; srcNdx++)
                {
                    char c = str[srcNdx];
                    if (c == '"' && str[srcNdx + 1] == '"')
                    {
                        dst[dstNdx++] = '"';
                        srcNdx++; // skip double quote
                    }
                    else
                        dst[dstNdx++] = c;
                }
                return new string(dst, 0, dstNdx);
            }
            else // not quoted
                return str;
        }

        public string[] SplitLine(string line)
        {
            List<string> cells = new List<string>();

            int startNdx = 0;
            bool isQuoted = false;
            for (int ndx = 0; ndx < line.Length; ndx++)
            {
                char c = line[ndx];
                if (c == _separator && !isQuoted)
                {
                    cells.Add(ParseQuoted(line.Substring(startNdx, ndx - startNdx)));
                    startNdx = ndx + 1;
                }
                else if (c == '"')
                {
                    if (isQuoted)
                    {
                        if (line.Length > ndx + 1 && line[ndx + 1] == '"')
                            ndx++;
                        else
                            isQuoted = false;
                    }
                    else
                        isQuoted = true;
                }
            }

            // Final cell
            cells.Add(ParseQuoted(line.Substring(startNdx)));
            return cells.ToArray();
        }
    }

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
    /// <summary>
    /// Utility class for reading various types of data from a CsvStream.
    ///
    /// Support reading class by fields, arrays of classes, and 2D arrays. Can automatically match
    /// column names from .csv to property or field names in the classes being read.
    /// </summary>
    public static class CsvReader
    {
        internal class StructMember
        {
            public string       Name            { get; private set; }
            public FieldInfo    FieldInfo       { get; private set; }
            public PropertyInfo PropertyInfo    { get; private set; }

            public Type Type => FieldInfo?.FieldType ?? PropertyInfo.PropertyType;

            public StructMember(string name, FieldInfo fieldInfo)
            {
                Name = name;
                FieldInfo = fieldInfo;
            }

            public StructMember(string name, PropertyInfo propInfo)
            {
                Name = name;
                PropertyInfo = propInfo;
            }

            public void SetValue(object obj, object value)
            {
                if (FieldInfo != null)
                    FieldInfo.SetValue(obj, value);
                else
                    PropertyInfo.DeclaringType.GetProperty(PropertyInfo.Name).SetValue(obj, value);
            }
        }

        static StructMember ParseMember(Type type, string memberName)
        {
            FieldInfo fi = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null)
                return new StructMember(memberName, fi);

            PropertyInfo pi = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.GetProperty | BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null)
                return new StructMember(memberName, pi);

            return null;
        }

        public readonly struct KeyValueRow
        {
            public readonly string Key;
            public readonly string Value;
            public readonly int RowNdx;

            public KeyValueRow(string key, string value, int ndx)
            {
                Key = key;
                Value = value;
                RowNdx = ndx;
            }
        }

        static KeyValueRow? RowToKeyValue(string[] row, int rowNdx, string filePath)
        {
            // Skip empty rows
            if (row.Length == 0 || (row.Length == 1 && row[0] == ""))
                return null;

            // \todo [petri] better handling for rows with more than 2 cells (add flag for ignoring?)
            if (row.Length < 2)
                throw new ParseError($"Failed to parse {filePath} row {rowNdx}: expecting at least 2 cells on row, got {row.Length}");

            return new KeyValueRow(row[0], row[1], rowNdx);
        }

        public static T ReadStruct<T>(CsvStream reader) where T : new()
        {
            return ReadStruct<T>(reader.ZipWithIndex().Select(x => RowToKeyValue(x.Value, x.Index, reader.FilePath)).Where(x => x.HasValue).Select(x => x.Value), reader.FilePath);
        }

        public static T ReadStruct<T>(IEnumerable<KeyValueRow> rows, string filePath) where T : new()
        {
            object boxedResult = new T();
            foreach (KeyValueRow row in rows)
            {
                // Resolve member info
                string memberName = row.Key;
                StructMember member = ParseMember(typeof(T), memberName);
                if (member == null)
                    throw new ParseError($"Failed to parse {filePath} row {row.RowNdx}: no such member {typeof(T).Name}.{memberName}");

                try
                {
                    // Only parse non-empty cells
                    string str = row.Value;
                    if (str != "")
                    {
                        object value = ParseCell(member.Type, str);
                        member.SetValue(boxedResult, value);
                    }
                }
                catch (Exception ex)
                {
                    throw new ParseError($"Failed to parse {filePath} row {row.RowNdx}: {typeof(T).Name}.{memberName} value '{row.Value}'", ex);
                }
            }
            return (T)boxedResult;
        }

        public static T[] ReadStructArray<T>(CsvStream reader, CsvParseOptions options = null) where T : new()
        {
            // Use default options when none specified
            if (options == null)
                options = CsvParseOptions.Default;

            Type    rowType = typeof(T);
            List<T> result  = new List<T>();

            IEnumerator<string[]> iter = reader.GetEnumerator();

            // Parse column references from header row
            iter.MoveNext();
            string[] header = iter.Current;
            StructMember[] columns = header.Select(memberName =>
            {
                // Empty columns are ignored
                if (string.IsNullOrEmpty(memberName))
                    return null;

                // Ignore any columns beginning with specified prefix (if in use)
                if (!string.IsNullOrEmpty(options.IgnoreMemberPrefix) && memberName.StartsWith(options.IgnoreMemberPrefix, StringComparison.Ordinal))
                    return null;

                // Parse reference to member (field or property)
                StructMember member = ParseMember(rowType, memberName);
                if (member == null)
                {
                    // Check if should throw error
                    if (options.UnknownMemberHandling == UnknownMemberHandling.Error)
                        throw new InvalidOperationException($"Reference to a non-existent member {rowType.Name}.{memberName} in {reader.FilePath}");
                }
                return member;
            }).ToArray();

            int rowNdx = 0;
            while (iter.MoveNext())
            {
                rowNdx++;

                string[]    row             = iter.Current;
                bool        isContinuation  = (row[0] == "");
                T           rowValue;

                if (!isContinuation)
                {
                    // \note not all rows need to specify all columns
                    //MetaDebug.Assert(row.Length == header.Length, $"Invalid number of columns on row {rowNdx} of {filePath} (got {row.Length}, expecting {header.Length})");

                    // Allocate element for row
                    rowValue = new T();
                }
                else
                    rowValue = result[result.Count - 1]; // \todo [petri] remember last elem?

                // Handle extraneous cells on row (more than in header)
                if (row.Length > columns.Length)
                {
                    if (options.ExtraCellHandling == ExtraCellHandling.Error)
                        throw new ParseError($"Failed to parse {reader.FilePath}: too many columns on row {rowNdx} (has {row.Length}, expecting at most {columns.Length})");
                }

                // Only cells handle up to maximum of row and header
                int numCells = System.Math.Min(row.Length, columns.Length);
                for (int colNdx = 0; colNdx < numCells; colNdx++)
                {
                    StructMember    member  = columns[colNdx];
                    string          str     = row[colNdx];

                    // Skip null members (empty header cell)
                    if (member == null)
                        continue;

                    // Only parse non-empty cells
                    if (str != "")
                    {
                        try
                        {
                            // \todo [petri] support element-cells for collection types (eg, [SomeList] as column name) ?
                            object value = ParseCell(member.Type, str);
                            member.SetValue(rowValue, value);
                        }
                        catch (Exception ex)
                        {
                            throw new ParseError($"Failed to parse {rowType.Name}.{member.Name} value '{str}' (in {reader.FilePath} row {rowNdx}, column {colNdx + 1})", ex);
                        }
                    }
                }

                if (!isContinuation)
                    result.Add(rowValue);
            }

            return result.ToArray();
        }

        public static OrderedDictionary<TKey, TValue> ReadOrderedDictionary<TKey, TValue>(CsvStream stream, Func<string, TKey> keyParseFunc, Func<string, TValue> valueParseFunc)
        {
            OrderedDictionary<TKey, TValue> dict = new OrderedDictionary<TKey, TValue>();

            foreach (string[] cells in stream)
            {
                if (cells.Length != 2)
                    throw new InvalidOperationException($"Error parsing {stream.FilePath}: Expecting exactly two values on row in: " + string.Join("; ", cells));

                TKey key = keyParseFunc(cells[0]);
                TValue value = valueParseFunc(cells[1]);
                dict.Add(key, value);
            }

            return dict;
        }

        public static T[,] ReadArray2D<T>(CsvStream reader) where T : class
        {
            Type cellType = typeof(T);
            List<List<T>> result = new List<List<T>>();

            // Parse all rows
            IEnumerator<string[]> rowIter = reader.GetEnumerator();
            int rowNdx = 0;
            while (rowIter.MoveNext())
            {
                rowNdx++;

                // Parse all cells on the row
                string[] rowCells = rowIter.Current;
                List<T> row = new List<T>();
                for (int colNdx = 0; colNdx < rowCells.Length; colNdx++)
                {
                    string str = rowCells[colNdx];
                    if (str != "")
                    {
                        try
                        {
                            T value = (T)ParseCell(cellType, str);
                            row.Add(value);
                        }
                        catch (Exception ex)
                        {
                            throw new ParseError($"Failed to parse {reader.FilePath}.{cellType.Name} (row {rowNdx}, col {colNdx + 2}), value '{row[colNdx]}'", ex);
                        }
                    }
                }

                result.Add(row);
            }

            return ToArray2D(result);
        }

        static T[,] ToArray2D<T>(List<List<T>> input)
        {
            // \todo [petri] assumes non-zero, square dimensions!
            int height = input.Count;
            int width = input.Max(row => row.Count);
            T[,] result = new T[width, height];

            for (int rowNdx = 0; rowNdx < input.Count; rowNdx++)
            {
                List<T> row = input[rowNdx];
                //MetaDebug.Assert(row.Count == width, "Dimensions must be square ({0} on row {1}, expecting {2})!", row.Count, rowNdx, width);
                for (int colNdx = 0; colNdx < row.Count; colNdx++)
                    result[colNdx, rowNdx] = row[colNdx];
            }

            return result;
        }

        static object ParseValue(Type type, ConfigLexer lexer)
        {
            if (ConfigParser.TryParse(type, lexer, out object configParserResult))
                return configParserResult;
            else
                throw new ParseError($"Invalid type for cell in csv: {type.Name}");
        }

        public static object ParseCell(Type cellType, string inputStr)
        {
            // Skip lexing for strings
            if (cellType == typeof(string))
                return inputStr;

            // \todo [petri] recycle lexer?
            ConfigLexer lexer = new ConfigLexer(inputStr);

            object result = ParseCellImpl(cellType, lexer);
            if (!lexer.IsAtEnd)
                throw new ParseError($"CSV cell was not fully consumed when parsing type {cellType.ToGenericTypeString()} from '{inputStr}'; remaining input is '{lexer.Input.Substring(lexer.CurrentToken.StartOffset)}'");

            return result;
        }

        static object ParseCellImpl(Type cellType, ConfigLexer lexer)
        {
            // Handle list types separately
            // \todo [petri] other container types?
            if (typeof(IList).IsAssignableFrom(cellType))
            {
                // Figure out element type in container
                Type elemType = cellType.GetCollectionElementType();
                IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType));

                while (!lexer.IsAtEnd)
                {
                    // Parse element and add to list
                    object elem = ParseValue(elemType, lexer);
                    list.Add(elem);

                    // Stop reading when no more commas found
                    if (!lexer.TryParseToken(ConfigLexer.TokenType.Comma))
                        break;
                }

                if (cellType.IsArray)
                {
                    Array array = Array.CreateInstance(elemType, list.Count);
                    for (int ndx = 0; ndx < list.Count; ndx++)
                        array.SetValue(list[ndx], ndx);
                    return array;
                }
                else if (cellType.IsGenericTypeOf(typeof(List<>)))
                    return list;
                else
                    return Activator.CreateInstance(cellType, list);
            }
            else
            {
                return ParseValue(cellType, lexer);
            }
        }
    }
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE
}
