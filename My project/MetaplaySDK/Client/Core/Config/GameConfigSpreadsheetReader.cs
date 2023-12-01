// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Metaplay.Core.Config.GameConfigSyntaxTree;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Converts from <see cref="SpreadsheetContent"/> to <see cref="GameConfigSyntaxTree"/>.
    /// </summary>
    public static class GameConfigSpreadsheetReader
    {
        public enum PathSegmentType
        {
            Root = 0,
            Member,
            VerticalCollection,
            IndexedElement,
        }

        public readonly struct PathSegment : IEquatable<PathSegment>
        {
            public readonly string          Name;
            public readonly string          VariantId;
            public readonly PathSegmentType Type;
            public readonly int?            ElementIndex;

            public NodeMemberId SegmentId => new NodeMemberId(Name, VariantId);

            public static readonly PathSegment Root = new PathSegment(name: "$root", variantId: null, PathSegmentType.Root, elementIndex: null);

            public PathSegment(string name, string variantId, PathSegmentType type, int? elementIndex)
            {
                bool needArrayElement = (type == PathSegmentType.IndexedElement);
                if (needArrayElement && !elementIndex.HasValue)
                    throw new ArgumentException($"PathSegmentType {type} must specify an elementIndex");
                if (!needArrayElement && elementIndex.HasValue)
                    throw new ArgumentException($"PathSegmentType {type} must not specify an elementIndex");

                if (elementIndex.HasValue)
                {
                    if (elementIndex.Value < 0)
                        throw new ArgumentException($"Element indices must be positive, got {elementIndex.Value}");
                }

                Name = name ?? throw new ArgumentNullException(nameof(name));
                VariantId = variantId;
                Type = type;
                ElementIndex = elementIndex;
            }

            public override string ToString()
            {
                switch (Type)
                {
                    case PathSegmentType.Root: return SegmentId.ToString();
                    case PathSegmentType.Member: return SegmentId.ToString();
                    case PathSegmentType.VerticalCollection: return $"{SegmentId}[]";
                    case PathSegmentType.IndexedElement: return Invariant($"{SegmentId}[{ElementIndex}]");
                    default:
                        throw new InvalidOperationException($"Invalid PathElementType {Type}");
                }
            }

            public override bool Equals(object obj) => obj is PathSegment segment && Equals(segment);

            public bool Equals(PathSegment other)
            {
                return Name == other.Name &&
                       VariantId == other.VariantId &&
                       Type == other.Type &&
                       ElementIndex == other.ElementIndex;
            }

            public override int GetHashCode() => Util.CombineHashCode(Name?.GetHashCode() ?? 0, VariantId?.GetHashCode() ?? 0, Type.GetHashCode(), ElementIndex?.GetHashCode() ?? 0);

            public static bool operator ==(PathSegment left, PathSegment right) => left.Equals(right);
            public static bool operator !=(PathSegment left, PathSegment right) => !(left == right);
        }

        public class ColumnInfo
        {
            public readonly int             ColumnIndex;
            public readonly string          FullPath;
            public readonly PathSegment[]   PathSegments;
            public readonly NodeTag[]       Tags;

            public bool HasEmptyHeader => string.IsNullOrEmpty(FullPath);

            public ColumnInfo(int columnIndex, string fullPath, PathSegment[] pathSegments, NodeTag[] tags)
            {
                ColumnIndex     = columnIndex;
                FullPath        = fullPath;
                PathSegments    = pathSegments;
                Tags            = tags;
            }

            public bool HasTagWithName(string tagName) => Tags.Any(tag => tag.Name == tagName);

            public bool IsKeyColumn => HasTagWithName(BuiltinTags.Key); // \note Ignores tag values -- may want to generalize to multiple identities in the future

            public override string ToString() => Invariant($"ColumnInfo(columnIndex={ColumnIndex}, path=[{string.Join(", ", PathSegments)}], tags=[{string.Join<NodeTag>(", ", Tags)}])");
        }

        public abstract class PathNode
        {
            public readonly string Name;

            protected PathNode(string name) => Name = name;

            internal abstract void ToStringBuilder(StringBuilder sb, int depth);

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                ToStringBuilder(sb, 0);
                return sb.ToString();
            }
        }

        public class PathNodeScalar : PathNode
        {
            public readonly ColumnInfo ColumnInfo;

            public PathNodeScalar(string name, ColumnInfo columnInfo) : base(name) =>
                ColumnInfo = columnInfo;

            internal override void ToStringBuilder(StringBuilder sb, int depth)
            {
                string indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{Name} : scalar");
            }
        }

        public class PathNodeCollection : PathNode
        {
            // \note Allows multiple representations statically, only one can be used for each item
            public readonly ColumnInfo      ScalarColumn;
            public readonly ColumnInfo[]    VerticalColumns;
            public readonly ColumnInfo[]    IndexedColumns;
            public readonly PathNode[]      IndexedNodes;

            public PathNodeCollection(string name, ColumnInfo scalarColumn, ColumnInfo[] verticalColumns, ColumnInfo[] indexedColumns, PathNode[] indexedNodes) : base(name)
            {
                ScalarColumn    = scalarColumn;
                VerticalColumns = verticalColumns;
                IndexedColumns  = indexedColumns;
                IndexedNodes    = indexedNodes;
            }

            internal override void ToStringBuilder(StringBuilder sb, int depth)
            {
                string indent = new string(' ', depth * 2);
                bool hasScalar = ScalarColumn != null;
                bool hasVertical = VerticalColumns != null;
                bool hasIndexed = IndexedColumns != null;
                string[] representations = new string[] { hasScalar ? "scalar" : null, hasVertical ? "vertical" : null, hasIndexed ? "indexed" : null };
                string representationsStr = string.Join(",", representations.Where(rep => rep != null));
                sb.AppendLine($"{indent}{Name} : collection<{representationsStr}>");

                if (hasScalar)
                    sb.AppendLine($"{indent}  {ScalarColumn.FullPath}");

                if (hasVertical)
                {
                    foreach (ColumnInfo columnInfo in VerticalColumns)
                        sb.AppendLine($"{indent}  {columnInfo.FullPath}");
                }

                if (hasIndexed)
                {
                    foreach (PathNode element in IndexedNodes)
                    {
                        if (element != null)
                            element.ToStringBuilder(sb, depth + 1);
                        else
                            sb.AppendLine($"{indent}  null");
                    }
                }
            }
        }

        public class PathNodeObject : PathNode
        {
            public OrderedDictionary<NodeMemberId, PathNode> Children;

            public PathNodeObject(string name, OrderedDictionary<NodeMemberId, PathNode> children) : base(name) => Children = children;

            internal override void ToStringBuilder(StringBuilder sb, int depth)
            {
                string indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{Name} : object");

                foreach (PathNode child in Children.Values)
                    child.ToStringBuilder(sb, depth + 1);
            }
        }

        static ColumnInfo ParseHeaderCell(int columnIndex, SpreadsheetCell cell)
        {
            if (string.IsNullOrEmpty(cell.Value))
                throw new ParseError("Empty header cell!");

            // Special handling of some special columns (/Variant, /Aliases)
            if (cell.Value == GameConfigSyntaxTreeUtil.VariantIdKey || cell.Value == GameConfigSyntaxTreeUtil.AliasesKey)
                return new ColumnInfo(columnIndex, cell.Value, new PathSegment[] { new PathSegment(cell.Value, variantId: null, PathSegmentType.Member, elementIndex: null) }, tags: Array.Empty<NodeTag>());

            // Handle comment columns (ignored during parsing)
            // \note Handling this with a raw StartsWith instead of ConfigLexer, because we want to
            //       allow arbitrary stuff after the slashes, whereas ConfigLexer throws if it
            //       sees invalid tokens immediately after the slashes.
            // \todo Allow spaces before the slashes?
            if (cell.Value.StartsWith("//", StringComparison.Ordinal)) // \todo Only handles comments at the start of cell
            {
                string comment = cell.Value.Substring(2);
                return new ColumnInfo(columnIndex, cell.Value, pathSegments: null, tags: new NodeTag[] { new NodeTag(BuiltinTags.Comment, comment) });
            }

            ConfigLexer lexer = new ConfigLexer(cell.Value);

            List<PathSegment> path = new List<PathSegment>();

            // Pseudo-columns start with a tag (and don't have a name).
            if (lexer.CurrentToken.Type != ConfigLexer.TokenType.Hash)
            {
                // Parse the path by element (dot-separated sequence of elements)
                for (; ; )
                {
                    string name = lexer.ParseIdentifier();
                    if (lexer.TryParseToken(ConfigLexer.TokenType.LeftBracket))
                    {
                        if (lexer.CurrentToken.Type == ConfigLexer.TokenType.IntegerLiteral)
                        {
                            int arrayIndex = lexer.ParseIntegerLiteral();
                            lexer.ParseToken(ConfigLexer.TokenType.RightBracket);
                            path.Add(new PathSegment(name, variantId: null, PathSegmentType.IndexedElement, elementIndex: arrayIndex));
                        }
                        else if (lexer.TryParseToken(ConfigLexer.TokenType.RightBracket))
                        {
                            path.Add(new PathSegment(name, variantId: null, PathSegmentType.VerticalCollection, elementIndex: null));
                        }
                        else
                            throw new ParseError($"Invalid token {lexer.CurrentToken.Type} ({lexer.GetTokenString(lexer.CurrentToken)}), expecting an integer or ']'");
                    }
                    else // simple member
                    {
                        path.Add(new PathSegment(name, variantId: null, PathSegmentType.Member, elementIndex: null));
                    }

                    // Check if path continues or not
                    if (lexer.TryParseToken(ConfigLexer.TokenType.Dot))
                        continue;
                    else if (lexer.IsAtEnd || lexer.CurrentToken.Type == ConfigLexer.TokenType.Hash)
                        break;
                    else
                        throw new ParseError($"Invalid token in header: {lexer.CurrentToken.Type} ({lexer.GetTokenString(lexer.CurrentToken)})");
                }
            }

            // Parse all hash tags for the
            List<NodeTag> tags = new List<NodeTag>();
            while (lexer.TryParseToken(ConfigLexer.TokenType.Hash))
            {
                // Parse tag identity
                string tagName = lexer.ParseIdentifier();
                string tagValue = null;

                // Parse tag value, if has one (indicated by a colon ':')
                if (lexer.TryParseToken(ConfigLexer.TokenType.Colon))
                {
                    // \todo [petri] currently assumes tag value is a single token -- use a more flexible custom rule?
                    tagValue = lexer.GetTokenString(lexer.CurrentToken);
                    lexer.Advance();
                }

                // \todo Allow unknown tags as well (to support userland customizations)?
                if (!BuiltinTags.All.Contains(tagName))
                    throw new ParseError($"Invalid tag '{tagName}' used on header '{cell.Value}'. Expected one of: {string.Join(", ", BuiltinTags.All)}");
                tags.Add(new NodeTag(tagName, tagValue));
            }

            // \todo Check for conflicting tags (eg, multiple of the same, or id+comment).

            // Must have consumed all input
            if (!lexer.IsAtEnd)
                throw new ParseError($"Failed to parse column header '{cell.Value}', got unexpected token '{lexer.CurrentToken}'.");

            // Check that #comments have an empty node path, and non-comments have non-empty node path.
            bool isComment = tags.Any(tag => tag.Name == BuiltinTags.Comment);
            if (isComment && path.Count != 0)
                throw new ParseError($"Failed to parse column header '{cell.Value}', got a non-empty node path for a #comment. The node path should be empty for comments.");
            else if (!isComment && path.Count == 0)
                throw new ParseError($"Failed to parse column header '{cell.Value}', got an empty node path.");

            return new ColumnInfo(columnIndex, cell.Value, path.ToArray(), tags.ToArray());
        }

        static ColumnInfo CreateVariantOverrideColumnInfo(int columnIndex, ColumnInfo baselineColumn, string variantId)
        {
            // Variant override column has the same path as the baseline column, except that
            // the last path segment specifies the variant id.
            PathSegment[] pathSegments = baselineColumn.PathSegments.ToArray(); // \note Copy with ToArray()
            int lastSegmentNdx = pathSegments.Length - 1;
            pathSegments[lastSegmentNdx] = CreateVariantPathSegment(pathSegments[lastSegmentNdx], variantId);

            string fullPath = $"{baselineColumn.FullPath} (variant {variantId})";
            NodeTag[] tags = baselineColumn.Tags.ToArray(); // \note Defensive copy...
            return new ColumnInfo(columnIndex, fullPath, pathSegments, tags);
        }

        public static PathSegment CreateVariantPathSegment(PathSegment baseline, string variantId)
        {
            return new PathSegment(name: baseline.Name, variantId: variantId, baseline.Type, baseline.ElementIndex);
        }

        static ColumnInfo[] ParseHeaderRow(GameConfigBuildLog buildLog, List<SpreadsheetCell> headerRow)
        {
            // Parse the header cells
            List<ColumnInfo> columns = new List<ColumnInfo>();
            ColumnInfo lastNonVariantColumn = null;
            for (int colNdx = 0; colNdx < headerRow.Count; colNdx++)
            {
                SpreadsheetCell cell = headerRow[colNdx];
                GameConfigSourceLocation location = GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell);

                if (string.IsNullOrEmpty(cell.Value))
                {
                    // Empty header. This will only be used for checking that the column is fully empty.
                    columns.Add(new ColumnInfo(colNdx, fullPath: "", Array.Empty<PathSegment>(), Array.Empty<NodeTag>()));
                }
                else if (cell.Value.StartsWith("/:", StringComparison.Ordinal))
                {
                    // Variant column. Specifies a variation of the previous non-variant column.

                    if (lastNonVariantColumn == null)
                        buildLog.WithLocation(location).Error(message: "Found variant override column but there is no corresponding normal column to its left");
                    else
                    {
                        IEnumerable<string> variantIds = cell.Value.Substring(2).Split(',').Select(x => x.Trim()).Where(x => x != string.Empty);
                        if (!variantIds.Any())
                            buildLog.WithLocation(location).Error("Malformed column override header");
                        else
                        {
                            foreach (string variantId in variantIds)
                                columns.Add(CreateVariantOverrideColumnInfo(colNdx, lastNonVariantColumn, variantId));
                        }
                    }
                }
                else
                {
                    // Column with a path.

                    try
                    {
                        ColumnInfo column = ParseHeaderCell(colNdx, cell);
                        columns.Add(column);
                        lastNonVariantColumn = column;
                    }
                    catch (ParseError ex)
                    {
                        buildLog.WithLocation(location).Error(message: $"Failed to parse column header '{cell.Value}': {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        buildLog.WithLocation(location).Error(message: $"Internal error while parsing column header '{cell.Value}'", ex);
                    }
                }
            }
            return columns.ToArray();
        }

        static void CheckNoMissingHeaderInNonemptyColumns(GameConfigBuildLog buildLog, ColumnInfo[] columns, List<List<SpreadsheetCell>> contentRows)
        {
            foreach (ColumnInfo column in columns)
            {
                if (!column.HasEmptyHeader)
                    continue;

                int colNdx = column.ColumnIndex;
                List<SpreadsheetCell> firstRowWithContent = contentRows.FirstOrDefault(row => !string.IsNullOrEmpty(row[colNdx].Value));

                if (firstRowWithContent != null)
                {
                    GameConfigSourceLocation columnLocation = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, colNdx, colNdx+1);
                    GameConfigSourceLocation nonemptyCellLocation = GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, firstRowWithContent[colNdx]);
                    buildLog.WithLocation(columnLocation).Error($"This column contains nonempty cells, but its header cell is empty, which is not supported. If this column is meant to be ignored, put two slashes (//) in the header cell. Nonempty content exists at: {nonemptyCellLocation.SourceInfo.GetLocationUrl(nonemptyCellLocation)}");
                }
            }
        }

        static PathNode[] CreateIndexedCollectionElementNodes(GameConfigBuildLog buildLog, PathSegment pathSegment, ColumnInfo[] columns, int depth)
        {
            // Group columns by their element index
            OrderedDictionary<int, ColumnInfo[]> byElemIndex =
                columns
                .GroupBy(col => col.PathSegments[depth].ElementIndex.Value)
                .ToOrderedDictionary(g => g.Key, g => g.ToArray());

            // Size of collection (maximum index + 1), can be sparse
            int collectionSize = byElemIndex.Keys.Max() + 1;

            // Iterate over all elements & create nodes for them
            List<PathNode> elementNodes = new List<PathNode>();
            for (int elemNdx = 0; elemNdx < collectionSize; elemNdx++)
            {
                if (byElemIndex.TryGetValue(elemNdx, out ColumnInfo[] elemColumns))
                {
                    bool isComplexElement = elemColumns.Length > 1 || elemColumns[0].PathSegments.Length > depth + 1;
                    if (isComplexElement)
                    {
                        // \todo [petri] is this correct? name should have elemNdx?
                        string childName = Invariant($"{pathSegment.SegmentId}[{elemNdx}]");
                        PathNodeObject childNode = CreateObjectNode(buildLog, childName, elemColumns, depth + 1);
                        elementNodes.Add(childNode);
                    }
                    else
                    {
                        string childName = Invariant($"[{elemNdx}]");
                        PathNode childNode = new PathNodeScalar(childName, elemColumns[0]);
                        elementNodes.Add(childNode);
                    }
                }
                else
                {
                    // Element not specified
                    elementNodes.Add(null);
                }
            }

            return elementNodes.ToArray();
        }

        static PathNodeObject CreateObjectNode(GameConfigBuildLog buildLog, string nodeName, ColumnInfo[] columns, int depth)
        {
            // If `depth` is out of bounds of any of the column paths, it means there were duplicate or conflicting columns.
            // For example, duplicate: multiple occurrences of `Foo.Bar`; or conflicting: both `Foo` and `Foo.Bar` .
            if (columns.Any(col => col.PathSegments.Length <= depth))
            {
                if (columns.Length == 1)
                {
                    // \note This is an error case that should never happen. Could use a better error message but this is an SDK internal error.
                    throw new MetaAssertException($"Encountered too short column but there is only 1 column, which should never happen.");
                }

                ColumnInfo firstTooShortColumn = columns.First(col => col.PathSegments.Length <= depth);
                GameConfigSourceLocation firstLocation = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, firstTooShortColumn.ColumnIndex, firstTooShortColumn.ColumnIndex + 1);

                foreach (ColumnInfo columnInfo in columns)
                {
                    if (ReferenceEquals(columnInfo, firstTooShortColumn))
                        continue;

                    GameConfigSourceLocation location = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, columnInfo.ColumnIndex, columnInfo.ColumnIndex + 1);

                    if (columnInfo.FullPath == firstTooShortColumn.FullPath)
                        buildLog.WithLocation(location).Error($"Duplicate header cell for '{columnInfo.FullPath}'. First occurrence is at {firstLocation.SourceInfo.GetLocationUrl(firstLocation)} .");
                    else
                        buildLog.WithLocation(location).Error($"Conflicting header cells '{firstTooShortColumn.FullPath}' and '{columnInfo.FullPath}'. The former occurs at {firstLocation.SourceInfo.GetLocationUrl(firstLocation)} . The same value cannot be both a scalar and a compound object.");
                }

                return new PathNodeObject(nodeName, new OrderedDictionary<NodeMemberId, PathNode>());
            }

            OrderedDictionary<NodeMemberId, PathNode> childNodes = new OrderedDictionary<NodeMemberId, PathNode>();

            // Group children by the name of the next path segment (arrays and indexing are ignored)
            foreach (IGrouping<NodeMemberId, ColumnInfo> group in columns.GroupBy(columnInfo => columnInfo.PathSegments[depth].SegmentId))
            {
                NodeMemberId    childId         = group.Key;
                ColumnInfo[]    childColumns    = group.ToArray();

                PathSegmentType[] uniqueSegmentTypes = childColumns.Select(col => col.PathSegments[depth].Type).Distinct().ToArray();
                bool isCollection = uniqueSegmentTypes.Contains(PathSegmentType.IndexedElement) || uniqueSegmentTypes.Contains(PathSegmentType.VerticalCollection);
                if (isCollection)
                {
                    // Handle collections: can have multiple representations (only one is allowed for each item)
                    ColumnInfo[] scalarColumns   = childColumns.Where(col => col.PathSegments[depth].Type == PathSegmentType.Member).ToArray();
                    ColumnInfo   scalarColumn    = (scalarColumns.Length >= 1) ? scalarColumns[0] : null;
                    ColumnInfo[] verticalColumns = childColumns.Where(col => col.PathSegments[depth].Type == PathSegmentType.VerticalCollection).ToArray();
                    ColumnInfo[] indexedColumns  = childColumns.Where(col => col.PathSegments[depth].Type == PathSegmentType.IndexedElement).ToArray();

                    // Can only have one scalar column
                    if (scalarColumns.Length > 1)
                    {
                        ColumnInfo dupeColumn = scalarColumns[1];
                        GameConfigSpreadsheetLocation columnLocation = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, dupeColumn.ColumnIndex, dupeColumn.ColumnIndex + 1);
                        buildLog.WithLocation(columnLocation).Error($"Duplicate columns specifying collection '{dupeColumn.FullPath}'");
                        continue;
                    }

                    // Convert arrays to nulls, if empty
                    if (!verticalColumns.Any())
                        verticalColumns = null;
                    if (!indexedColumns.Any())
                        indexedColumns = null;

                    // Check duplicates and other error cases in vertical columns.
                    // \todo This should ideally be handled by the same check that already exists
                    //       at the top of CreateObjectNode. Maybe that will happen when this gets refactored
                    //       to support arbitrarily nested vertical collections.
                    if (verticalColumns != null)
                    {
                        CheckErrorsInVerticalCollectionColumns(buildLog, verticalColumns, depth, out bool hasErrors);
                        if (hasErrors)
                            continue;
                    }

                    // Convert indexed columns into a hierarchical representation (if specified)
                    PathNode[] indexedNodes = null;
                    if (indexedColumns != null)
                    {
                        PathSegment childSegment = indexedColumns[0].PathSegments[depth];
                        indexedNodes = CreateIndexedCollectionElementNodes(buildLog, childSegment, indexedColumns, depth);
                    }

                    PathNodeCollection collection = new PathNodeCollection($"{childId}[]", scalarColumn, verticalColumns, indexedColumns, indexedNodes);
                    childNodes.Add(childId, collection);
                }
                else
                {
                    // Handle non-collections (objects and scalars)
                    // \todo Allow multi-representation for objects as well?
                    if (uniqueSegmentTypes.Length != 1)
                        throw new InvalidOperationException($"Multiple conflicting segment types for non-collection: {string.Join(", ", uniqueSegmentTypes)}");

                    PathSegment childSegment = childColumns[0].PathSegments[depth];
                    bool isNested = childColumns.Length > 1 || childColumns[0].PathSegments.Length > depth + 1;
                    if (isNested)
                    {
                        PathNode childNode = CreateObjectNode(buildLog, childSegment.SegmentId.ToString(), childColumns, depth + 1);
                        childNodes.Add(childId, childNode);
                    }
                    else // scalar element
                    {
                        ColumnInfo childColumn = childColumns[0];
                        childNodes.Add(childId, new PathNodeScalar(childId.ToString(), childColumn));
                    }
                }
            }

            return new PathNodeObject(nodeName, childNodes);
        }

        static void CheckErrorsInVerticalCollectionColumns(GameConfigBuildLog buildLog, ColumnInfo[] verticalColumns, int depth, out bool hasErrors)
        {
            hasErrors = false;

            // Check no deeply-nested, e.g. `Array[].Foo.Bar` .
            // Deeply-nested objects as elements are not yet supported (though are intended to be in the future).
            // On level of nesting within the array is supported, e.g. `Array[].Foo` .
            foreach (ColumnInfo col in verticalColumns)
            {
                if (col.PathSegments.Length - depth > 2)
                {
                    GameConfigSpreadsheetLocation location = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, col.ColumnIndex, col.ColumnIndex + 1);
                    buildLog.WithLocation(location).Error("At most 1 level of object nesting inside a vertical collection is currently supported.");
                    hasErrors = true;
                }
            }

            if (hasErrors)
                return;

            bool hasScalars = verticalColumns.Any(col => col.PathSegments.Length == depth + 1);
            if (hasScalars)
            {
                // Contains scalar element columns. Therefore must contain only 1 column.

                if (verticalColumns.Length > 1)
                {
                    ColumnInfo scalarColumn = verticalColumns.First(col => col.PathSegments.Length == depth+1);
                    GameConfigSpreadsheetLocation scalarLocation = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, scalarColumn.ColumnIndex, scalarColumn.ColumnIndex + 1);

                    foreach (ColumnInfo col in verticalColumns)
                    {
                        if (ReferenceEquals(col, scalarColumn))
                            continue;

                        GameConfigSpreadsheetLocation location = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, col.ColumnIndex, col.ColumnIndex + 1);

                        if (col.FullPath == scalarColumn.FullPath)
                            buildLog.WithLocation(location).Error($"Duplicate header cell for '{col.FullPath}'. First occurrence is at {scalarLocation.SourceInfo.GetLocationUrl(scalarLocation)} .");
                        else
                            buildLog.WithLocation(location).Error($"Conflicting header cells '{scalarColumn.FullPath}' and '{col.FullPath}'. The former occurs at {scalarLocation.SourceInfo.GetLocationUrl(scalarLocation)} . The same value cannot be both a scalar and a compound object.");

                        hasErrors = true;
                    }
                }
            }
            else
            {
                // Does not contain scalar columns. Check duplicates.

                foreach (IGrouping<NodeMemberId, ColumnInfo> group in verticalColumns.GroupBy(columnInfo => columnInfo.PathSegments[depth + 1].SegmentId))
                {
                    if (group.Count() != 1)
                    {
                        ColumnInfo firstColumn = group.First();
                        GameConfigSpreadsheetLocation firstLocation = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, firstColumn.ColumnIndex, firstColumn.ColumnIndex + 1);

                        foreach (ColumnInfo col in verticalColumns)
                        {
                            if (ReferenceEquals(col, firstColumn))
                                continue;

                            GameConfigSpreadsheetLocation location = GameConfigSpreadsheetLocation.FromColumns(buildLog.SourceInfo, col.ColumnIndex, col.ColumnIndex + 1);

                            buildLog.WithLocation(location).Error($"Duplicate header cell for '{col.FullPath}'. First occurrence is at {firstLocation.SourceInfo.GetLocationUrl(firstLocation)} .");

                            hasErrors = true;
                        }
                    }
                }
            }

            if (hasErrors)
                return;
        }

        static CollectionNode ConvertVerticalCollection(GameConfigBuildLog buildLog, List<List<SpreadsheetCell>> itemRows, int depth, ColumnInfo[] columnInfos)
        {
            bool isElementComplex = columnInfos.Length > 1 || columnInfos[0].PathSegments.Length > depth + 1;

            // Parse all elements rows of collection (rows of sheet) -- element can be simple or complex
            List<NodeBase> elements = new List<NodeBase>();
            for (int rowNdx = 0; rowNdx < itemRows.Count; rowNdx++)
            {
                List<SpreadsheetCell> rowCells = itemRows[rowNdx];

                // Parse all columns of the element
                if (isElementComplex)
                {
                    // Parse a full collection element
                    OrderedDictionary<NodeMemberId, NodeBase> members = new OrderedDictionary<NodeMemberId, NodeBase>();
                    foreach (ColumnInfo columnInfo in columnInfos)
                    {
                        int columnIndex = columnInfo.ColumnIndex;

                        SpreadsheetCell cell = rowCells[columnIndex];
                        if (!string.IsNullOrEmpty(cell.Value))
                        {
                            ScalarNode scalar = new ScalarNode(cell.Value, GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell));
                            members.Add(columnInfo.PathSegments[columnInfo.PathSegments.Length - 1].SegmentId, scalar);
                        }
                    }

                    // Append element to collection, but only if at least 1 member had a non-empty cell.
                    if (members.Count > 0)
                    {
                        // Fill inbetween skipped elements.
                        while (elements.Count < rowNdx)
                            elements.Add(null); // \todo null or a GameConfigSyntaxTree.Object with 0 members? #missing-collection-entry

                        ObjectNode element = new ObjectNode(members);
                        elements.Add(element);
                    }
                }
                else
                {
                    int columnIndex = columnInfos[0].ColumnIndex;

                    SpreadsheetCell cell = rowCells[columnIndex];
                    if (!string.IsNullOrEmpty(cell.Value))
                    {
                        // Fill inbetween skipped elements.
                        while (elements.Count < rowNdx)
                            elements.Add(null); // \todo null or empty string? #missing-collection-entry

                        elements.Add(new ScalarNode(cell.Value, GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell)));
                    }
                }
            }

            return new CollectionNode(elements);
        }

        static CollectionNode ConvertIndexedCollection(GameConfigBuildLog buildLog, List<List<SpreadsheetCell>> itemRows, int depth, PathNode[] indexedElements)
        {
            List<NodeBase> elements = new List<NodeBase>();

            for (int elemNdx = 0; elemNdx < indexedElements.Length; elemNdx++)
            {
                // Parse all columns of the element
                PathNode elemNode = indexedElements[elemNdx];
                switch (elemNode)
                {
                    case PathNodeScalar scalarNode:
                        ColumnInfo columnInfo = scalarNode.ColumnInfo;
                        List<SpreadsheetCell> rowCells = itemRows[0];
                        SpreadsheetCell cell = rowCells[columnInfo.ColumnIndex];
                        if (!string.IsNullOrEmpty(cell.Value))
                        {
                            // Fill inbetween skipped elements.
                            while (elements.Count < elemNdx)
                                elements.Add(null); // \todo null or empty string? #missing-collection-entry

                            ScalarNode element = new ScalarNode(cell.Value, GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell));
                            elements.Add(element);
                        }
                        break;

                    case PathNodeObject objectNode:
                        // Recursively convert object nodes (only keep children with some items)
                        ObjectNode elemObj = ConvertItemToObjectNode(buildLog, objectNode, itemRows, depth + 1);
                        if (elemObj.Members.Count > 0)
                        {
                            // Fill inbetween skipped elements.
                            while (elements.Count < elemNdx)
                                elements.Add(null); // \todo null or a GameConfigSyntaxTree.Object with 0 members? #missing-collection-entry

                            elements.Add(elemObj);
                        }

                        break;

                    case PathNodeCollection:
                        throw new InvalidOperationException("Nested collections are not supported"); // \todo Put error message in buildLog

                    case null:
                        break;

                    default:
                        throw new InvalidOperationException($"Invalid node type {elemNode.GetType()}");
                }
            }

            return new CollectionNode(elements);
        }

        static ObjectNode ConvertItemToObjectNode(GameConfigBuildLog buildLog, PathNodeObject node, List<List<SpreadsheetCell>> itemRows, int depth)
        {
            OrderedDictionary<NodeMemberId, NodeBase> children = new OrderedDictionary<NodeMemberId, NodeBase>();

            // Iterate all children: parse any leaf nodes & recurse into any non-leaf nodes.
            foreach ((NodeMemberId childId, PathNode childNode) in node.Children)
            {
                switch (childNode)
                {
                    case PathNodeScalar scalarNode:
                    {
                        ColumnInfo  columnInfo  = scalarNode.ColumnInfo;
                        int         columnIndex = columnInfo.ColumnIndex;

                        // Read a scalar value from the first cell
                        SpreadsheetCell cell = itemRows[0][columnIndex];
                        if (!string.IsNullOrEmpty(cell.Value))
                        {
                            ScalarNode scalar = new ScalarNode(cell.Value, GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell));
                            children.Add(childId, scalar);
                        }
                        // \todo [petri] check other vertical cells are empty (if any)

                        break;
                    }

                    case PathNodeCollection collectionNode:
                    {
                        bool HasValuesInCells(ColumnInfo[] columns)
                        {
                            foreach (ColumnInfo column in columns)
                            {
                                int colNdx = column.ColumnIndex;
                                for (int rowNdx = 0; rowNdx < itemRows.Count; rowNdx++)
                                {
                                    if (!string.IsNullOrEmpty(itemRows[rowNdx][colNdx].Value))
                                        return true;
                                }
                            }

                            return false;
                        }

                        // Check which representations have any values defined for this item
                        bool hasScalarValue     = collectionNode.ScalarColumn != null ? HasValuesInCells(new[] { collectionNode.ScalarColumn }) : false;
                        bool hasVerticalValues  = collectionNode.VerticalColumns != null ? HasValuesInCells(collectionNode.VerticalColumns) : false;
                        bool hasIndexedValues   = collectionNode.IndexedColumns != null ? HasValuesInCells(collectionNode.IndexedColumns) : false;

                        // Only allow one representation in each item
                        // \todo Does this work for variants?!
                        int numRepresentationsUsed = (hasScalarValue ? 1 : 0) + (hasVerticalValues ? 1 : 0) + (hasIndexedValues ? 1 : 0);
                        if (numRepresentationsUsed > 1)
                        {
                            int startRow = itemRows[0][0].Row;
                            int endRow = itemRows[itemRows.Count - 1][0].Row + 1;
                            GameConfigSourceLocation location = GameConfigSpreadsheetLocation.FromRows(buildLog.SourceInfo, startRow, endRow);
                            buildLog.WithLocation(location).Error(Invariant($"Collection {node.Name} specified using multiple representations. Only one representation is allowed in a single item."));
                            break;
                        }

                        // Handle the representation used by the item
                        if (hasScalarValue)
                        {
                            // The whole collection is parsed from a single "scalar" cell
                            // \todo Check that other vertical cells are empty
                            SpreadsheetCell cell = itemRows[0][collectionNode.ScalarColumn.ColumnIndex];
                            ScalarNode scalar = new ScalarNode(cell.Value, GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell));
                            children.Add(childId, scalar);
                        }
                        else if (hasVerticalValues)
                        {
                            CollectionNode collection = ConvertVerticalCollection(buildLog, itemRows, depth, collectionNode.VerticalColumns);
                            children.Add(childId, collection);
                        }
                        else if (hasIndexedValues)
                        {
                            // \todo Check that other vertical cells are empty
                            CollectionNode collection = ConvertIndexedCollection(buildLog, itemRows, depth, collectionNode.IndexedNodes);
                            children.Add(childId, collection);
                        }
                        else // no values for any representation
                        {
                            // Output implicitly empty collection: in spreadsheets, the default behavior for parsing a collection
                            // that has no elements specified is to output an empty collection. Implicitly empty collections
                            // behave specially in the variant inheritance logic where implicitly empty collections in the
                            // variants use the value from the baseline.
                            children.Add(childId, new CollectionNode(Array.Empty<NodeBase>()));
                        }

                        break;
                    }

                    case PathNodeObject objectNode:
                    {
                        // Recursively convert nested nodes (only keep children with some items)
                        ObjectNode childObj = ConvertItemToObjectNode(buildLog, objectNode, itemRows, depth + 1);
                        if (childObj.Members.Count > 0)
                            children.Add(childId, childObj);
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Invalid PathNode type {childNode.GetType().ToGenericTypeString()}");
                }
            }

            return new ObjectNode(children);
        }

        static bool RowStartsNewItem(List<SpreadsheetCell> row, ColumnInfo[] identityColumns, ColumnInfo[] variantIdColumns)
        {
            // If any cell in an index column has a value, this row starts a new item
            foreach (ColumnInfo info in identityColumns)
            {
                if (!string.IsNullOrEmpty(row[info.ColumnIndex].Value))
                    return true;
            }

            // If any cell in a variant id column has a value, this row starts a new item
            foreach (ColumnInfo info in variantIdColumns)
            {
                if (!string.IsNullOrEmpty(row[info.ColumnIndex].Value))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Convert the input spreadsheet data (<paramref name="contentRows"/>) into a set of syntax tree objects (<see cref="GameConfigSyntaxTree.RootObject"/>).
        /// The input spreadsheet is divided into items such that any non-empty cell for <paramref name="identityColumns"/> or <paramref name="variantIdColumns"/>
        /// begins a new item. The identity columns are inherited from the previous entries where they are not defined.
        /// </summary>
        /// <param name="buildLog"></param>
        /// <param name="rootNode"></param>
        /// <param name="contentRows"></param>
        /// <param name="identityColumns"></param>
        /// <param name="variantIdColumns"></param>
        /// <returns></returns>
        static List<RootObject> ParseItems(GameConfigBuildLog buildLog, PathNodeObject rootNode, List<List<SpreadsheetCell>> contentRows, ColumnInfo[] identityColumns, ColumnInfo[] variantIdColumns)
        {
            List<RootObject> resultNodes = new List<RootObject>();

            int numRows = contentRows.Count;
            int startNdx = 0;
            string[] identityValues = new string[identityColumns.Length]; // keep track of identity column values to handle inheritance (from above)
            while (startNdx < contentRows.Count)
            {
                // Resolve identity values from first row of item
                for (int ndx = 0; ndx < identityColumns.Length; ndx++)
                {
                    ColumnInfo      info = identityColumns[ndx];
                    SpreadsheetCell cell = contentRows[startNdx][info.ColumnIndex];
                    if (!string.IsNullOrEmpty(cell.Value))
                        identityValues[ndx] = cell.Value;

                    // Each item must have valid values for all identity columns
                    if (string.IsNullOrEmpty(identityValues[ndx]))
                        buildLog.WithLocation(GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell)).Error("Empty value for identity column -- cannot infer from above either");
                }

                // Find the endNdx (start of next item or end-of-sheet)
                int endNdx = startNdx + 1;
                while (endNdx < numRows)
                {
                    if (RowStartsNewItem(contentRows[endNdx], identityColumns, variantIdColumns))
                        break;
                    endNdx += 1;
                }

                // Resolve rows in the original sheet for the object's location: this works also in case some rows were removed from the input, but not if rows were re-ordered
                int startRowNdx = contentRows[startNdx][0].Row;
                int endRowNdx = contentRows[endNdx - 1][0].Row + 1; // \note Use the last existing row to avoid out-of-bounds accesses
                GameConfigSpreadsheetLocation objectLocation = GameConfigSpreadsheetLocation.FromRows(buildLog.SourceInfo, startRowNdx, endRowNdx);

                // Parse the object id & value from the rows
                List<List<SpreadsheetCell>> itemRows = contentRows.GetRange(startNdx, endNdx - startNdx);
                ObjectId objectId = new ObjectId(identityValues.ToArray()); // \note defensive copy
                ObjectNode objectNode = ConvertItemToObjectNode(buildLog, rootNode, itemRows, depth: 0);
                resultNodes.Add(new RootObject(objectId, objectNode, objectLocation, aliases: null, variantId: null)); // \note aliases and variantId are filled later

                // Start parsing next element
                startNdx = endNdx;
            }

            return resultNodes;
        }

        static List<RootObject> TransformSpreadsheetImpl(GameConfigBuildLog buildLog, List<SpreadsheetCell> headerRow, List<List<SpreadsheetCell>> contentRows)
        {
            // \note Assumes rectangular input spreadsheet (all rows must be padded to same length)

            // Parse header columns
            // \todo [petri] Only allow one non-indexed segment in a path!
            ColumnInfo[] columns = ParseHeaderRow(buildLog, headerRow);
            //Console.WriteLine("Columns:");
            //foreach (ColumnInfo column in columns)
            //    Console.WriteLine("  {0}", column);
            //Console.WriteLine("");

            // Filter out comment columns
            columns = columns.Where(col => !col.HasTagWithName(BuiltinTags.Comment)).ToArray();

            // Check nonempty columns don't have missing header; i.e. that empty-header columns are fully empty.
            CheckNoMissingHeaderInNonemptyColumns(buildLog, columns, contentRows);
            // Filter out empty columns now that we're done with the above check.
            columns = columns.Where(col => !col.HasEmptyHeader).ToArray();

            // Create hierarchy from the columns (for nested members)
            PathNodeObject rootNode = CreateObjectNode(buildLog, PathSegment.Root.Name, columns, depth: 0);
            //Console.WriteLine("Hierarchy:\n{0}", rootNode.ToString());

            // Resolve key/identity columns. At least one key column must be specified.
            ColumnInfo[] keyColumns = columns.Where(col => col.IsKeyColumn).ToArray();
            if (keyColumns.Length == 0)
                buildLog.Error($"No key columns were specified. At least one column must have the '#{BuiltinTags.Key}' tag.");

            // Resolve variant id column, if any (there is at most 1, identified by the fixed VariantIdKey).
            ColumnInfo[] variantIdColumns = columns.Where(col => col.FullPath == GameConfigSyntaxTreeUtil.VariantIdKey).ToArray();

            // Check that identity columns do not have variants.
            foreach (ColumnInfo column in columns)
            {
                if (column.IsKeyColumn && column.PathSegments[column.PathSegments.Length - 1].VariantId != null)
                {
                    SpreadsheetCell cell = headerRow[column.ColumnIndex];
                    GameConfigSourceLocation location = GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell);
                    buildLog.WithLocation(location).Error(message: $"Identity column {column.FullPath} has a variant override, which isn't supported");
                }
            }

            // If any errors from the header, bail out
            if (buildLog.HasErrors())
                return null;

            // Split into items, group by item and variant id, and parse each item-with-variants to a syntax tree object
            List<RootObject> parsedObjects = ParseItems(buildLog, rootNode, contentRows, keyColumns, variantIdColumns);
            return parsedObjects;
        }

        public static List<RootObject> TransformLibrarySpreadsheet(GameConfigBuildLog buildLog, SpreadsheetContent content)
        {
            // \note Assumes rectangular input spreadsheet (all rows must be padded to same length)

            return TransformSpreadsheetImpl(
                buildLog,
                headerRow: content.Cells[0],
                contentRows: content.Cells.Skip(1).ToList());
        }

        public static List<RootObject> TransformKeyValueSpreadsheet(GameConfigBuildLog buildLog, SpreadsheetContent content)
        {
            List<SpreadsheetCell> syntheticHeader = new List<SpreadsheetCell>
            {
                new SpreadsheetCell("MemberName #key", 0, 0),
                new SpreadsheetCell("MemberValue", 0, 1),
            };

            IEnumerable<List<SpreadsheetCell>> contentRows = content.Cells;

            int variantIdColumnIndex = content.Cells[0].FindIndex(cell => cell.Value == GameConfigSyntaxTreeUtil.VariantIdKey);

            if (variantIdColumnIndex >= 0)
            {
                if (variantIdColumnIndex != 0)
                {
                    SpreadsheetCell cell = content.Cells[0][variantIdColumnIndex];
                    buildLog.WithLocation(GameConfigSpreadsheetLocation.FromCell(buildLog.SourceInfo, cell)).Error($"Variant id column '{GameConfigSyntaxTreeUtil.VariantIdKey}' must be the first column if present.");
                    return null;
                }

                // Bump columns of existing syntheticHeader cells by 1.
                syntheticHeader = syntheticHeader.Select(cell => new SpreadsheetCell(cell.Value, row: cell.Row, column: 1 + cell.Column)).ToList();
                // Insert variant id header cell in first column.
                syntheticHeader.Insert(0, content.Cells[0][variantIdColumnIndex]);
                contentRows = contentRows.Skip(1);
            }

            // \todo [nuutti] Below is modified copypaste from GameConfigParsePipeline.PreprocessSpreadsheet.
            //       Done here again due to syntheticHeader.
            // Pad all rows with empty cells to equal length
            {
                IEnumerable<List<SpreadsheetCell>> rows = new List<SpreadsheetCell>[]{ syntheticHeader }.Concat(contentRows);
                int maxColumns = rows.Max(row => row.Count);
                foreach (List<SpreadsheetCell> row in rows)
                {
                    while (row.Count < maxColumns)
                        row.Add(new SpreadsheetCell("", row: row[0].Row, column: row.Count));
                }
            }

            return TransformSpreadsheetImpl(
                buildLog,
                headerRow: syntheticHeader,
                contentRows: contentRows.ToList());
        }
    }
}
