// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Schedule;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
#if UNITY_EDITOR || NETCOREAPP
using System.Linq.Expressions;
#endif
using System.Reflection;
using System.Text.RegularExpressions;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE

    public class SpreadsheetParsePrepareFeedback
    {
        public List<int> UsedColumnIndexes = new List<int>();

        public SpreadsheetParsePrepareFeedback(IEnumerable<int> usedColumnIndexes)
        {
            UsedColumnIndexes = usedColumnIndexes.ToList();
        }
    }

    public interface ISpreadsheetParserPass
    {
        SpreadsheetParsePrepareFeedback Prepare(List<SpreadsheetCell> headerRow, SpreadsheetParseOptions spreadsheetParseOptions, bool debugPrint);
        void ParseItem(object item, List<SpreadsheetCell> cells, bool allowPartial = false);
    }

    public abstract class SpreadsheetParserPass<TItem> : ISpreadsheetParserPass
    {
        /// <summary>
        /// Optimized setters for properties, and setters for fields.
        /// </summary>
        Dictionary<MemberInfo, Action<object, object>> _cachedSetters = new Dictionary<MemberInfo, Action<object, object>>();

        public abstract SpreadsheetParsePrepareFeedback Prepare(List<SpreadsheetCell> headerRow, SpreadsheetParseOptions spreadsheetParseOptions, bool debugPrint);

        public abstract void ParseItem(TItem item, List<SpreadsheetCell> spreadsheetCells, bool allowPartial = false);

        void ISpreadsheetParserPass.ParseItem(object item, List<SpreadsheetCell> cells, bool allowPartial)
        {
            ParseItem((TItem)item, cells, allowPartial);
        }

        protected Type GetMemberType(MemberInfo memberInfo)
        {
            switch (memberInfo)
            {
                case PropertyInfo propInfo: return propInfo.PropertyType;
                case FieldInfo fieldInfo: return fieldInfo.FieldType;
                default:
                    throw new InvalidOperationException($"Invalid member type {memberInfo.Name}: {memberInfo.GetType().FullName}");
            }
        }

        protected void SetMemberValue(MemberInfo memberInfo, object item, object value)
        {
            if (!_cachedSetters.ContainsKey(memberInfo))
                CreateAndAddCachedSetter(memberInfo);

            _cachedSetters[memberInfo].Invoke(item, value);
        }

        void CreateAndAddCachedSetter(MemberInfo memberInfo)
        {
            switch (memberInfo)
            {
                case PropertyInfo propInfo:
                    MethodInfo methodInfo = propInfo.GetSetMethodOnDeclaringType();
                    Type       actionType = typeof(Action<,>).MakeGenericType(methodInfo.DeclaringType, propInfo.PropertyType);
                    Delegate   del        = methodInfo.CreateDelegate(actionType);

                    _cachedSetters.Add(memberInfo, (target, val) => del.DynamicInvoke(target, val));
                    break;
                case FieldInfo fieldInfo:
                    _cachedSetters.Add(memberInfo, fieldInfo.SetValue);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid member type {memberInfo.Name}: {memberInfo.GetType().FullName}");
            }
        }
    }

    public class DirectMemberParserPass<TItem> : SpreadsheetParserPass<TItem>
    {
        List<(int, MemberInfo)> _matchingColumns = new List<(int, MemberInfo)>();

        SpreadsheetParseOptions _parseOptions;

        public override SpreadsheetParsePrepareFeedback Prepare(List<SpreadsheetCell> headerRow, SpreadsheetParseOptions spreadsheetParseOptions, bool debugPrint)
        {
            _parseOptions = spreadsheetParseOptions;

            // Resolve all members with names matching columns
            HashSet<SpreadsheetCell> seenColumns = new HashSet<SpreadsheetCell>();
            List<SpreadsheetCell>    columns     = headerRow;
            for (int colNdx = 0; colNdx < columns.Count; colNdx++)
            {
                string     name       = columns[colNdx].Value;
                MemberInfo memberInfo = typeof(TItem).GetMember(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault();

                if (memberInfo != null)
                {
                    //DebugLog.Info("Matching member column #{ColNdx} to {MemberName}", colNdx, memberInfo.Name);
                    _matchingColumns.Add((colNdx, memberInfo));
                    if (!seenColumns.Add(columns[colNdx]))
                        throw new ConfigParserException($"Multiple columns for member {memberInfo.Name}", seenColumns.ToList());
                }
            }

            return new SpreadsheetParsePrepareFeedback(usedColumnIndexes: _matchingColumns.Select(((int, MemberInfo) colNdxInfo) => colNdxInfo.Item1));
        }

        public override void ParseItem(TItem item, List<SpreadsheetCell> cells, bool allowPartial = false)
        {
            foreach ((int colNdx, MemberInfo memberInfo) in _matchingColumns)
            {
                // Row doesn't contain the column (trailing empty cells truncated)?
                if (colNdx >= cells.Count)
                    continue;

                SpreadsheetCell cell = cells[colNdx];
                if (cell.Value == "")
                    continue;

                // \todo [petri] using CsvReader -- rename to SpreadsheetParser ?
                try
                {
                    object value = CsvReader.ParseCell(GetMemberType(memberInfo), cell.Value);
                    SetMemberValue(memberInfo, item, value);
                }
                catch (Exception ex)
                {
                    throw new ConfigParserException("Failed to parse value", cell, ex);
                }
            }
        }
    }

    public class ValueCollectionParserPass<TItem> : SpreadsheetParserPass<TItem>
    {
        public class CollectionInfo
        {
            public MemberInfo MemberInfo;
            public Type       MemberType;
            public Type       ElementType;
            public List<int>  ColumnIndexes = new List<int>();

            public CollectionInfo(MemberInfo memberInfo, Type memberType, Type elementType)
            {
                MemberInfo  = memberInfo;
                MemberType  = memberType;
                ElementType = elementType;
            }
        };

        Dictionary<string, CollectionInfo> _collections = new Dictionary<string, CollectionInfo>();
        SpreadsheetParseOptions            _parseOptions;

#if UNITY_EDITOR || NETCOREAPP
        Dictionary<Type, Func<IList>> cachedInstanceCreators = new Dictionary<Type, Func<IList>>();
#endif

        public override SpreadsheetParsePrepareFeedback Prepare(List<SpreadsheetCell> headerRow, SpreadsheetParseOptions spreadsheetParseOptions, bool debugPrint)
        {
            _parseOptions = spreadsheetParseOptions;

            // Resolve all members with names matching columns
            List<SpreadsheetCell> columnNames = headerRow;
            for (int colNdx = 0; colNdx < columnNames.Count; colNdx++)
            {
                // Only resolve columns surrounded with brackets ([])
                string colName = columnNames[colNdx].Value;
                if (colName.StartsWith("[", StringComparison.Ordinal) && colName.EndsWith("]", StringComparison.Ordinal))
                {
                    string     memberName = colName.Substring(1, colName.Length - 2);
                    MemberInfo memberInfo = typeof(TItem).GetMember(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault();

                    if (memberInfo != null)
                    {
                        Type memberType = GetMemberType(memberInfo);
                        if (memberType.IsCollection())
                        {
                            if (memberType.IsDictionary())
                            {
                                throw new ConfigParserException($"Dictionary types not yet supported", columnNames[colNdx]);
                            }
                            else
                            {
                                //DebugLog.Info("Matching collection column #{ColNdx} to {MemberName}", colNdx, memberInfo.Name);

                                // Ensure collection is registered
                                if (!_collections.TryGetValue(memberName, out CollectionInfo coll))
                                {
                                    coll = new CollectionInfo(memberInfo, memberType, memberType.GetCollectionElementType());
                                    _collections.Add(memberName, coll);
                                }

                                // Register column for collection
                                coll.ColumnIndexes.Add(colNdx);
                            }
                        }
                        else
                            throw new ConfigParserException($"Value collection column {colName} backing member {memberInfo.ToMemberWithGenericDeclaringTypeString()} is not a collection type!", columnNames[colNdx]);
                    }
                }
            }

            return new SpreadsheetParsePrepareFeedback(usedColumnIndexes: _collections.Values.SelectMany(collectionInfo => collectionInfo.ColumnIndexes));
        }

        public override void ParseItem(TItem item, List<SpreadsheetCell> cells, bool allowPartial = false)
        {
            foreach ((string memberName, CollectionInfo coll) in _collections)
            {
                Type memberType = coll.MemberType;
                Type elemType   = coll.ElementType;

                //DebugLog.Info("Parse collection:");
#if UNITY_EDITOR || NETCOREAPP
                if (!cachedInstanceCreators.ContainsKey(elemType))
                    cachedInstanceCreators[elemType] = Expression.Lambda<Func<IList>>(Expression.Convert(Expression.New(typeof(List<>).MakeGenericType(elemType)), typeof(IList))).Compile();

                IList list = cachedInstanceCreators[elemType].Invoke();
#else
                IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType));
#endif

                int skippedElements = 0;

                foreach ((int colNdx, int elemNdx) in coll.ColumnIndexes.ZipWithIndex())
                {
                    SpreadsheetCell cell = cells[colNdx];
                    string          str  = (colNdx < cells.Count) ? cell.Value : "";
                    try
                    {
                        //DebugLog.Info("  #{Ndx}: {Value}", colNdx, str);
                        if (str != "")
                        {
                            if (!string.IsNullOrWhiteSpace(_parseOptions.IgnoreCollectionElementValue) && str == _parseOptions.IgnoreCollectionElementValue)
                            {
                                skippedElements++;
                                continue;
                            }

                            if (!allowPartial)
                            {
                                // If there were any empty cells between this cell and the previous cell with a value,
                                // fill the interim empty cells with values parsed from an empty string.
                                // Except for columns that were purposefully ignored
                                while (list.Count + skippedElements < elemNdx)
                                    list.Add(CsvReader.ParseCell(elemType, ""));
                            }

                            object elem = CsvReader.ParseCell(elemType, str);
                            list.Add(elem);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new ConfigParserException("Failed to parse value", cell, ex);
                    }
                }

                // Create collection from parsed items
                // \todo [petri] refactor to method
                object value;
                if (memberType.IsArray)
                {
                    Array array = Array.CreateInstance(elemType, list.Count);
                    for (int ndx = 0; ndx < list.Count; ndx++)
                        array.SetValue(list[ndx], ndx);
                    value = array;
                }
                else if (memberType.IsGenericTypeOf(typeof(List<>)))
                    value = list;
                else
                    value = Activator.CreateInstance(memberType, list);

                SetMemberValue(coll.MemberInfo, item, value);
            }
        }
    }

    public class MetaScheduleParserPass<TItem> : SpreadsheetParserPass<TItem>
    {
        MemberInfo              _scheduleMemberInfo;
        int?                    _colNdxTimeMode;
        int?                    _colNdxStartDate;
        int?                    _colNdxStartTime;
        int?                    _colNdxDuration;
        int?                    _colNdxEndingSoon;
        int?                    _colNdxPreview;
        int?                    _colNdxReview;
        int?                    _colNdxRecurrence;
        int?                    _colNdxNumRepeats;
        SpreadsheetParseOptions _parseOptions;

        int?[] GetAllColumnIndexes() => new int?[]
        {
            _colNdxTimeMode,
            _colNdxStartDate,
            _colNdxStartTime,
            _colNdxDuration,
            _colNdxEndingSoon,
            _colNdxPreview,
            _colNdxReview,
            _colNdxRecurrence,
            _colNdxNumRepeats,
        };

        public override SpreadsheetParsePrepareFeedback Prepare(List<SpreadsheetCell> headerRow, SpreadsheetParseOptions spreadsheetParseOptions, bool debugPrint)
        {
            _parseOptions = spreadsheetParseOptions;

            // Find MemberInfo of MetaScheduleBase in target type (only allow one)
            _scheduleMemberInfo =
                typeof(TItem).GetMembers()
                    .Where(member => member is PropertyInfo || member is FieldInfo)
                    .Where(member => GetMemberType(member) == typeof(MetaScheduleBase))
                    .SingleOrDefault();

            // If has schedule member, resolve all schedule pseudo-columns (start with '#')
            if (_scheduleMemberInfo != null)
            {
                for (int colNdx = 0; colNdx < headerRow.Count; colNdx++)
                {
                    SpreadsheetCell cell    = headerRow[colNdx];
                    string          colName = cell.Value;
                    if (colName.StartsWith("#", StringComparison.Ordinal))
                    {
                        switch (colName)
                        {
                            case "#TimeMode":
                                _colNdxTimeMode = colNdx;
                                break;
                            case "#StartDate":
                                _colNdxStartDate = colNdx;
                                break;
                            case "#StartTime":
                                _colNdxStartTime = colNdx;
                                break;
                            case "#Duration":
                                _colNdxDuration = colNdx;
                                break;
                            case "#EndingSoon":
                                _colNdxEndingSoon = colNdx;
                                break;
                            case "#Preview":
                                _colNdxPreview = colNdx;
                                break;
                            case "#Review":
                                _colNdxReview = colNdx;
                                break;
                            case "#Recurrence":
                                _colNdxRecurrence = colNdx;
                                break;
                            case "#NumRepeats":
                                _colNdxNumRepeats = colNdx;
                                break;
                            default:
                                throw new ConfigParserException($"Unrecognized MetaScheduleBase column name {colName}", cell);
                        }
                    }
                }

                if (debugPrint)
                    DebugLog.Info(
                        "Matched MetaSchedule: #TimeMode={0}, #Recurrence={1}, #StartDay={2}, #StartTime={3}, #Duration={4}, #NumRepeats={5}",
                        _colNdxTimeMode,
                        _colNdxRecurrence,
                        _colNdxStartDate,
                        _colNdxStartTime,
                        _colNdxDuration,
                        _colNdxNumRepeats);
            }

            return new SpreadsheetParsePrepareFeedback(usedColumnIndexes: GetAllColumnIndexes().OfType<int>());
        }


        public override void ParseItem(TItem item, List<SpreadsheetCell> cells, bool allowPartial = false)
        {
            // If has schedule member, parse it
            if (_scheduleMemberInfo != null)
            {
                MetaScheduleBase schedule = null;
                try
                {
                    schedule = ParseRecurringScheduleOrNothing(cells);
                }
                catch (Exception ex)
                {
                    throw new ConfigParserException(
                        "Failed to parse MetaSchedule",
                        cells.Where((_, i) =>
                                GetAllColumnIndexes()
                                    .OfType<int>()
                                    .Contains(i))
                            .ToList(),
                        ex);
                }

                if (schedule != null)
                    SetMemberValue(_scheduleMemberInfo, item, schedule);
            }
        }

        /// <summary>
        /// Parse a recurring schedule definition, or return null if ALL the relevant cells are empty/omitted.
        /// </summary>
        MetaRecurringCalendarSchedule ParseRecurringScheduleOrNothing(List<SpreadsheetCell> cells)
        {
            if (GetAllColumnIndexes().All(colNdx => GetCellValueOrDefault(cells, colNdx, null) == null))
                return null;

            string startDate    = GetCellValueOrDefault(cells, _colNdxStartDate);
            string startTime    = GetCellValueOrDefault(cells, _colNdxStartTime, "00:00:00");   // default to start-of-day

            return new MetaRecurringCalendarSchedule(
                timeMode:   ParseTimeMode(GetCellValueOrDefault(cells, _colNdxTimeMode)),
                start:      ParseDateTime(startDate, startTime),
                duration:   ParseCalendarPeriod(GetCellValueOrDefault(cells, _colNdxDuration), allowEmpty: false),
                endingSoon: ParseCalendarPeriod(GetCellValueOrDefault(cells, _colNdxEndingSoon)),
                preview:    ParseCalendarPeriod(GetCellValueOrDefault(cells, _colNdxPreview)),
                review:     ParseCalendarPeriod(GetCellValueOrDefault(cells, _colNdxReview)),
                recurrence: ParseRecurrencePeriod(GetCellValueOrDefault(cells, _colNdxRecurrence, defaultValue: null)),
                numRepeats: ParseInteger(GetCellValueOrDefault(cells, _colNdxNumRepeats, "-1")));
        }

        MetaScheduleTimeMode ParseTimeMode(string str) =>
            EnumUtil.ParseCaseInsensitive<MetaScheduleTimeMode>(str);

        MetaCalendarDateTime ParseDateTime(string dateStr, string timeStr)
        {
            if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date))
                throw new InvalidOperationException($"Failed to parse Date '{dateStr}'");

            if (!DateTime.TryParseExact(timeStr, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time)
             && !DateTime.TryParseExact(timeStr, "H:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time))
                throw new InvalidOperationException($"Unable to parse Time '{timeStr}'");

            return new MetaCalendarDateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second);
        }

        MetaCalendarPeriod? ParseRecurrencePeriod(string str)
        {
            if (str == null)
                return null;
            else
                return ParseCalendarPeriod(str);
        }

        MetaCalendarPeriod ParseCalendarPeriod(string str, bool allowEmpty = true)
        {
            if (string.IsNullOrEmpty(str))
            {
                if (!allowEmpty)
                    throw new InvalidOperationException("Empty calendar period specified!");

                return new MetaCalendarPeriod();
            }
            else
                return MetaCalendarPeriod.ConfigParse(new ConfigLexer(str));
        }

        int ParseInteger(string str) =>
            int.Parse(str, CultureInfo.InvariantCulture);

        string GetCellValueOrDefault(List<SpreadsheetCell> cells, int? colNdxMaybe, string defaultValue = "")
        {
            if (colNdxMaybe == null)
                return defaultValue;

            int colNdx = colNdxMaybe.Value;
            if (colNdx >= cells.Count)
                return defaultValue;

            string value = cells[colNdx].Value;
            if (value == "")
                return defaultValue;

            return value;
        }
    }

    /// <summary>
    /// Specifies how unused columns are handled when parsing a spreadsheet.
    /// </summary>
    public enum UnusedSheetColumnHandling
    {
        Ignore,     // Unused columns (e.g. references to unknown config class members) are ignored.
        Error,      // Throw an error on encountering an unused column.
    }

    public class SpreadsheetParseOptions
    {
        public static readonly SpreadsheetParseOptions Default = new SpreadsheetParseOptions();

        public string                    IgnoreColumnNamePrefix       = null;                            // Columns with header name starting with this prefix are ignored.
        public string                    IgnoreCollectionElementValue = null;                            // Cells with value matching this are ignored.
        public UnusedSheetColumnHandling UnusedColumnHandling         = UnusedSheetColumnHandling.Error; // Specify how unused columns (other than those ignored by IgnoreColumnNamePrefix) are handled (ignore or throw).
    }

    public class SpreadsheetLibraryParser<TRef, TItem> where TItem : IGameConfigKey<TRef>, new()
    {
        public string                         SheetName  { get; }
        public List<SpreadsheetCell>          Header     { get; }
        public SpreadsheetParserPass<TItem>[] Passes     { get; }
        public SpreadsheetParseOptions        Options    { get; }
        public bool                           DebugPrint { get; }

        public int   AliasColumnIndex { get; }

        HashSet<int> _usedColumnIndexes = new HashSet<int>();

        public SpreadsheetLibraryParser(
            string sheetName,
            List<SpreadsheetCell> header,
            SpreadsheetParserPass<TItem>[] passes,
            SpreadsheetParseOptions options,
            bool debugPrint = true)
        {
            SheetName  = sheetName;
            Header     = header;
            Passes     = passes;
            Options    = options;
            DebugPrint = debugPrint;

            AliasColumnIndex = Header.FindIndex(x => x.Value == GameConfigHelper.AliasColumnName);

            // usedColumnIndexes keeps track of which columns are getting used by parser passes or special handling,
            // so that we can give errors about unused columns (which may be a sign of e.g. typo'd member name).
            if (AliasColumnIndex >= 0)
                _usedColumnIndexes.Add(AliasColumnIndex);

            // Prepare all passes (parse headers).
            foreach (SpreadsheetParserPass<TItem> pass in Passes)
            {
                SpreadsheetParsePrepareFeedback feedback = pass.Prepare(Header, Options, DebugPrint);
                _usedColumnIndexes.UnionWith(feedback.UsedColumnIndexes);
            }
        }

        public List<VariantConfigItem<TRef, TItem>> ParseRows(
            List<GameConfigHelper.RowWithVariantAssociation> rows,
            bool allowPartial = false,
            bool validateVariant = false)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));

            try
            {
                bool isConfigKeyValueType = typeof(TRef).IsValueType;

                // Check column usage and ignoration.
                foreach ((SpreadsheetCell column, int columnNdx) in Header.ZipWithIndex())
                {
                    string columnName = column.Value;
                    bool   isUsed     = _usedColumnIndexes.Contains(columnNdx);
                    bool isIgnored = Options.IgnoreColumnNamePrefix != null
                        && columnName.StartsWith(Options.IgnoreColumnNamePrefix, StringComparison.Ordinal);

                    if (isUsed && isIgnored)
                        throw new ConfigParserException(
                            $"Sheet {SheetName}: column '{columnName}' (index {columnNdx}) in sheet is being used, but is supposed to be ignored due to prefix '{Options.IgnoreColumnNamePrefix}'",
                            column);

                    if (!isUsed && !isIgnored)
                    {
                        // Tolerate fully-empty columns (including header row) not being used, but also don't forbid them from being used (in case used by some special parser pass)
                        bool isEmptyColumn =
                            Enumerable.Range(0, rows.Count)
                                .Select(rowNdx => rows[rowNdx]?.Row?[columnNdx].Value)
                                .All(string.IsNullOrEmpty);

                        if (!isEmptyColumn)
                        {
                            if (Options.UnusedColumnHandling == UnusedSheetColumnHandling.Error)
                            {
                                if (columnName != "")
                                {
                                    string commonMessage = Invariant($"Sheet {SheetName}: column '{columnName}' (index {columnNdx}) in sheet is not empty, yet is not being used by any parser pass. This may be happening because the column does not match to any member in {typeof(TItem).Name}.");

                                    string ignoreHint;
                                    if (Options.IgnoreColumnNamePrefix != null)
                                        ignoreHint = $"Hint: You can explicitly ignore a column by prefixing its name with the configured {nameof(SpreadsheetParseOptions.IgnoreColumnNamePrefix)}, like so: {Options.IgnoreColumnNamePrefix}{columnName}";
                                    else
                                        ignoreHint = $"Hint: To explicitly ignore columns, override the SpreadsheetParseOptions property in your GameConfigBuild class and specify {nameof(SpreadsheetParseOptions.IgnoreColumnNamePrefix)}.";

                                    throw new ConfigParserException($"{commonMessage} {ignoreHint}", column);
                                }
                                else
                                    throw new ConfigParserException($"Sheet {SheetName}: unnamed column at index {columnNdx} in sheet is not empty, yet is not being used by any parser pass.", column);
                            }
                        }
                    }
                }

                // Parse all items one-by-one with each of the passes
                List<VariantConfigItem<TRef, TItem>> items = new List<VariantConfigItem<TRef, TItem>>();
                foreach (GameConfigHelper.RowWithVariantAssociation row in rows)
                {
                    try
                    {
                        TItem baseItem    = ParseRow(row.Row, Passes, allowPartial);
                        if (validateVariant && !string.IsNullOrWhiteSpace(row.VariantKey) && isConfigKeyValueType)
                        {
                            var copiedRow = new GameConfigHelper.RowWithVariantAssociation(row.Row.ToList(), row.VariantKey, row.Baseline);
                            copiedRow.ExpandFromBaseline(AliasColumnIndex);
                            TItem copiedItem = ParseRow(copiedRow.Row, Passes, allowPartial);

                            if (!Equals(copiedItem.ConfigKey, baseItem.ConfigKey))
                                throw new ConfigParserException("Variant patches with value typed config keys are only supported when using the patch by config key feature.", row);
                        }

                        string     variantIdMaybe = row.VariantKey;
                        List<TRef> aliases        = null;
                        if (AliasColumnIndex >= 0 && AliasColumnIndex < row.Row.Count && row.Row[AliasColumnIndex].Value != "")
                            aliases = (List<TRef>)CsvReader.ParseCell(typeof(List<TRef>), row.Row[AliasColumnIndex].Value);

                        if (!allowPartial && baseItem.ConfigKey == null)
                            throw new ConfigParserException("Found config item without a valid ConfigKey value", row);

                        items.Add(new VariantConfigItem<TRef, TItem>(baseItem, variantIdMaybe, aliases, row));
                    }
                    catch (ConfigParserException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new ConfigParserException("Error while parsing spreadsheet", row, ex);
                    }
                }

                return items;
            }
            catch (ConfigParserException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error while parsing spreadsheet {SheetName}: {ex.Message}", ex);
            }
        }

        TItem ParseRow(List<SpreadsheetCell> row, SpreadsheetParserPass<TItem>[] passes, bool allowPartial = false)
        {
            TItem item = new TItem();

            try
            {
                // Parse with all configured passes
                foreach (SpreadsheetParserPass<TItem> pass in passes)
                    pass.ParseItem(item, row, allowPartial);
            }
            catch (ConfigParserException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ConfigParserException($"Failed to parse row", row, ex);
            }

            return item;
        }
    }

    public static class SpreadsheetKeyValueStructureParser<TStructure>
    {
        struct RowReader
        {
            List<SpreadsheetCell> _row;
            int _index;

            public RowReader(List<SpreadsheetCell> row)
            {
                _row = row;
                _index = 0;
            }

            // Transforms missing and empty values to `valueIfMissing`
            public string Next(string valueIfMissing)
            {
                if (_index >= _row.Count)
                    return valueIfMissing;
                string val = _row[_index++].Value;
                return string.IsNullOrEmpty(val) ? valueIfMissing : val;
            }
        }

        public static List<VariantConfigStructureMember> ParseSheetWithExpandedVariants(string sheetName, List<GameConfigHelper.RowWithVariantAssociation> rows)
        {
            if (sheetName == null)
                throw new ArgumentNullException(nameof(sheetName));
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));

            int variantIdColumnNdx = rows[0].Row.FindIndex(x => x.Value.Equals(GameConfigHelper.VariantColumnName, StringComparison.InvariantCultureIgnoreCase));
            if (variantIdColumnNdx >= 0 && variantIdColumnNdx != 0)
                throw new ConfigParserException($"In key-value config structures, '{GameConfigHelper.VariantColumnName}' (if present) must be in the first column", rows[0].Row[variantIdColumnNdx]);

            bool hasVariants = rows.Any(x => x.VariantKey != null);

            int numHeaderRows;
            if (hasVariants)
            {
                foreach (SpreadsheetCell cell in rows[0].Row.Skip(1))
                {
                    if (!string.IsNullOrEmpty(cell.Value))
                        throw new ConfigParserException($"In key-value config structures, '{GameConfigHelper.VariantColumnName}' (if present) must be the only nonempty cell in the first row (i.e. the header)", cell);
                }

                numHeaderRows = 1;
            }
            else
                numHeaderRows = 0;

            List<VariantConfigStructureMember> members = new List<VariantConfigStructureMember>();

            foreach (GameConfigHelper.RowWithVariantAssociation rowData in rows.Skip(numHeaderRows))
            {
                RowReader row = new RowReader(rowData.Row);

                // Order of columns in key-value configs is fixed:
                // (Variant Id |) key | value
                string variantIdMaybe = rowData.VariantKey;
                string memberName     = row.Next(null);
                string memberValueStr = row.Next(String.Empty);

                if (memberName == null)
                    throw new ConfigParserException($"Failed to parse {sheetName}: empty member name specified.", rowData);

                MemberInfo  memberInfo      = (MemberInfo)typeof(TStructure).GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                              ?? (MemberInfo)typeof(TStructure).GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (memberInfo == null)
                    throw new ConfigParserException($"Failed to parse {sheetName}: no such data member {typeof(TStructure).Name}.{memberName}.", rowData);


                object memberValue;
                try
                {
                    memberValue = CsvReader.ParseCell(memberInfo.GetDataMemberType(), memberValueStr);
                }
                catch (Exception ex)
                {
                    throw new ConfigParserException($"Failed to parse {sheetName}: {typeof(TStructure).Name}.{memberName} value '{memberValueStr}'", rowData, ex);
                }

                ConfigStructureMember member = new ConfigStructureMember(memberInfo, memberValue);
                members.Add(new VariantConfigStructureMember(member, variantIdMaybe, rowData));
            }

            return members;
        }
    }

    // Only used by legacy pipeline for forward-compatibility
    internal static class DummyGameConfigSourceInfo
    {
        public static readonly SpreadsheetFileSourceInfo Instance = new SpreadsheetFileSourceInfo("DummySource");
    }

