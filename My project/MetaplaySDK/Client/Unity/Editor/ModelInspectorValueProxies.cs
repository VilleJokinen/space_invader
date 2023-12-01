// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using UnityEngine;
using static Metaplay.Client.Unity.ModelInspectorWindow;
using static System.FormattableString;

namespace Metaplay.Client.Unity
{
    /// <summary>
    /// Proxy class for objects with presentable values. Used to represent serializable Metaplay Models in <see cref="ModelInspectorWindow"/>.
    /// Represents an element in a tree structure created through examining an object's properties and fields with reflection.
    /// </summary>
    public abstract class ValueProxyBase
    {
        // Generate unique ids for proxies
        static int _nextUniqueId;

        static int GenerateUniqueId()
        {
            return _nextUniqueId++;
        }

        public int                  Id          { get; private set; }
        public int                  Depth       { get; private set; }
        public Type                 StaticType  { get; private set; }
        public List<ValueProxyBase> Children    { get; private set; }
        public bool                 HasChildren => Children != null && Children.Count > 0;
        /// <summary>
        /// Name text shown in the name column of the UI
        /// </summary>
        public string NameText { get; set; }
        /// <summary>
        /// Value text shown in the value column of the UI
        /// </summary>
        public string ValueText { get;         set; }
        public bool           IsLocal   { get; set; }
        public bool           HasSetter { get; set; } = true;
        public ValueProxyBase Parent;
        public Type           ValueType => _cachedValueObject != null ? _cachedValueObject.GetType() : StaticType;

        protected string _cachedName;
        protected object _cachedValueObject;
        protected bool   _appendTypeToName;
        protected Type   _cachedValueType;

        protected ValueProxyBase(int depth, Type staticType, ValueProxyBase parent)
        {
            this.Id    = GenerateUniqueId();
            Depth      = depth;
            StaticType = staticType;
            Children   = new List<ValueProxyBase>();
            Parent     = parent;

            NameText  = "";
            ValueText = "";
        }

        public ValueProxyBase FindChild(int id, bool recursive)
        {
            if (this.Id == id)
                return this;

            foreach (ValueProxyBase child in Children)
            {
                if (recursive)
                {
                    ValueProxyBase found = child.FindChild(id, true);
                    if (found != null)
                        return found;
                }
                else if (child.Id == id)
                    return child;
            }

            return null;
        }

        /// <summary>
        /// Adds proxy to <see cref="Children"/> if not null.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddChild(ValueProxyBase child)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));

            Children.Add(child);
        }

        /// <summary>
        /// Ensures that count of <see cref="Children"/> is <see cref="numChildren"/> by adding or removing children.
        /// </summary>
        /// <returns>Number of children added. If negative, number of children removed.</returns>
        protected int EnsureChildCount(int numChildren, Type childType)
        {
            int addedChildrenCount = 0;
            // Remove extra children
            if (Children.Count > numChildren)
            {
                while (Children.Count > numChildren)
                {
                    Children.RemoveAt(Children.Count - 1);
                    addedChildrenCount--;
                }

                return addedChildrenCount;
            }
            else if (Children.Count < numChildren)
            {
                // Add missing children
                while (Children.Count < numChildren)
                {
                    addedChildrenCount++;
                    AddChild(CreateProxy(Depth + 1, childType, this));
                }

                return addedChildrenCount;
            }

            return 0;
        }

        /// <summary>
        /// Updates properties of this proxy using reflection. If <see cref="recursive"/> is true,
        /// calls <see cref="Update"/> recursively in each element of <see cref="Children"/>.
        /// </summary>
        /// <param name="recursive">If true, calls <see cref="Update"/> recursively in each
        /// element of <see cref="Children"/>.</param>
        public abstract void Update(
            string name,
            object value,
            bool recursive,
            bool isRoot,
            bool forceRefreshTexts,
            bool appendTypeToName,
            ModelInspectorArgs args);

        /// <summary>
        /// Updates <see cref="NameText"/> and <see cref="ValueText"/> using cached references set in <see cref="Update"/>.
        /// </summary>
        /// <param name="recursive">If true, calls <see cref="Update"/> recursively in each
        /// element of <see cref="Children"/>.</param>
        public abstract void RefreshTexts(
            bool recursive,
            bool isRoot,
            ModelInspectorArgs args);

