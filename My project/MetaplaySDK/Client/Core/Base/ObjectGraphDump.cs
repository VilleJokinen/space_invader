// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using static System.FormattableString;

using ObjectHandle = System.UInt64;

namespace Metaplay.Core
{
    /// <summary>
    /// Utility for dumping an object graph into a flat format, and for comparing
    /// two dump results. The object graph is not required to be a tree, and can
    /// have cycles.
    /// <para>
    /// There is only dumping (serialization), no deserialization. The use case for
    /// this is checking that an operation did not modify a given object:
    /// dump the object (<see cref="Dump"/>), do the operation, dump the object again,
    /// and compare the two dumps (<see cref="CompareDumpResults"/>). Note that the
    /// comparison is rather strict and may report differences even if the objects
    /// are structurally equal (therefore this is not appropriate for checking
    /// structural equality).
    /// </para>
    /// </summary>
    public static class ObjectGraphDump
    {
        /// <summary>
        /// Opt out a field from being traversed by the dumping.
        /// The dumping does not respect other ignore attributes such as
        /// <see cref="System.Runtime.Serialization.IgnoreDataMemberAttribute"/>,
        /// as the dumping is intended to be aware of runtime-only data as well.
        /// </summary>
        /// <remarks>
        /// This is only applicable to fields, not properties. The dumper does not
        /// read properties.
        /// </remarks>
        [AttributeUsage(AttributeTargets.Field)]
        public class IgnoreAttribute : Attribute
        {
        }

        public class DumpOptions
        {
            /// <summary>
            /// Limit on how many objects the dumping may visit.
            /// Exceeding this will throw an exception.
            /// </summary>
            public int ObjectCountSafetyLimit { get; }

            /// <summary>
            /// Initial capacity to allocate for the collections which will contain
            /// all of the objects in the graph. If possible, this should be set to
            /// a good guess of the total number of objects in the graph (but slightly
            /// higher than actual is preferable to slightly smaller).
            /// </summary>
            public int ObjectCollectionInitialCapacity { get; }

            /// <summary>
            /// An instance of <see cref="ObjectGraphDump.FieldInfoCache"/> that the
            /// dump operation should use. A cache can be re-used over multiple dumps
            /// to reduce memory allocations.
            /// </summary>
            public FieldInfoCache FieldInfoCache { get; set; }

            public DumpOptions(int objectCountSafetyLimit, int objectCollectionInitialCapacity)
            {
                ObjectCountSafetyLimit = objectCountSafetyLimit;
                ObjectCollectionInitialCapacity = objectCollectionInitialCapacity;
            }
        }

        /// <summary>
        /// Dump the object graph with <paramref name="root"/> as the starting node.
        /// </summary>
        public static DumpResult Dump(object root, DumpOptions options)
        {
            // Implementation is in a stateful class, for convenience of implementation.
            return new DumpImpl().Dump(root, options);
        }

