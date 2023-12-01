// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Utility for comparing two objects in the serialized form. This is intended for finding and explaining byte-level differences
    /// in serialized wire data instead of the projected (deserialized) form.
    /// </summary>
    public class SerializedObjectComparer
    {
        /// <summary>
        /// Optional human-readable description of the first object.
        /// </summary>
        public string FirstName;

        /// <summary>
        /// Optional human-readable description of the second object.
        /// </summary>
        public string SecondName;

        /// <summary>
        /// Optional type of the compared objects. If type is given, it will be used to
        /// resolve human-readable type and member names.
        /// </summary>
        public Type Type;

        /// <summary>
        /// Report describing the differences between two compared objects.
        /// </summary>
        public struct Report
        {
            /// <summary>
            /// Human readable description of the differences.
            /// </summary>
            public string Description;

            /// <summary>
            /// Paths within the object where the differences occurred.
            /// These are "vague" paths in that certain dynamic details of them
            /// are omitted, such as array indexes. The purpose is to be able to
            /// use these in incident report grouping (fingerprinting), where
            /// such details would be usually uninteresting and would cause two
            /// incidents to be grouped separately even when they are essentially
            /// similar, differing only in these details.
            /// <para>
            /// This may be null, in particular when the object comparison outcome
            /// is determined without traversing the objects (such as when one of
            /// the top-level objects is null).
            /// </para>
            /// </summary>
            public List<string> VagueDifferencePathsMaybe;

            public Report(string description, List<string> vagueDifferencePathsMaybe)
            {
                Description = description;
                VagueDifferencePathsMaybe = vagueDifferencePathsMaybe;
            }
        }

        /// <summary>
        /// Compares objects and returns a report of the differences.
        /// </summary>
        public Report Compare(byte[] firstSerialized, byte[] secondSerialized)
        {
            if (firstSerialized == null && secondSerialized == null)
                return new Report($"{GetTitleFragment()} are both null", null);
            else if (firstSerialized == null)
                return new Report($"{FirstName ?? "first object"} is null, {SecondName ?? "second object"} is not null", null);
            else if (secondSerialized == null)
                return new Report($"{FirstName ?? "first object"} is not null, {SecondName ?? "second object"} is null", null);

            if (Util.ArrayEqual(firstSerialized, secondSerialized))
                return new Report($"{GetTitleFragment()} are bit-exact identical", null);

            using (IOReader firstReader = new IOReader(firstSerialized))
            using (IOReader secondReader = new IOReader(secondSerialized))
                return Compare(firstReader, secondReader);
        }

        public Report Compare(IOBuffer firstSerialized, IOBuffer secondSerialized)
        {
            if (firstSerialized == null)
                throw new ArgumentNullException(nameof(firstSerialized));
            if (secondSerialized == null)
                throw new ArgumentNullException(nameof(secondSerialized));

            if (IOBufferUtil.ContentsEqual(firstSerialized, secondSerialized))
                return new Report($"{GetTitleFragment()} are bit-exact identical", null);

            using (IOReader firstReader = new IOReader(firstSerialized))
            using (IOReader secondReader = new IOReader(secondSerialized))
                return Compare(firstReader, secondReader);
        }

        Report Compare(IOReader firstReader, IOReader secondReader)
        {
            if (firstReader == null)
                throw new ArgumentNullException(nameof(firstReader));
            if (secondReader == null)
                throw new ArgumentNullException(nameof(secondReader));

            TaggedSerializedInspector.ObjectInfo firstInfo;
            TaggedSerializedInspector.ObjectInfo secondInfo;
            try
            {
                firstInfo = TaggedSerializedInspector.Inspect(firstReader, Type, checkReaderWasCompletelyConsumed: true);
            }
            catch (Exception ex)
            {
                return new Report($"Error during compare. Cannot parse {FirstName ?? "first object"}: {ex}", null);
            }
            try
            {
                secondInfo = TaggedSerializedInspector.Inspect(secondReader, Type, checkReaderWasCompletelyConsumed: true);
            }
            catch (Exception ex)
            {
                return new Report($"Error during compare. Cannot parse {SecondName ?? "second object"}: {ex}", null);
            }

            Comparer comparer = new Comparer();
            try
            {
                comparer.Compare(Type, firstInfo, secondInfo);
            }
            catch (Exception ex)
            {
                return new Report($"Error during compare. {ex}", null);
            }

            if (comparer.NumDifferences == 0)
                return new Report($"Error during compare. Data is not bit exact but {GetTitleFragment()} are structurally equal.", null);

            return new Report(
                description: $"{GetTitleFragment()} differ:\n{comparer.GetDiffString()}",
                vagueDifferencePathsMaybe: comparer.DifferencePaths.Select(ConvertToVagueDifferencePath).ToList());
        }

        string GetTitleFragment()
        {
            if (FirstName != null)
                return $"Objects {FirstName} and {SecondName}";
            return "Objects";
        }

        string ConvertToVagueDifferencePath(string[] path)
        {
            return string.Join(" -> ", path.Select(ConvertToVagueDifferencePathElement));
        }

        string ConvertToVagueDifferencePathElement(string element)
        {
            // For array and key-value indexing, omit the actual index/key,
            // merely retain the info that this was an indexing element.
            if (element.StartsWith("[", StringComparison.Ordinal)
             && element.EndsWith("]", StringComparison.Ordinal))
            {
                return "[]";
            }

            return element;
        }

        struct Comparer
        {
            StringBuilder _builder;
            List<string> _path;
            int _currentLevel;
            public List<string[]> DifferencePaths;
            public int NumDifferences;

            public string GetDiffString()
            {
                return _builder.ToString();
            }

            static string MemberName(TaggedSerializedInspector.ObjectInfo.MemberInfo member)
            {
                if (member.Name != null)
                    return member.Name;
                return FormattableString.Invariant($"<MetaMember:{member.TagId}>");
            }

            static string TypeName(TaggedSerializedInspector.ObjectInfo info)
            {
                if (info.SerializableType != null)
                    return $"({info.SerializableType.Name})";

                if (info.TypeCode == 0)
                    return "<null>";

                if (info.TypeCode.HasValue)
                    return FormattableString.Invariant($"<MetaDerived:{info.TypeCode}>");

                return "()";
            }

            void PushPath(string step)
            {
                _path.Add(step);
            }

            void PopPath()
            {
                if (_currentLevel == _path.Count)
                {
                    _builder.Append('\n');
                    _currentLevel--;
                    WriteIndent();
                    _builder.Append('}');
                }

                _path.RemoveAt(_path.Count - 1);
            }

            void WriteOnlyError(string error)
            {
                DoWriteError(error, true);
            }
            void WriteAggregateError(string error)
            {
                DoWriteError(error, false);
            }

            void DoWriteError(string error, bool onlyError)
            {
                if (NumDifferences != 0)
                    _builder.Append('\n');

                int numExpandedLevels = (onlyError) ? _path.Count - 1 : _path.Count;
                while (_currentLevel < numExpandedLevels)
                {
                    WriteIndent();
                    _builder.Append(_path[_currentLevel]);
                    _builder.Append(" {\n");
                    _currentLevel++;
                }

                if (onlyError)
                {
                    WriteIndent();
                    _builder.Append(_path[_currentLevel]);
                    _builder.Append(": ");
                }
                else
                {
                    WriteIndent();
                }

                _builder.Append(error);
                DifferencePaths.Add(_path.ToArray()); // \note Copies _path, _path is mutable.

                NumDifferences++;
            }

            void WriteIndent()
            {
                for (int ndx = 0; ndx < _currentLevel; ++ndx)
                    _builder.Append("  ");
            }

            public void Compare(Type type, TaggedSerializedInspector.ObjectInfo firstInfo, TaggedSerializedInspector.ObjectInfo secondInfo)
            {
                _builder = new StringBuilder();

                _path = new List<string>();
                if (type != null)
                    PushPath(type.ToGenericTypeString());
                else
                    PushPath("<root>");

                _currentLevel = 0;
                DifferencePaths = new List<string[]>();
                NumDifferences = 0;

                CompareSerializedObjects(firstInfo, secondInfo);

                PopPath();
            }

            void CompareSerializedObjects(TaggedSerializedInspector.ObjectInfo firstInfo, TaggedSerializedInspector.ObjectInfo secondInfo)
            {
                if (firstInfo.WireType != secondInfo.WireType)
                {
                    WriteOnlyError($"[WireType] {firstInfo.WireType} vs {secondInfo.WireType}");
                }
                else if ((firstInfo.Members != null) != (secondInfo.Members != null))
                {
                    string firstValue = (firstInfo.Members == null) ? "<null>" : "<not-null>";
                    string secondValue = (secondInfo.Members == null) ? "<null>" : "<not-null>";

                    WriteOnlyError($"{firstValue} vs {secondValue}");
                }
                else if ((firstInfo.ValueCollection != null) != (secondInfo.ValueCollection != null))
                {
                    string firstValue = (firstInfo.ValueCollection == null) ? "<null>" : "<not-null>";
                    string secondValue = (secondInfo.ValueCollection == null) ? "<null>" : "<not-null>";

                    WriteOnlyError($"{firstValue} vs {secondValue}");
                }
                else if ((firstInfo.KeyValueCollection != null) != (secondInfo.KeyValueCollection != null))
                {
                    string firstValue = (firstInfo.KeyValueCollection == null) ? "<null>" : "<not-null>";
                    string secondValue = (secondInfo.KeyValueCollection == null) ? "<null>" : "<not-null>";

                    WriteOnlyError($"{firstValue} vs {secondValue}");
                }
                else if (firstInfo.IsPrimitive)
                {
                    // Bytes is represented as byte[], which is a reference type. Other types implement
                    // Equals() as expected.
                    if (firstInfo.WireType != WireDataType.Bytes && !Equals(firstInfo.PrimitiveValue, secondInfo.PrimitiveValue))
                    {
                        object firstValue = firstInfo.ProjectedPrimitiveValue ?? firstInfo.PrimitiveValue ?? "<null>";
                        object secondValue = secondInfo.ProjectedPrimitiveValue ?? secondInfo.PrimitiveValue ?? "<null>";

                        WriteOnlyError(FormattableString.Invariant($"{firstValue} vs {secondValue}"));
                    }
                    else if (firstInfo.WireType == WireDataType.Bytes && !Util.ArrayEqual((byte[])firstInfo.PrimitiveValue, (byte[])secondInfo.PrimitiveValue))
                    {
                        string firstValue = firstInfo.PrimitiveValue != null ? Convert.ToBase64String((byte[])firstInfo.PrimitiveValue) : "<null>";
                        string secondValue = secondInfo.PrimitiveValue != null ? Convert.ToBase64String((byte[])secondInfo.PrimitiveValue) : "<null>";

                        WriteOnlyError(FormattableString.Invariant($"{firstValue} vs {secondValue}"));
                    }
                }
                else if (firstInfo.Members != null)
                {
                    if (firstInfo.TypeCode != secondInfo.TypeCode)
                    {
                        string firstType = TypeName(firstInfo);
                        string secondType = TypeName(secondInfo);

                        WriteOnlyError($"[Type] {firstType} vs {secondType}");
                    }
                    else
                    {
                        bool hasDynamicType = firstInfo.TypeCode.HasValue;
                        if (hasDynamicType)
                            PushPath(TypeName(firstInfo));

                        for (int ndx = 0; ndx < System.Math.Max(firstInfo.Members.Count, secondInfo.Members.Count); ++ndx)
                        {
                            TaggedSerializedInspector.ObjectInfo.MemberInfo? firstMember = (ndx < firstInfo.Members.Count) ? firstInfo.Members[ndx] : (TaggedSerializedInspector.ObjectInfo.MemberInfo?)null;
                            TaggedSerializedInspector.ObjectInfo.MemberInfo? secondMember = (ndx < secondInfo.Members.Count) ? secondInfo.Members[ndx] : (TaggedSerializedInspector.ObjectInfo.MemberInfo?)null;

                            if (firstMember is null)
                            {
                                WriteAggregateError($"[Member] <no-such-member> vs {MemberName(secondMember.Value)}");
                            }
                            else if (secondMember is null)
                            {
                                WriteAggregateError($"[Member] {MemberName(firstMember.Value)} vs <no-such-member>");
                            }
                            else if (firstMember.Value.TagId != secondMember.Value.TagId)
                            {
                                WriteAggregateError(FormattableString.Invariant($"[MemberTag] <MetaMember:{firstMember.Value.TagId}> vs <MetaMember:{secondMember.Value.TagId}>"));
                            }
                            else
                            {
                                PushPath(MemberName(firstMember.Value));
                                CompareSerializedObjects(firstMember.Value.ObjectInfo, secondMember.Value.ObjectInfo);
                                PopPath();
                            }
                        }

                        if (hasDynamicType)
                            PopPath();
                    }
                }
                else if (firstInfo.ValueCollection != null)
                {
                    for (int ndx = 0; ndx < System.Math.Max(firstInfo.ValueCollection.Count, secondInfo.ValueCollection.Count); ++ndx)
                    {
                        TaggedSerializedInspector.ObjectInfo firstElem = (ndx < firstInfo.ValueCollection.Count) ? firstInfo.ValueCollection[ndx] : null;
                        TaggedSerializedInspector.ObjectInfo secondElem = (ndx < secondInfo.ValueCollection.Count) ? secondInfo.ValueCollection[ndx] : null;

                        PushPath(FormattableString.Invariant($"[{ndx}]"));
                        if (firstElem is null)
                        {
                            WriteOnlyError("<missing> vs <value>");
                        }
                        else if (secondElem is null)
                        {
                            WriteOnlyError("<value> vs <missing>");
                        }
                        else
                        {
                            CompareSerializedObjects(firstElem, secondElem);
                        }
                        PopPath();
                    }
                }
                else if (firstInfo.KeyValueCollection != null)
                {
                    var firstEnumerator = firstInfo.KeyValueCollection.GetEnumerator();
                    var secondEnumerator = secondInfo.KeyValueCollection.GetEnumerator();
                    bool firstHasRemaining;
                    bool secondHasRemaining;
                    int index = 0;

                    for (;;)
                    {
                        firstHasRemaining = firstEnumerator.MoveNext();
                        secondHasRemaining = secondEnumerator.MoveNext();
                        if (!firstHasRemaining || !secondHasRemaining)
                            break;

                        PushPath(FormattableString.Invariant($"[{index}]"));
                        PushPath($"<Key>");
                        CompareSerializedObjects(firstEnumerator.Current.Key, secondEnumerator.Current.Key);
                        PopPath();

                        PushPath($"<Value>");
                        CompareSerializedObjects(firstEnumerator.Current.Value, secondEnumerator.Current.Value);
                        PopPath();
                        PopPath();

                        index++;
                    }

                    while (firstHasRemaining)
                    {
                        PushPath(FormattableString.Invariant($"[{index}]"));
                        WriteOnlyError("<value> vs <missing>");
                        PopPath();

                        firstHasRemaining = firstEnumerator.MoveNext();
                        index++;
                    }
                    while (secondHasRemaining)
                    {
                        PushPath(FormattableString.Invariant($"[{index}]"));
                        WriteOnlyError("<missing> vs <value>");
                        PopPath();

                        secondHasRemaining = secondEnumerator.MoveNext();
                        index++;
                    }
                }
            }
        }
    }
}