        /// <summary>
        /// Updates <see cref="ValueText"/> to show a preview of child elements in <see cref="Children"/>.
        /// </summary>
        public virtual void UpdateValueTextToPreviewChildren(bool isRoot, ModelInspectorArgs args)
        {
            if (Children.Count > 0)
            {
                int           previewedChildren = 0;
                StringBuilder sb                = new StringBuilder("{ ");
                for (int i = 0; i < Children.Count && previewedChildren < ModelInspectorWindow.ValuePreviewChildCount; i++)
                {
                    if (isRoot && this is ObjectValueProxy objectValueProxy && objectValueProxy.IsChildBaseClassMember(Children[i]) && !args.ShowBaseClassMembers)
                        continue;
                    if (!Children[i].HasSetter && !args.ShowReadOnlyProperties)
                        continue;

                    if (previewedChildren > 0)
                        sb.Append(", ");
                    if (Children[i] is SimpleValueProxy)
                        sb.Append(Children[i].UpdateValueText(args));
                    else
                        sb.Append(Children[i].UpdateNameText());
                    previewedChildren++;
                }

                if (Children.Count > ModelInspectorWindow.ValuePreviewChildCount)
                    sb.Append(", ...");
                sb.Append(" }");
                ValueText = sb.ToString();
            }
            else
            {
                ValueText = "";
            }
        }

        /// <summary>
        /// Updates and returns <see cref="NameText"/>. Uses cached name.
        /// </summary>
        public abstract string UpdateNameText();

        /// <summary>
        /// Updates and returns <see cref="ValueText"/>. Uses cached value object to update the text.
        /// </summary>
        public abstract string UpdateValueText(ModelInspectorArgs args);

        /// <summary>
        /// Returns the child proxy at the given index of <see cref="Children"/> if available.
        /// Otherwise creates a new child proxy using the given type and returns it.
        /// </summary>
        protected ValueProxyBase GetOrCreateChildProxy(int childNdx, Type staticType)
        {
            if (childNdx < Children.Count)
            {
                return Children[childNdx];
            }
            else
            {
                ValueProxyBase child = CreateProxy(Depth + 1, staticType, this);
                AddChild(child);
                return child;
            }
        }