        /// <summary>
        /// Print the dump result to a string for debugging purposes.
        /// </summary>
        public static string DumpToString(DumpResult dump, bool includeIdentityHashes = true)
        {
            IndentedStringBuilder sb = new IndentedStringBuilder(outputDebugCode: false);

            sb.AppendLine($"root <#{DumpResult.RootObjectHandle}>");

            for (int objectIndex = 0; objectIndex < dump.Objects.Count; objectIndex++)
            {
                ObjectHandle objectHandle = (ObjectHandle)objectIndex;
                ObjectEntry objectEntry = dump.Objects[objectIndex];

                sb.AppendLine($"<#{objectHandle}>:");
                sb.Indent();

                if (objectEntry.Obj == null)
                    sb.AppendLine("null");
                else
                {
                    Type type = objectEntry.Obj.GetType();

                    sb.AppendLine($"type {type.ToNamespaceQualifiedTypeString()}");

                    if (includeIdentityHashes && TypeHasSensibleObjectIdentity(type))
                        sb.AppendLine(Invariant($"identity hash {RuntimeHelpers.GetHashCode(objectEntry.Obj)}"));

                    if (_scalarTypes.Contains(type))
                        sb.AppendLine(Invariant($"scalar {objectEntry.Obj}"));

                    if (objectEntry.ChildrenMaybe != null)
                    {
                        foreach (ChildEntry child in objectEntry.ChildrenMaybe)
                            sb.AppendLine($"{child.ChildId}: <#{child.ObjectHandle}>");
                    }
                }

                sb.Unindent();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Compare the two dump results, and report whether they're equal or different.
        /// If they're different, <see cref="ComparisonResult.DifferenceDescription"/> will contain a description.
        /// <para>
        /// The comparison also accounts for object identities.
        /// Therefore this is not appropriate for structural comparison, as it may report a difference
        /// even if the two object graphs only differ in object identities.
        /// </para>
        /// <para>
        /// Optionally, <paramref name="compareObjectRuntimeIdentities"/> may be set to false in order to
        /// ignore objects' runtime identities (reference equality) between the two dumps.
        /// However, if the graphs are not trees, this is still too strict to implement structural equality
        /// checking, as the "object handles" (<see cref="ChildEntry.ObjectHandle"/>) are still compared,
        /// and those can differ between the dumps if the graphs internally share their nodes in a different manner.
        /// For example, assuming Foo is a reference (class) type, then given
        /// <![CDATA[
        ///  A = new Foo{ f = 1 }; B = new List<Foo>{ A, A }
        /// and
        ///  X = new Foo{ f = 1 }; Y = new Foo{ f = 1 }; Z = new List<Foo>{ X, Y }
        /// ]]>,
        /// B and Z are structurally equivalent, but are reported as different due to B using the same
        /// instance A twice whereas Z uses the two different instances X and Y.
        /// </para>
        /// </summary>
        public static ComparisonResult CompareDumpResults(DumpResult aDump, DumpResult bDump, bool compareObjectRuntimeIdentities = true)
        {

            int minObjectCount = System.Math.Min(aDump.Objects.Count, bDump.Objects.Count);

            for (int objectIndex = 0; objectIndex < minObjectCount; objectIndex++)
            {
                ObjectHandle objectHandle = (ObjectHandle)objectIndex;

                ObjectEntry aObject = aDump.Objects[objectIndex];
                ObjectEntry bObject = bDump.Objects[objectIndex];

                ComparisonResult CreateResultWithPath(string baseDescription)
                {
                    Dictionary<ObjectHandle, (ObjectHandle Handle, int ChildIndex)> parents = ConstructParentRelations(aDump, objectHandle);
                    string path = ResolveHumanReadablePath(aDump, parents, objectHandle);
                    return ComparisonResult.Different($"{baseDescription}. Path: {path}");
                }

                if (aObject.Obj?.GetType() != bObject.Obj?.GetType())
                {
                    return CreateResultWithPath(
                        $"Types differ: {aObject.Obj?.GetType().ToNamespaceQualifiedTypeString() ?? "<null>"} " +
                        $"vs {bObject.Obj?.GetType().ToNamespaceQualifiedTypeString() ?? "<null>"}");
                }

                Type type = aObject.Obj?.GetType();

                // Only scalars are compared with Equals.
                if (type != null && _scalarTypes.Contains(type))
                {
                    if (!aObject.Obj.Equals(bObject.Obj))
                        return CreateResultWithPath(Invariant($"Values differ: {aObject.Obj} vs {bObject.Obj}"));
                }

                if (compareObjectRuntimeIdentities && type != null && TypeHasSensibleObjectIdentity(type))
                {
                    if (!ReferenceEquals(aObject.Obj, bObject.Obj))
                        return CreateResultWithPath("Object identities differ");
                }

                bool aHasChildren = aObject.ChildrenMaybe != null;
                bool bHasChildren = bObject.ChildrenMaybe != null;

                // Either both objects should have children or neither should, since we already checked the objects have the same type.
                if (aHasChildren != bHasChildren)
                    return CreateResultWithPath("Unexpected: one object has children and the other doesn't");

                if (aHasChildren)
                {
                    int minChildCount = System.Math.Min(aObject.ChildrenMaybe.Count, bObject.ChildrenMaybe.Count);

                    for (int childIndex = 0; childIndex < minChildCount; childIndex++)
                    {
                        ChildEntry aChild = aObject.ChildrenMaybe[childIndex];
                        ChildEntry bChild = bObject.ChildrenMaybe[childIndex];

                        // The ChildIds should never differ, since we already checked the objects have the same type.
                        if (aChild.ChildId.Kind != bChild.ChildId.Kind)
                            return CreateResultWithPath("Unexpected: child kinds differ");
                        if (aChild.ChildId.EnumerableIndex != bChild.ChildId.EnumerableIndex)
                            return CreateResultWithPath("Unexpected: child enumerable indexes differ");
                        if (aChild.ChildId.FieldInfoMaybe != bChild.ChildId.FieldInfoMaybe)
                            return CreateResultWithPath("Unexpected: child fields differ");

                        // Matching children can only differ in their values.
                        if (aChild.ObjectHandle != bChild.ObjectHandle)
                        {
                            string aHandleStr = aChild.ObjectHandle.ToString(CultureInfo.InvariantCulture);
                            if (aDump.Objects[(int)aChild.ObjectHandle].Obj == null)
                                aHandleStr += " (i.e. null)";

                            string bHandleStr = bChild.ObjectHandle.ToString(CultureInfo.InvariantCulture);
                            if (bDump.Objects[(int)bChild.ObjectHandle].Obj == null)
                                bHandleStr += " (i.e. null)";

                            return CreateResultWithPath($"Child [{aChild.ChildId}] object handles differ: {aHandleStr} vs {bHandleStr}");

                        }
                    }

                    // Child counts can differ between enumerables (including dictionaries).
                    if (aObject.ChildrenMaybe.Count != bObject.ChildrenMaybe.Count)
                        return CreateResultWithPath(Invariant($"Child counts differ: {aObject.ChildrenMaybe.Count} vs {bObject.ChildrenMaybe.Count}"));
                }
            }

            // Object counts should never differ, since no prior differences have been found.
            // An object cannot come out of nowhere, it must be either the root or the child of a prior object,
            // so a difference should've already been found there.
            if (aDump.Objects.Count != bDump.Objects.Count)
                return ComparisonResult.Different(Invariant($"Unexpected: object counts differ: {aDump.Objects.Count} vs {bDump.Objects.Count}"));

            return ComparisonResult.Equal;
        }

        public readonly struct ComparisonResult
        {
            public readonly bool DumpsAreEqual;
            public readonly string Description;

            public ComparisonResult(bool dumpsAreEqual, string description)
            {
                DumpsAreEqual = dumpsAreEqual;
                Description = description;
            }

            public static ComparisonResult Equal => new ComparisonResult(dumpsAreEqual: true, description: "Dumps are equal");
            public static ComparisonResult Different(string description) => new ComparisonResult(dumpsAreEqual: false, description: description);
        }

        /// <summary>
        /// Construct a mapping from each object to its parent, including the child's index within the parent's children list.
        /// This stops at parent <paramref name="lastParentToInclude"/>, in the order of <see cref="DumpResult.Objects"/>.
        /// This is for the purpose of reporting a path when a difference between dumps is found.
        /// In a non-tree graph, an object might be reachable by multiple edges. Here we only record the first edge encountered.
        /// As a special case, the root object is considered to have no parent.
        /// </summary>
        static Dictionary<ObjectHandle, (ObjectHandle Handle, int ChildIndex)> ConstructParentRelations(DumpResult dump, ObjectHandle lastParentToInclude)
        {
            Dictionary<ObjectHandle, (ObjectHandle Handle, int ChildIndex)> parents = new Dictionary<ObjectHandle, (ObjectHandle Handle, int ChildIndex)>();

            for (int objectIndex = 0; objectIndex < dump.Objects.Count; objectIndex++)
            {
                ObjectHandle objectHandle = (ObjectHandle)objectIndex;
                ObjectEntry objectEntry = dump.Objects[objectIndex];

                if (objectEntry.ChildrenMaybe != null)
                {
                    for (int childIndex = 0; childIndex < objectEntry.ChildrenMaybe.Count; childIndex++)
                    {
                        ChildEntry child = objectEntry.ChildrenMaybe[childIndex];

                        if (child.ObjectHandle != DumpResult.RootObjectHandle)
                        {
                            if (!parents.ContainsKey(child.ObjectHandle))
                                parents.Add(child.ObjectHandle, (objectHandle, childIndex));
                        }
                    }
                }

                if (objectHandle == lastParentToInclude)
                    break;
            }

            return parents;
        }

        /// <summary>
        /// Produce the path from the root object to the object with handle <paramref name="targetHandle"/>,
        /// using the given parent information as produced by <see cref="ConstructParentRelations"/>.
        /// </summary>
        /// <remarks>
        /// In a non-tree graph, there is no unambiguous path to each object. It's up to the caller to decide how to construct <paramref name="parents"/>.
        /// </remarks>
        static string ResolveHumanReadablePath(DumpResult dump, Dictionary<ObjectHandle, (ObjectHandle Handle, int ChildIndex)> parents, ObjectHandle targetHandle)
        {
            List<string> humanReadablePath = new List<string>();
            List<ObjectHandle> objectHandlePath = new List<ObjectHandle>();

            ObjectHandle handle = targetHandle;
            while (parents.TryGetValue(handle, out (ObjectHandle Handle, int ChildIndex) parent))
            {
                ObjectEntry parentObject = dump.Objects[(int)parent.Handle];
                ChildId childId = parentObject.ChildrenMaybe[parent.ChildIndex].ChildId;

                string part;
                switch (childId.Kind)
                {
                    case ChildKind.DictionaryKey:
                        part = Invariant($".Keys[{childId.EnumerableIndex}]");
                        break;

                    case ChildKind.DictionaryValue:
                    {
                        ChildEntry keyChild = parentObject.ChildrenMaybe[parent.ChildIndex-1];
                        ChildId keyChildId = keyChild.ChildId;
                        if (keyChildId.Kind != ChildKind.DictionaryKey
                         || keyChildId.EnumerableIndex != childId.EnumerableIndex)
                        {
                            throw new MetaAssertException(
                                $"Expected {nameof(ChildKind.DictionaryValue)} (index {childId.EnumerableIndex}) to be preceded by " +
                                $"corresponding {nameof(ChildKind.DictionaryKey)} in {nameof(ObjectEntry)}.{nameof(ObjectEntry.ChildrenMaybe)}, but got {keyChildId}");
                        }
                        object keyObject = dump.Objects[(int)keyChild.ObjectHandle].Obj;
                        part = Invariant($"[{keyObject}]");
                        break;
                    }

                    case ChildKind.EnumerableElement:
                    {
                        part = Invariant($"[{childId.EnumerableIndex}]");
                        break;
                    }

                    case ChildKind.Field:
                    {
                        part = $".{PrettifyFieldName(childId.FieldInfoMaybe?.Name)}";
                        break;
                    }

                    default:
                        throw new MetaAssertException("unreachable");
                }

                humanReadablePath.Add(part);
                objectHandlePath.Add(handle);

                if (objectHandlePath.Count > parents.Count)
                    throw new MetaAssertException("Object graph path longer than number of objects. Bug in the ObjectGraphDump implementation.");

                handle = parent.Handle;
            }

            humanReadablePath.Reverse();

            objectHandlePath.Add(handle);
            objectHandlePath.Reverse();

            string humanReadablePathStr = string.Concat(humanReadablePath);
            string objectHandlePathStr = string.Join(" -> ", objectHandlePath);

            return $"{humanReadablePathStr} ({nameof(ObjectGraphDump)} handle path: {objectHandlePathStr})";
        }

        /// <summary>
        /// Strips backing field name mangling, thus returning the corresponding
        /// property name.
        /// If the name isn't a backing field name, it's returned unchanged.
        /// </summary>
        static string PrettifyFieldName(string name)
        {
            if (name == null)
                return null;

            {
                const string prefix = "<";
                const string suffix = ">k__BackingField";

                if (name.StartsWith(prefix, StringComparison.Ordinal)
                    && name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
                }
            }

            return name;
        }

        /// <summary>
        /// The result from dumping an object graph with <see cref="Dump"/>.
        /// Contains a the objects found during the traversal of the object graph,
        /// listed in a flat format in <see cref="Objects"/>.
        /// <para>
        /// Each object instance is registered a "handle", which represents the
        /// object's identity _within this dump_. The handle is the index into
        /// the <see cref="Objects"/> list. Each object is either a scalar or
        /// has a list of children; children are things like fields and elements of lists.
        /// Each child refers to another object instance by its handle (<see cref="ChildEntry.ObjectHandle"/>).
        /// </para>
        /// </summary>
        /// <remarks>
        /// Value-typed objects get boxed and represented as objects. For example
        /// if there are multiple <c>int</c>s with the same value within the object graph,
        /// then those will appear as distinct entries in <see cref="Objects"/>
        /// (unless the <c>int</c>s were already boxed and actually referred to the
        /// same instance).
        /// </remarks>
        /// <remark>
        /// Objects' properties are not traversed, only fields are (including properties'
        /// backing fields).
        /// Properties might cause infinite graphs and furthermore are not guaranteed
        /// to be deterministic. It is assumed that all the relevant data within an
        /// object graph is reachable via fields (and enumerables). Note that simple
        /// auto-implemented properties are still effectively included, due to backing
        /// fields being included.
        /// </remark>
        public class DumpResult
        {
            public const ObjectHandle RootObjectHandle = 0;

            public readonly List<ObjectEntry> Objects;

            public DumpResult(List<ObjectEntry> objects)
            {
                Objects = objects;
            }
        }

        public readonly struct ObjectEntry
        {
            public readonly object Obj;
            public readonly List<ChildEntry> ChildrenMaybe;

            public ObjectEntry(object obj, List<ChildEntry> childrenMaybe)
            {
                Obj = obj;
                ChildrenMaybe = childrenMaybe;
            }
        }

        public readonly struct ChildEntry
        {
            public readonly ChildId ChildId;
            public readonly ObjectHandle ObjectHandle;

            public ChildEntry(ChildId childId, ObjectHandle objectHandle)
            {
                ChildId = childId;
                ObjectHandle = objectHandle;
            }
        }

        /// <summary>
        /// Identifies whether a child of an object is an enumerable element,
        /// dictionary key or value, or a field; and holds teh element/key/value
        /// index or the field identity.
        /// </summary>
        public readonly struct ChildId
        {
            public readonly ChildKind Kind;
            public readonly int? EnumerableIndex;
            public readonly FieldInfo FieldInfoMaybe;

            public ChildId(ChildKind kind, int? enumerableIndex, FieldInfo fieldInfoMaybe)
            {
                Kind = kind;
                EnumerableIndex = enumerableIndex;
                FieldInfoMaybe = fieldInfoMaybe;
            }

            public static ChildId DictionaryKey(int enumerableIndex) => new ChildId(ChildKind.DictionaryKey, enumerableIndex, fieldInfoMaybe: null);
            public static ChildId DictionaryValue(int enumerableIndex) => new ChildId(ChildKind.DictionaryValue, enumerableIndex, fieldInfoMaybe: null);
            public static ChildId EnumerableElement(int enumerableIndex) => new ChildId(ChildKind.EnumerableElement, enumerableIndex, fieldInfoMaybe: null);
            public static ChildId Field(FieldInfo fieldInfo) => new ChildId(ChildKind.Field, enumerableIndex: null, fieldInfo);

            public override string ToString()
            {
                switch (Kind)
                {
                    case ChildKind.DictionaryKey: return Invariant($"key at index {EnumerableIndex}");
                    case ChildKind.DictionaryValue: return Invariant($"value at index {EnumerableIndex}");
                    case ChildKind.EnumerableElement: return Invariant($"element at index {EnumerableIndex}");
                    case ChildKind.Field: return $"field {PrettifyFieldName(FieldInfoMaybe?.Name)}";
                    default:
                        throw new MetaAssertException("unreachable");
                }
            }
        }

        public enum ChildKind
        {
            DictionaryKey,
            DictionaryValue,
            EnumerableElement,
            Field,
        }

        static bool TypeHasSensibleObjectIdentity(Type type)
        {
            // Reference-typed objects have sensible, persistent identities.
            // Values-typed objects usually don't, because they tend to get boxed.
            //
            // Note that a value-typed object might still have an identity
            // if it is held in boxed form and referred to via a reference
            // (e.g. `object` or an interface type), but for simplicity we
            // ignore this.
            //
            // Also, note the exception of System.Reflection.Pointer.
            // It is a class (reference) type, but is apparently a special
            // kind of thing into which pointer-typed fields get boxed into
            // when getting them with FieldInfo.GetValue.
            // We ignore its identity for the same reason we ignore value types'.
            return type.IsClass
                   && type != typeof(System.Reflection.Pointer);
        }

        /// <summary>
        /// Implementation of <see cref="Dump"/>, in a stateful class for convenience.
        /// The dumping is implemented as a breadth-first traversal of the object graph.
        /// </summary>
        class DumpImpl
        {
            FieldInfoCache _fieldInfoCache;

            /// <summary>
            /// Objects registered so far.
            /// Maps an object's identity to the "handle" assigned to the object.
            /// Handles are actually just indexes into <see cref="_objects"/>.
            /// </summary>
            Dictionary<ObjectIdentity, ObjectHandle> _objectHandles;

            /// <summary>
            /// The resulting list of objects, indexed by the object's "handle".
            /// During the operation of <see cref="Dump"/>, this acts as both the traversal queue
            /// and the so-far partial result list:
            /// - Entries up to (but excluding) <see cref="_nextObjectIndexToTraverse"/>
            ///   are result entries in their final form.
            /// - Entries starting at <see cref="_nextObjectIndexToTraverse"/> are the queue:
            ///   yet to be processed by the traversal, they're not in their final form: only
            ///   <see cref="ObjectEntry.Obj"/> is assigned, but <see cref="ObjectEntry.ChildrenMaybe"/>
            ///   is not.
            /// - The breadth-first traversal loop in <see cref="Dump"/> picks the next item from
            ///   the queue (the one at <see cref="_nextObjectIndexToTraverse"/>) and resolves and
            ///   assigns its <see cref="ObjectEntry.ChildrenMaybe"/>. During the resolving of its
            ///   children, new items may be added to the end of this list (without their own
            ///   children resolved yet).
            /// </summary>
            List<ObjectEntry> _objects;
            int _nextObjectIndexToTraverse = 0;

            public DumpResult Dump(object root, DumpOptions options)
            {
                if (options == null)
                    throw new ArgumentNullException(nameof(options));

                _fieldInfoCache = options.FieldInfoCache ?? new FieldInfoCache();

                _objectHandles = new Dictionary<ObjectIdentity, ObjectHandle>(capacity: options.ObjectCollectionInitialCapacity);
                _objects = new List<ObjectEntry>(capacity: options.ObjectCollectionInitialCapacity);

                // Start traversal from the root.
                ObjectHandle rootHandle = RegisterObject(root);
                MetaDebug.Assert(rootHandle == 0, "Expected object list to start at 0");

                // Traverse objects in the order they have been encountered and added into _objects.
                while (_nextObjectIndexToTraverse != _objects.Count)
                {
                    ObjectEntry currentEntry = _objects[_nextObjectIndexToTraverse];
                    object currentObject = currentEntry.Obj;

                    if (_objects.Count > options.ObjectCountSafetyLimit)
                        throw new InvalidOperationException($"Object graph dump {nameof(options.ObjectCountSafetyLimit)} of {options.ObjectCountSafetyLimit} exceeded");

                    // Visit the object's children - when new objects are seen among the children's
                    // referred objects, they're added to the end of _objects (and will be processed
                    // later by this same loop we're in now).
                    List<ChildEntry> children = TryVisitChildren(currentObject);

                    // Finalize the current object by assigning its children.
                    // (Assigns a new ObjectEntry struct in _objects because can't in-place mutate a list element.)
                    _objects[_nextObjectIndexToTraverse] = new ObjectEntry(currentObject, children);
                    _nextObjectIndexToTraverse++;
                }

                return new DumpResult(_objects);
            }

            /// <summary>
            /// If the object hasn't been seen yet, this registers a new handle for it,
            /// adds the object to the traversal queue, and returns the handle.
            /// If the object was already seen before, this just returns the same handle as before.
            /// </summary>
            ObjectHandle RegisterObject(object obj)
            {
                ObjectIdentity objIdentity = new ObjectIdentity(obj);
                if (_objectHandles.TryGetValue(objIdentity, out ObjectHandle existingHandle))
                    return existingHandle;
                ObjectHandle newHandle = (ObjectHandle)_objects.Count;
                // \note Children not assigned yet. That'll be done when this object is later
                //       processed in the loop in Dump.
                _objects.Add(new ObjectEntry(obj, childrenMaybe: null));
                _objectHandles.Add(objIdentity, newHandle);
                return newHandle;
            }

            /// <summary>
            /// Visits the given object's children, registering the object each child refers to.
            /// Returns the list of children.
            /// </summary>
            List<ChildEntry> TryVisitChildren(object obj)
            {
                if (obj == null
                 || _scalarTypes.Contains(obj.GetType())
                 || obj is System.Reflection.Pointer)
                {
                    // Null and scalars objects don't have children.
                    //
                    // Pointer we just basically ignore because but it's a bit fussy to deal with
                    // and we don't actually care about its value (probably wouldn't play nice
                    // in CompareDumpResults anyway due to GC changing pointers?).
                    return null;
                }
                else if (obj is IGameConfigLibraryEntry gameConfigLibrary)
                {
                    return VisitGameConfigLibraryChildren(gameConfigLibrary);
                }
                else if (obj is IEnumerable enumerable)
                {
                    if (enumerable is IDictionary dictionary)
                        return VisitDictionaryChildren(dictionary);
                    else
                        return TryVisitEnumerableChildren(enumerable);
                }
                else
                {
                    return VisitByMembersChildren(obj);
                }
            }

            /// <summary>
            /// We treat config libraries basically the same as dictionaries.
            /// The children are the keys and values.
            /// \todo This is ad hoc support for config libraries because the use case at hand
            ///       for the whole ObjectGraphDump is checking for changes in config items.
            ///       Could implement support for registering custom visitation hooks from the outside.
            /// </summary>
            List<ChildEntry> VisitGameConfigLibraryChildren(IGameConfigLibraryEntry library)
            {
                List<ChildEntry> children = new List<ChildEntry>(capacity: 2*library.Count);

                int index = 0;
                foreach ((object key, object value) in library.EnumerateAll())
                {
                    children.Add(new ChildEntry(
                        ChildId.DictionaryKey(index),
                        RegisterObject(key)));
                    children.Add(new ChildEntry(
                        ChildId.DictionaryValue(index),
                        RegisterObject(value)));
                    index++;
                }

                return children;
            }

            /// <summary>
            /// A dictionary's children are its keys and values.
            /// </summary>
            List<ChildEntry> VisitDictionaryChildren(IDictionary dictionary)
            {
                List<ChildEntry> children = new List<ChildEntry>(capacity: 2*dictionary.Count);

                int index = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    children.Add(new ChildEntry(
                        ChildId.DictionaryKey(index),
                        RegisterObject(entry.Key)));
                    children.Add(new ChildEntry(
                        ChildId.DictionaryValue(index),
                        RegisterObject(entry.Value)));
                    index++;
                }

                return children;
            }

            /// <summary>
            /// An enumerable's children are its elements.
            /// This returns null if GetEnumerator throws, which it can
            /// for example for the default value of <see cref="ArraySegment{T}"/>.
            /// </summary>
            List<ChildEntry> TryVisitEnumerableChildren(IEnumerable enumerable)
            {
                IEnumerator enumerator;
                try
                {
                    enumerator = enumerable.GetEnumerator();
                }
                catch
                {
                    return null;
                }

                List<ChildEntry> children = new List<ChildEntry>();

                int index = 0;
                while (enumerator.MoveNext())
                {
                    children.Add(new ChildEntry(
                        ChildId.EnumerableElement(index),
                        RegisterObject(enumerator.Current)));
                    index++;
                }

                return children;
            }

            /// <summary>
            /// A general by-members object's children are the fields of the object
            /// (including the backing fields of auto-implemented properties).
            /// </summary>
            List<ChildEntry> VisitByMembersChildren(object obj)
            {
                Type objType = obj.GetType();
                FieldInfo[] fields = _fieldInfoCache.GetFieldsWithCache(objType);
                List<ChildEntry> children = new List<ChildEntry>(capacity: fields.Length);

                foreach (FieldInfo field in fields)
                {
                    Type fieldType = field.FieldType;
                    if (fieldType.IsValueType && fieldType == objType)
                    {
                        throw new MetaAssertException(
                            $"Field {field.ToMemberWithGenericDeclaringTypeString()} is value-typed and has the same type as containing object! " +
                            $"This is only expected for builtin value types, which are meant to be handled separately based on {nameof(_scalarTypes)}.");
                    }
                    else
                    {
                        object fieldValue = field.GetValue(obj);
                        children.Add(new ChildEntry(
                            ChildId.Field(field),
                            RegisterObject(fieldValue)));
                    }
                }

                return children;
            }
        }

        public class FieldInfoCache
        {
            Dictionary<Type, FieldInfo[]> _typeFieldsCache = new Dictionary<Type, FieldInfo[]>();

            public FieldInfo[] GetFieldsWithCache(Type type)
            {
                if (_typeFieldsCache.TryGetValue(type, out FieldInfo[] cachedFields))
                    return cachedFields;

                // \note This implementation is modified copypaste from TypeExtensions.EnumerateInstanceFieldsInUnspecifiedOrder.
                //       The difference is that this includes fields with CompilerGeneratedAttribute.

                IEnumerable<FieldInfo> fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                // Need to get ancestors' private fields separately.
                for (Type ancestor = type.BaseType; ancestor != null; ancestor = ancestor.BaseType)
                {
                    IEnumerable<FieldInfo> ancestorPrivateFields =
                        ancestor.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic)
                        .Where(field => field.IsPrivate);

                    fields = fields.Concat(ancestorPrivateFields);
                }

                fields = fields.Where(f => f.GetCustomAttribute<IgnoreAttribute>() == null);

                FieldInfo[] fieldsArray = fields.ToArray();
                _typeFieldsCache.Add(type, fieldsArray);
                return fieldsArray;
            }
        }

        /// <summary>
        /// Types which are handled as scalars, i.e. they don't have children
        /// but are compared by value. This should contain types which aren't
        /// enumerables or otherwise specially handled, and also cannot be
        /// treated based on their fields.
        /// </summary>
        static readonly HashSet<Type> _scalarTypes = new HashSet<Type>
        {
            typeof(bool),
            typeof(sbyte),
            typeof(byte),
            typeof(char),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(nint),
            typeof(nuint),
            typeof(float),
            typeof(double),
            typeof(decimal),

            typeof(string),
        };
    }
}