#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE

    /// <summary>
    /// Represents a single config item along with info about which variant it belongs to (if any).
    /// </summary>
    public struct VariantConfigItem<TRef, TItem> // \todo where TItem : IGameConfigKey (but we don't have non-generic IGameConfigKey yet)
    {
        public readonly TItem                       Item;
        public readonly string                      VariantIdMaybe;
        public readonly List<TRef>                  Aliases;
        public readonly GameConfigSourceLocation    SourceLocation;

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public readonly GameConfigHelper.RowWithVariantAssociation SourceRow;

        public VariantConfigItem(TItem item, string variantIdMaybe, List<TRef> aliases, GameConfigHelper.RowWithVariantAssociation sourceRow)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            Item = item;
            VariantIdMaybe = variantIdMaybe;
            Aliases = aliases;
            SourceRow = sourceRow;

            int row = sourceRow.Row.FirstOrDefault(c => c.HasValues()).Row;
            SourceLocation = GameConfigSpreadsheetLocation.FromRows(DummyGameConfigSourceInfo.Instance, row, row + 1);
        }
#endif

        public VariantConfigItem(TItem item, string variantIdMaybe, List<TRef> aliases, GameConfigSourceLocation sourceLocation)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            Item           = item;
            VariantIdMaybe = variantIdMaybe;
            Aliases        = aliases;
            SourceLocation = sourceLocation;

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
            SourceRow = default;