        /// <summary>
        /// Creates a new proxy based on given type using inheriting classes of <see cref="ValueProxyBase"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static ValueProxyBase CreateProxy(int depth, Type staticType, ValueProxyBase parent)
        {
            if (staticType == null)
                throw new ArgumentNullException(nameof(staticType));

            // Cap depth to avoid infinite loops
            if (depth >= ModelInspectorWindow.MaximumTreeDepth)
                throw new InvalidOperationException($"Model Inspector hierarchy maximum depth exceeded, bailing out!");

            if (staticType == typeof(object))
                return new UnhandledTypeValueProxy(depth, staticType, parent);
            else if (staticType.IsDerivedFrom<Delegate>())
                return new UnhandledTypeValueProxy(depth, staticType, parent);
            else if (staticType.IsPrimitive || staticType == typeof(string) || staticType == typeof(EntityId))
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType.IsEnum)
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType.ImplementsInterface<IStringId>())
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType.ImplementsInterface<IMetaRef>())
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType.ImplementsInterface<IGameConfigData>())
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType == typeof(MetaTime))
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType.ImplementsInterface<IDynamicEnum>())
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType == typeof(F32) || staticType == typeof(F64))
                return new SimpleValueProxy(depth, staticType, parent);
            else if (staticType == typeof(F32Vec2) || staticType == typeof(F32Vec3)
                     || staticType == typeof(F64Vec2) || staticType == typeof(F64Vec3))
                return new FixedPointVectorProxy(depth, staticType, parent);
            else if (staticType.IsCollection())
                return new CollectionValueProxy(depth, staticType, parent);
            else if (staticType.IsEnumerable())
                return new CollectionValueProxy(depth, staticType, parent);
            else if (staticType.IsClass || staticType.IsInterface || (staticType.IsValueType && !staticType.IsPrimitive))
                return new ObjectValueProxy(depth, staticType, parent);
            else
                return new UnhandledTypeValueProxy(depth, staticType, parent);
        }

        /// <summary>
        /// Gets and adds to the given list recursively all parents above this proxy.
        /// </summary>
        public void AddParentsToListRecursive(List<ValueProxyBase> parents)
        {
            if (Parent != null && !parents.Contains(Parent))
            {
                parents.Add(Parent);
                Parent.AddParentsToListRecursive(parents);
            }
        }

        /// <summary>
        /// Checks recursively if <see cref="obj"/> is referenced in the parent of this <see cref="ValueProxyBase"/> or in any ancestor in the hierarchy.
        /// </summary>
        public bool IsRecursiveObjectReference()
        {
            if (Parent == null)
                return false;

            return Parent.IsObjectReferencedInParentRecursive(_cachedValueObject);
        }

        private bool IsObjectReferencedInParentRecursive(object obj)
        {
            // Continues to the next parent until a reference is found (returns true) or has reached root and its parent is null (returns false).
            if (obj == null)
                return false;
            if (ReferenceEquals(_cachedValueObject, obj))
                return true;

            return Parent != null && Parent.IsObjectReferencedInParentRecursive(obj);
        }
    }

    /// <summary>
    /// Proxy that represents a simple value (such as int, float, string...) or other objects that can be presented as simple values
    /// and have no child fields or properties that need to be presented. Proxies of this type never have child proxies.
    /// </summary>
    public class SimpleValueProxy : ValueProxyBase
    {
        Func<object, string> _cachedValueStringGetter;

        public SimpleValueProxy(int depth, Type staticType, ValueProxyBase parent) : base(depth, staticType, parent)
        {
            SetValueStringGetter();
        }

        public override void Update(
            string name,
            object value,
            bool recursive,
            bool isRoot,
            bool forceRefreshTexts,
            bool appendTypeToName,
            ModelInspectorArgs args)
        {
            _appendTypeToName  = appendTypeToName;
            _cachedValueObject = value;
            _cachedName        = name;
            _cachedValueType   = value?.GetType();
            // Refresh texts only if this item is visible
            if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                RefreshTexts(recursive, false, args);
        }

        public override void RefreshTexts(
            bool recursive,
            bool isRoot,
            ModelInspectorArgs args)
        {
            UpdateNameText();
            UpdateValueText(args);
        }

        public override string UpdateNameText()
        {
            NameText = _cachedName;
            if (_appendTypeToName)
                NameText += _cachedValueType != null ? _cachedValueType.ToGenericTypeString() : StaticType.ToGenericTypeString();
            return NameText;
        }

        public override string UpdateValueText(ModelInspectorArgs args)
        {
            ValueText = GetValueString(_cachedName, _cachedValueObject);
            return ValueText;
        }

        string GetValueString(string name, object value)
        {
            if (value == null)
                return "null";
            else if (_cachedValueStringGetter != null)
                return _cachedValueStringGetter(value);

            try
            {
                return (value != null) ? value.ToString() : "null";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to get property value of {name}: {ex}");
                return "<Failed to get value>";
            }
        }

        void SetValueStringGetter()
        {
            if (StaticType.IsPrimitive || StaticType == typeof(string) || StaticType == typeof(EntityId))
                _cachedValueStringGetter = value => value.ToString();
            else if (StaticType.ImplementsInterface<IStringId>())
                _cachedValueStringGetter = value => ((IStringId)value).Value;
            else if (StaticType.ImplementsInterface<IMetaRef>())
                _cachedValueStringGetter = value => ((IMetaRef)value).KeyObject.ToString();
            else if (StaticType.ImplementsInterface<IGameConfigData>())
            {
                // Find proper IGameConfigData<T>
                foreach (Type interfaceType in StaticType.GetInterfaces())
                {
                    if (!interfaceType.IsGenericType)
                        continue;
                    if (interfaceType.GetGenericTypeDefinition() != typeof(IGameConfigKey<>))
                        continue;

                    _cachedValueStringGetter = value =>
                    {
                        object configKey = interfaceType.GetProperty(nameof(IGameConfigKey<string>.ConfigKey)).GetValue(value);
                        return configKey.ToString();
                    };
                    return;
                }

                _cachedValueStringGetter = value => "Unrecognized GameConfigData";
            }
            else if (StaticType == typeof(MetaTime))
                _cachedValueStringGetter = value => ((MetaTime)value).ToString();
            else if (StaticType.ImplementsInterface<IDynamicEnum>())
                _cachedValueStringGetter = value => ((IDynamicEnum)value).ToString();
            else if (StaticType.IsEnum)
                _cachedValueStringGetter = value => Enum.GetName(StaticType, value);
            else if (StaticType == typeof(F32) || StaticType == typeof(F32Vec2) || StaticType == typeof(F32Vec3)
                     || StaticType == typeof(F64) || StaticType == typeof(F64Vec2) || StaticType == typeof(F64Vec3))
                _cachedValueStringGetter = value => value.ToString();
        }
    }

    /// <summary>
    /// Proxy that represents any finite collection (such as Array, List, Dictionary...). Members of the collection
    /// become child proxies to this proxy. Does not handle <see cref="IEnumerable"/> objects that are not collections,
    /// they are handled by <see cref="ObjectValueProxy"/> instead.
    /// </summary>
    public class CollectionValueProxy : ValueProxyBase
    {
        const int   MaxNumElements = 100;

        IEnumerable _enumerableCollection;
        Type        _childValueType;
        int         _collectionCount;
        bool        _recursiveReference = false;

        public CollectionValueProxy(int depth, Type staticType, ValueProxyBase parent) : base(depth, staticType, parent) { }

        public override void Update(
            string name,
            object value,
            bool recursive,
            bool isRoot,
            bool forceRefreshTexts,
            bool appendTypeToName,
            ModelInspectorArgs args)
        {
            // Collections will update regardless of caching as we cannot know if any children have been changed without going through the whole collection
            // So we update the whole collection

            bool valueChanged  = _cachedValueObject != value;
            _appendTypeToName  = appendTypeToName;
            _cachedValueObject = value;
            _cachedName        = name;
            _cachedValueType   = value?.GetType();
            _enumerableCollection = (IEnumerable)value;

            if (_enumerableCollection == null)
            {
                if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                    RefreshTexts(false, false, args);

                if (HasChildren)
                    Children.Clear();

                return;
            }

            // Get collection count. Limit to MaxNumElements + 1. The +1 is for
            // the pseudo-element that shows element limit was reached
            if (_enumerableCollection is ICollection collection)
            {
                _collectionCount = Math.Min(collection.Count, MaxNumElements + 1);
            }
            else
            {
                // Some collection types, such as HashSet<T>, cannot be cast to ICollection, only to the generic type.
                // For these types, getting the count is easiest with a simple loop through, as we know it is a finite collection.
                _collectionCount = 0;
                foreach (object o in _enumerableCollection)
                {
                    _collectionCount++;
                    if (_collectionCount >= MaxNumElements + 1)
                        break;
                }
            }

            // Check for recursive reference
            if (valueChanged)
            {
                // Don't update children of this object, instead label this object as a recursive reference
                _recursiveReference = IsRecursiveObjectReference();
            }

            if (_recursiveReference)
            {
                RefreshTexts(false, false, args);

                if (HasChildren)
                    Children.Clear();

                return;
            }

            // Update child type, and children if recursive
            if (_collectionCount > 0)
            {
                // Add missing children
                if (_enumerableCollection is IDictionary dictionary)
                {
                    (Type keyType, Type valueType) = StaticType.GetDictionaryKeyAndValueTypes();
                    _childValueType                = valueType;
                    EnsureChildCount(_collectionCount, valueType);

                    if (recursive)
                    {
                        int childNdx = 0;
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            if (childNdx == MaxNumElements)
                            {
                                Children[childNdx].Update(
                                    Invariant($"[limit of {MaxNumElements} entries reached]"),
                                    null,
                                    false,
                                    false,
                                    forceRefreshTexts,
                                    false,
                                    args);
                                break;
                            }

                            Children[childNdx].Update(
                                entry.Key.ToString(),
                                entry.Value,
                                true,
                                false,
                                forceRefreshTexts,
                                false,
                                args);
                            childNdx++;
                        }
                    }
                }
                else
                {
                    _childValueType = _cachedValueType.GetEnumerableElementType();
                    EnsureChildCount(_collectionCount, _childValueType);

                    if (recursive)
                    {
                        int childNdx = 0;
                        foreach (object elem in _enumerableCollection)
                        {
                            if (childNdx == MaxNumElements)
                            {
                                Children[childNdx].Update(
                                    Invariant($"[limit of {MaxNumElements} entries reached]"),
                                    null,
                                    false,
                                    false,
                                    forceRefreshTexts,
                                    false,
                                    args);
                                break;
                            }

                            Children[childNdx].Update(
                                $"[{childNdx}]: ",
                                elem,
                                true,
                                false,
                                forceRefreshTexts,
                                true,
                                args);
                            childNdx++;
                        }
                    }
                }
            }
            else // Collection count is 0
            {
                EnsureChildCount(0, _childValueType);
            }

            if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                RefreshTexts(false, false, args);
        }

        public override void RefreshTexts(
            bool recursive,
            bool isRoot,
            ModelInspectorArgs args)
        {
            UpdateNameText();
            UpdateValueText(args);

            if (_enumerableCollection == null)
            {
                return;
            }

            // Refresh child texts recursively
            if (recursive && !_recursiveReference)
            {
                if (_enumerableCollection is IDictionary dictionary)
                {
                    int childNdx = 0;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        Children[childNdx].RefreshTexts(true, false, args);
                        childNdx++;
                    }
                }
                else
                {
                    for (int childNdx = 0; childNdx < Children.Count; childNdx++)
                    {
                        Children[childNdx].RefreshTexts(true, false, args);
                    }
                }
            }
        }

        public override string UpdateNameText()
        {
            if (_enumerableCollection == null)
            {
                if (_appendTypeToName)
                    NameText = $"{_cachedName} {(_cachedValueType != null ? _cachedValueType.ToGenericTypeString() : StaticType.ToGenericTypeString())} [null]";
                else
                    NameText = $"{_cachedName} [null]";
                return NameText;
            }

            if (_appendTypeToName)
                NameText = $"{_cachedName} {(_cachedValueType != null ? _cachedValueType.ToGenericTypeString() : StaticType.ToGenericTypeString())} [{_collectionCount}]";
            else
                NameText = $"{_cachedName} [{_collectionCount}]";
            return NameText;
        }

        public override string UpdateValueText(ModelInspectorArgs args)
        {
            if (_enumerableCollection == null)
            {
                ValueText = "null";
                return ValueText;
            }

            if (_recursiveReference)
            {
                ValueText = "Recursive object reference: child members not shown.";
                return ValueText;
            }

            // Update value text to show preview of children
            UpdateValueTextToPreviewChildren(false, args);
            return ValueText;
        }
    }

    /// <summary>
    /// Proxy that represents any object that is not handled by <see cref="SimpleValueProxy"/> or <see cref="CollectionValueProxy"/>.
    /// Properties and fields of the object become child proxies to this proxy.
    ///
    /// <para>
    /// If an object could be better presented as a simple value with no child proxies,
    /// consider adding handling for the object type in <see cref="ValueProxyBase.CreateProxy"/> and in <see cref="SimpleValueProxy.SetValueStringGetter"/>.
    /// </para>
    /// </summary>
    public class ObjectValueProxy : ValueProxyBase
    {
        public List<ValueProxyBase>      BaseMembers  { get; set; }
        public List<ValueProxyBase>      LocalMembers { get; set; }
        Dictionary<PropertyInfo, object> _cachedPropertyInfos;
        Dictionary<FieldInfo, object>    _cachedFieldInfos;
        bool                             _recursiveReference = false;

        public ObjectValueProxy(int depth, Type staticType, ValueProxyBase parent) : base(depth, staticType, parent) { }

        public override void Update(
            string name,
            object value,
            bool recursive,
            bool isRoot,
            bool forceRefreshTexts,
            bool appendTypeToName,
            ModelInspectorArgs args)
        {
            bool valueChanged     = _cachedValueObject != value;
            bool valueTypeChanged = _cachedValueType != value?.GetType();
            bool cachedValueObjectWasNull = _cachedValueObject == null;
            bool wasRecursiveReference = _recursiveReference;

            _appendTypeToName  = appendTypeToName;
            _cachedValueObject = value;
            _cachedName        = name;
            _cachedValueType   = value?.GetType();

            if (value is null)
            {
                if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                    RefreshTexts(false, isRoot, args);

                if (HasChildren)
                    Children.Clear();

                return;
            }

            // Check for recursive reference
            if (valueChanged)
            {
                // Don't update children of this object, instead label this object as a recursive reference
                _recursiveReference = IsRecursiveObjectReference();
            }

            if (_recursiveReference)
            {
                RefreshTexts(false, isRoot, args);
                
                if (HasChildren)
                    Children.Clear();

                return;
            }

            // If object type has not changed, update children based on cached info
            if (_cachedValueObject != null && !valueTypeChanged && !cachedValueObjectWasNull && !wasRecursiveReference)
            {
                // Refresh children recursively
                if (recursive && !_recursiveReference)
                {
                    int childNdx2 = 0;
                    foreach ((PropertyInfo propInfo, object cachedPropValue) in _cachedPropertyInfos)
                    {
                        if (childNdx2 >= Children.Count)
                            throw new IndexOutOfRangeException("Inconsistent child count.");

                        // Update child with a fresh value if child is visible or part of the value preview of this item
                        if (args.VisibleProxies.Contains(Children[childNdx2].Id) || forceRefreshTexts
                            || (args.VisibleProxies.Contains(Id) && childNdx2 < ModelInspectorWindow.ValuePreviewChildCount))
                            Children[childNdx2].Update(
                                propInfo.Name,
                                propInfo.GetValue(value),
                                true,
                                false,
                                forceRefreshTexts,
                                false,
                                args);
                        else
                            Children[childNdx2].Update(
                                propInfo.Name,
                                cachedPropValue,
                                true,
                                false,
                                forceRefreshTexts,
                                false,
                                args);
                        childNdx2++;
                    }

                    foreach ((FieldInfo fieldInfo, object cachedFieldValue) in _cachedFieldInfos)
                    {
                        if (childNdx2 >= Children.Count)
                            throw new IndexOutOfRangeException("Inconsistent child count.");

                        // Update child with a fresh value if child is visible or part of the value preview of this item
                        if (args.VisibleProxies.Contains(Children[childNdx2].Id) || forceRefreshTexts
                            || (args.VisibleProxies.Contains(Id) && childNdx2 < ModelInspectorWindow.ValuePreviewChildCount))
                            Children[childNdx2].Update(
                                fieldInfo.Name,
                                fieldInfo.GetValue(value),
                                true,
                                false,
                                forceRefreshTexts,
                                false,
                                args);
                        else
                            Children[childNdx2].Update(
                                fieldInfo.Name,
                                cachedFieldValue,
                                true,
                                false,
                                forceRefreshTexts,
                                false,
                                args);
                        childNdx2++;
                    }
                }

                // Refresh texts only if this item is visible
                if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                    RefreshTexts(false, isRoot, args);

                return;
            }

            // Full reflection based update, getting properties and fields from the object's type.
            // Children of this object, properties and fields, are sorted into two lists, base class members and local members.
            if (BaseMembers != null)
                BaseMembers.Clear();
            else
                BaseMembers = new List<ValueProxyBase>();

            if (LocalMembers != null)
                LocalMembers.Clear();
            else
                LocalMembers = new List<ValueProxyBase>();

            PropertyInfo[] propertyInfos = _cachedValueType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_cachedPropertyInfos != null)
                _cachedPropertyInfos.Clear();
            else
                _cachedPropertyInfos = new Dictionary<PropertyInfo, object>(propertyInfos.Length);

            int childNdx = 0;
            foreach (PropertyInfo propInfo in propertyInfos)
            {
                // Skip ignored members
                if (propInfo.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                    continue;

                // Skip indexed properties
                if (propInfo.GetIndexParameters().Length > 0)
                    continue;

                _cachedPropertyInfos.Add(propInfo, propInfo.GetValue(value));

                // Tag read-only properties
                bool hasSetter = propInfo.GetSetValueOnDeclaringType() != null;

                // Tag base class members
                bool isLocal = propInfo.DeclaringType == _cachedValueType;

                // Add property as child
                Type           propType = propInfo.PropertyType;
                ValueProxyBase child    = GetOrCreateChildProxy(childNdx++, propType);
                child.HasSetter = hasSetter;
                child.IsLocal   = isLocal;
                if (isLocal)
                    LocalMembers.Add(child);
                else
                    BaseMembers.Add(child);

                // Recursively update child
                if (recursive)
                    child.Update(
                        propInfo.Name,
                        _cachedPropertyInfos[propInfo],
                        true,
                        false,
                        forceRefreshTexts,
                        false,
                        args);
            }

            FieldInfo[] fieldInfos = _cachedValueType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_cachedFieldInfos != null)
                _cachedFieldInfos.Clear();
            else
                _cachedFieldInfos = new Dictionary<FieldInfo, object>(fieldInfos.Length);

            foreach (FieldInfo fieldInfo in fieldInfos)
            {
                // Ignore backing fields
                if (fieldInfo.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                    continue;

                // Skip ignored members
                if (fieldInfo.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                    continue;

                _cachedFieldInfos.Add(fieldInfo, fieldInfo.GetValue(value));

                bool           isLocal   = fieldInfo.DeclaringType == _cachedValueType;
                Type           fieldType = fieldInfo.FieldType;
                ValueProxyBase child     = GetOrCreateChildProxy(childNdx++, fieldType);
                child.IsLocal = isLocal;
                if (isLocal)
                    LocalMembers.Add(child);
                else
                    BaseMembers.Add(child);

                // Recursively update child
                if (recursive)
                    child.Update(
                        fieldInfo.Name,
                        _cachedFieldInfos[fieldInfo],
                        true,
                        false,
                        forceRefreshTexts,
                        false,
                        args);
            }

            if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                RefreshTexts(false, isRoot, args);
        }

        public override void RefreshTexts(
            bool recursive,
            bool isRoot,
            ModelInspectorArgs args)
        {
            UpdateNameText();
            UpdateValueText(args);

            // Refresh child texts recursively
            if (recursive && _cachedValueObject != null && !_recursiveReference)
            {
                int childNdx = 0;
                foreach ((PropertyInfo propInfo, object propValue) in _cachedPropertyInfos)
                {
                    if (childNdx >= Children.Count)
                        throw new IndexOutOfRangeException("Inconsistent child count.");

                    Children[childNdx].RefreshTexts(recursive, false, args);
                    childNdx++;
                }

                foreach ((FieldInfo fieldInfo, object fieldValue) in _cachedFieldInfos)
                {
                    if (childNdx >= Children.Count)
                        throw new IndexOutOfRangeException("Inconsistent child count.");

                    Children[childNdx].RefreshTexts(recursive, false, args);
                    childNdx++;
                }
            }
        }

        public override string UpdateNameText()
        {
            NameText = _cachedName;
            if (_appendTypeToName)
                NameText += _cachedValueType != null ? _cachedValueType.ToGenericTypeString() : StaticType.ToGenericTypeString();
            return NameText;
        }

        public override string UpdateValueText(ModelInspectorArgs args)
        {
            if (_cachedValueObject is null)
            {
                ValueText = "null";
                return ValueText;
            }

            if (_recursiveReference)
            {
                ValueText = "Recursive object reference: child members not shown.";
                return ValueText;
            }

            // Update value text to show preview of children
            UpdateValueTextToPreviewChildren(false, args);
            return ValueText;
        }

        public bool IsChildBaseClassMember(ValueProxyBase child)
        {
            if (BaseMembers != null && BaseMembers.Contains(child))
                return true;
            else
                return false;
        }
    }

    /// <summary>
    /// Proxy that represents objects of types that can not be handled by <see cref="ObjectValueProxy"/> such as <see cref="object"/>.
    /// Does not attempt to display a value, instead simply states that the type is unhandled.
    /// </summary>
    public class UnhandledTypeValueProxy : ValueProxyBase
    {
        public UnhandledTypeValueProxy(int depth, Type staticType, ValueProxyBase parent) : base(depth, staticType, parent) { }

        public override void Update(
            string name,
            object value,
            bool recursive,
            bool isRoot,
            bool forceRefreshTexts,
            bool appendTypeToName,
            ModelInspectorArgs args)
        {
            _cachedValueObject = value;
            _cachedName        = name;
            // Refresh texts only if this item is visible
            if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                RefreshTexts(recursive, false, args);
        }

        public override void RefreshTexts(
            bool recursive,
            bool isRoot,
            ModelInspectorArgs args)
        {
            NameText  = _cachedName;
            ValueText = "Unhandled type: " + StaticType.ToGenericTypeString();
        }

        public override string UpdateNameText()
        {
            NameText = _cachedName;
            return NameText;
        }

        public override string UpdateValueText(ModelInspectorArgs args)
        {
            ValueText = "Unhandled type: " + StaticType.ToGenericTypeString();
            return ValueText;
        }
    }

    public class FixedPointVectorProxy : ValueProxyBase
    {
        public FixedPointVectorProxy(int depth, Type staticType, ValueProxyBase parent) : base(depth, staticType, parent) { }

        public override void Update(
            string name,
            object value,
            bool recursive,
            bool isRoot,
            bool forceRefreshTexts,
            bool appendTypeToName,
            ModelInspectorArgs args)
        {
            _appendTypeToName  = appendTypeToName;
            _cachedValueObject = value;
            _cachedName        = name;
            _cachedValueType   = value?.GetType();

            if (value == null)
            {
                if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                    RefreshTexts(false, isRoot, args);

                if (HasChildren)
                    Children.Clear();

                return;
            }

            Type   propType  = typeof(int);
            string propXName = "";
            string propYName = "";
            string propZName = "";
            if (_cachedValueType == typeof(F32Vec2))
            {
                propType  = typeof(F32);
                propXName = nameof(F32Vec2.X);
                propYName = nameof(F32Vec2.Y);
            }
            else if (_cachedValueType == typeof(F64Vec2))
            {
                propType  = typeof(F64);
                propXName = nameof(F64Vec2.X);
                propYName = nameof(F64Vec2.Y);
            }
            else if (_cachedValueType == typeof(F32Vec3))
            {
                propType  = typeof(F32);
                propXName = nameof(F32Vec3.X);
                propYName = nameof(F32Vec3.Y);
                propZName = nameof(F32Vec3.Z);
            }
            else if (_cachedValueType == typeof(F64Vec3))
            {
                propType  = typeof(F64);
                propXName = nameof(F64Vec3.X);
                propYName = nameof(F64Vec3.Y);
                propZName = nameof(F64Vec3.Z);
            }

            // Get X and Y properties
            PropertyInfo propX = StaticType.GetProperty(propXName);
            PropertyInfo propY = StaticType.GetProperty(propYName);

            // Add properties as child proxies
            ValueProxyBase childX = GetOrCreateChildProxy(0, propType);
            childX.IsLocal   = false;
            childX.HasSetter = true;
            ValueProxyBase childY = GetOrCreateChildProxy(1, propType);
            childY.IsLocal   = false;
            childY.HasSetter = true;

            if (recursive)
            {
                childX.Update(propXName, propX.GetValue(value), true, isRoot, forceRefreshTexts, appendTypeToName, args);
                childY.Update(propYName, propY.GetValue(value), true, isRoot, forceRefreshTexts, appendTypeToName, args);
            }

            // Get Z property and create child proxy if Vec3 type
            if (_cachedValueType == typeof(F32Vec3) || _cachedValueType == typeof(F64Vec3))
            {
                ValueProxyBase childZ = GetOrCreateChildProxy(2, propType);
                childZ.IsLocal   = false;
                childZ.HasSetter = true;

                PropertyInfo propZ = StaticType.GetProperty(propZName);
                if (recursive)
                    childZ.Update(propZName, propZ.GetValue(value), true, isRoot, forceRefreshTexts, appendTypeToName, args);
            }

            if (args.VisibleProxies.Contains(Id) || forceRefreshTexts)
                RefreshTexts(false, isRoot, args);
        }

        public override void RefreshTexts(bool recursive, bool isRoot, ModelInspectorArgs args)
        {
            UpdateNameText();
            UpdateValueText(args);

            // Update children
            if (recursive)
            {
                foreach (ValueProxyBase child in Children)
                {
                    child.RefreshTexts(true, isRoot, args);
                }
            }
        }

        public override string UpdateNameText()
        {
            NameText = _cachedName;
            if (_appendTypeToName)
                NameText += _cachedValueType != null ? _cachedValueType.ToGenericTypeString() : StaticType.ToGenericTypeString();
            return NameText;
        }

        public override string UpdateValueText(ModelInspectorArgs args)
        {
            if (_cachedValueObject is null)
            {
                ValueText = "null";
                return ValueText;
            }

            // Update value text to show preview of children
            UpdateValueTextToPreviewChildren(false, args);
            return ValueText;
        }
    }
}