#endif
        }
    }

    /// <summary>
    /// Represents a single key-value structure config member along with info about which variant it belongs to (if any).
    /// </summary>
    public struct VariantConfigStructureMember
    {
        public readonly ConfigStructureMember       Member;
        public readonly string                      VariantIdMaybe;
        public readonly GameConfigSourceLocation    SourceLocation;

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public readonly GameConfigHelper.RowWithVariantAssociation SourceRow;

        public VariantConfigStructureMember(ConfigStructureMember member, string variantIdMaybe, GameConfigHelper.RowWithVariantAssociation sourceRow)
        {
            Member = member;
            VariantIdMaybe = variantIdMaybe;
            SourceRow = sourceRow;

            int row = sourceRow.Row.FirstOrDefault(c => c.HasValues()).Row;
            SourceLocation = GameConfigSpreadsheetLocation.FromRows(DummyGameConfigSourceInfo.Instance, row, row + 1);
        }
#endif

        public VariantConfigStructureMember(ConfigStructureMember member, string variantIdMaybe, GameConfigSourceLocation sourceLocation)
        {
            Member = member;
            VariantIdMaybe = variantIdMaybe;
            SourceLocation = sourceLocation;

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
            SourceRow = default;
#endif
        }
    }

    /// <summary>
    /// Represents a single key-value structure config member.
    /// </summary>
    public struct ConfigStructureMember
    {
        public readonly MemberInfo  MemberInfo;
        public readonly object      MemberValue;

        public ConfigStructureMember(MemberInfo memberInfo, object memberValue)
        {
            MemberInfo = memberInfo ?? throw new ArgumentNullException(nameof(memberInfo));
            MemberValue = memberValue;
        }
    }
}
