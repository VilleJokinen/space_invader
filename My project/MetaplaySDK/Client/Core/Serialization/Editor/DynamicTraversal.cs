// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Utility for dynamic traversal of MetaSerializable objects.
    /// "Dynamic" in the sense that no code generation is involved (despite this piggy-backing on <see cref="TaggedSerializerRoslynGenerator"/>).
    /// <para>
    /// Traversal of the various kinds of serializable types is implemented in the classes implementing <see cref="TaggedSerializerRoslynGenerator.ITypeInfo"/>
    /// (and <see cref="TaggedSerializerRoslynGenerator.IMembersInfo"/>).
    /// It is intended that the traversal corresponds the MetaSerialization model in the sense that equivalent serialization
    /// could in principle be cleanly implemented using this traversal (however, this would be slower than the generated serializer).
    /// </para>
    /// <para>
    /// You can implement a subclass of <see cref="Visitor"/> and override the desired visitation hooks.
    /// See <see cref="PathVisitor"/> for example. <see cref="PathVisitor"/> is a convenient helper intermediate
    /// for your own visitor classes, as it maintains a (mostly human-readable) representation of the subobject path.
    /// </para>
    /// </summary>
    public static class DynamicTraversal
    {
        public class Resources
        {
            public static Resources Create(IEnumerable<MetaSerializableType> allTypes)
            {
                Dictionary<Type, TaggedSerializerRoslynGenerator.ITypeInfo> typeInfos = TaggedSerializerRoslynGenerator.CreateTypeInfos(allTypes);
                Dictionary<Type, TaggedSerializerRoslynGenerator.IMembersInfo> membersInfos = TaggedSerializerRoslynGenerator.CreateMembersInfos(allTypes).ToDictionary(m => m.Type);
                TaggedSerializerRoslynGenerator.GetMetaRefContainingTypes(typeInfos.Values, out OrderedSet<Type> metaRefContainingTypes, out OrderedSet<Type> metaRefByMembersContainingTypes);

                return new Resources(
                    typeTraversables: typeInfos.ToDictionary(kv => kv.Key, kv => (ITraversableTypeInfo)kv.Value),
                    typeMembersTraversables: membersInfos.ToDictionary(kv => kv.Key, kv => (ITraversableTypeInfo)kv.Value),
                    typeSpecs: allTypes.ToDictionary(spec => spec.Type),
                    metaRefContainingTypes: metaRefContainingTypes);
            }

            Dictionary<Type, ITraversableTypeInfo> _typeTraversables;
            Dictionary<Type, ITraversableTypeInfo> _typeMembersTraversables;
            Dictionary<Type, MetaSerializableType> _typeSpecs;
            OrderedSet<Type> _metaRefContainingTypes;

            public virtual ITraversableTypeInfo GetTraversableInfo(Type type) => _typeTraversables[type];
            public virtual ITraversableTypeInfo GetMembersTraversableInfo(Type type) => _typeMembersTraversables[type];
            public virtual MetaSerializableType GetTypeSpec(Type type) => _typeSpecs[type];
            public virtual bool TypeContainsMetaRefs(Type type) => _metaRefContainingTypes.Contains(type);

            public Resources(Dictionary<Type, ITraversableTypeInfo> typeTraversables, Dictionary<Type, ITraversableTypeInfo> typeMembersTraversables, Dictionary<Type, MetaSerializableType> typeSpecs, OrderedSet<Type> metaRefContainingTypes)
            {
                _typeTraversables = typeTraversables;
                _typeMembersTraversables = typeMembersTraversables;
                _typeSpecs = typeSpecs;
                _metaRefContainingTypes = metaRefContainingTypes;
            }
        }

        /// <summary>
        /// Dynamic traversal interface into a serializable type.
        /// Separated from <see cref="TaggedSerializerRoslynGenerator.ITypeInfo"/> and <see cref="TaggedSerializerRoslynGenerator.IMembersInfo"/>
        /// only for clarity.
        /// </summary>
        public interface ITraversableTypeInfo
        {
            /// <summary>
            /// Dynamically traverse the given object, calling <paramref name="visitor"/>'s visitation
            /// and sub-traversal hooks.
            /// </summary>
            void Traverse(object obj, IVisitor visitor);
        }

        public interface IVisitor
        {
            void Traverse(Type type, object obj);

            void TraverseMembers(Type type, object obj);

            void VisitSimple(Type type, object obj);
            void VisitNullablePrimitive(Type type, object obj);
            void VisitEnum(Type type, object obj);
            void VisitNullableEnum(Type type, object obj);
            void VisitStringId(Type type, object obj);
            void VisitDynamicEnum(Type type, object obj);
            void VisitGameConfigData(Type type, object obj);
            void VisitMetaRef(Type type, object obj);

            void BeginTraverseConcreteClass(Type type, object obj);
            void EndTraverseConcreteClass(Type type, object obj);

            void BeginTraverseStruct(Type type, object obj);
            void EndTraverseStruct(Type type, object obj);

            void BeginTraverseNullableStruct(Type type, object obj);
            void EndTraverseNullableStruct(Type type, object obj);

            void BeginTraverseAbstractClass(Type type, object obj);
            void BeginTraverseDerivedClass(Type type, object obj, Type derivedType);
            void EndTraverseDerivedClass(Type type, object obj, Type derivedType);
            void EndTraverseAbstractClass(Type type, object obj);

            void BeginTraverseGameConfigDataContent(Type type, object obj);
            void EndTraverseGameConfigDataContent(Type type, object obj);

            void BeginTraverseMember(Type type, object obj, MetaSerializableMember member, object memberObj);
            void EndTraverseMember(Type type, object obj, MetaSerializableMember member, object memberObj);

            void BeginTraverseValueCollection(Type type, IEnumerable enumerable);
            void BeginTraverseValueCollectionElement(Type type, IEnumerable enumerable, int index, Type elementType, object elem);
            void EndTraverseValueCollectionElement(Type type, IEnumerable enumerable, int index, Type elementType, object elem);
            void EndTraverseValueCollection(Type type, IEnumerable enumerable);

            void BeginTraverseKeyValueCollection(Type type, IDictionary dictionary);
            void BeginTraverseKeyValueCollectionKey(Type type, IDictionary dictionary, int index, Type keyType, object key);
            void EndTraverseKeyValueCollectionKey(Type type, IDictionary dictionary, int index, Type keyType, object key);
            void BeginTraverseKeyValueCollectionValue(Type type, IDictionary dictionary, int index, Type keyType, object key, Type valueType, object value);
            void EndTraverseKeyValueCollectionValue(Type type, IDictionary dictionary, int index, Type keyType, object key, Type valueType, object value);
            void EndTraverseKeyValueCollection(Type type, IDictionary dictionary);
        }

        public abstract class Visitor : IVisitor
        {
            protected Resources Resources { get; }

            public Visitor(Resources resources)
            {
                Resources = resources;
            }

            public virtual void Traverse(Type type, object obj)
            {
                ITraversableTypeInfo traversableInfo = Resources.GetTraversableInfo(type);
                traversableInfo.Traverse(obj, this);
            }

            public virtual void TraverseMembers(Type type, object obj)
            {
                ITraversableTypeInfo membersTraversableInfo = Resources.GetMembersTraversableInfo(type);
                membersTraversableInfo.Traverse(obj, this);
            }

            public virtual void VisitSimple(Type type, object obj) { }
            public virtual void VisitNullablePrimitive(Type type, object obj) { }
            public virtual void VisitEnum(Type type, object obj) { }
            public virtual void VisitNullableEnum(Type type, object obj) { }
            public virtual void VisitStringId(Type type, object obj) { }
            public virtual void VisitDynamicEnum(Type type, object obj) { }
            public virtual void VisitGameConfigData(Type type, object obj) { }
            public virtual void VisitMetaRef(Type type, object obj) { }

            public virtual void BeginTraverseConcreteClass(Type type, object obj) { }
            public virtual void EndTraverseConcreteClass(Type type, object obj) { }

            public virtual void BeginTraverseStruct(Type type, object obj) { }
            public virtual void EndTraverseStruct(Type type, object obj) { }

            public virtual void BeginTraverseNullableStruct(Type type, object obj) { }
            public virtual void EndTraverseNullableStruct(Type type, object obj) { }

            public virtual void BeginTraverseAbstractClass(Type type, object obj) { }
            public virtual void BeginTraverseDerivedClass(Type type, object obj, Type derivedType) { }
            public virtual void EndTraverseDerivedClass(Type type, object obj, Type derivedType) { }
            public virtual void EndTraverseAbstractClass(Type type, object obj) { }

            public virtual void BeginTraverseGameConfigDataContent(Type type, object obj) { }
            public virtual void EndTraverseGameConfigDataContent(Type type, object obj) { }

            public virtual void BeginTraverseMember(Type type, object obj, MetaSerializableMember member, object memberObj) { }
            public virtual void EndTraverseMember(Type type, object obj, MetaSerializableMember member, object memberObj) { }

            public virtual void BeginTraverseValueCollection(Type type, IEnumerable enumerable) { }
            public virtual void BeginTraverseValueCollectionElement(Type type, IEnumerable enumerable, int index, Type elementType, object elem) { }
            public virtual void EndTraverseValueCollectionElement(Type type, IEnumerable enumerable, int index, Type elementType, object elem) { }
            public virtual void EndTraverseValueCollection(Type type, IEnumerable enumerable) { }

            public virtual void BeginTraverseKeyValueCollection(Type type, IDictionary dictionary) { }
            public virtual void BeginTraverseKeyValueCollectionKey(Type type, IDictionary dictionary, int index, Type keyType, object key) { }
            public virtual void EndTraverseKeyValueCollectionKey(Type type, IDictionary dictionary, int index, Type keyType, object key) { }
            public virtual void BeginTraverseKeyValueCollectionValue(Type type, IDictionary dictionary, int index, Type keyType, object key, Type valueType, object value) { }
            public virtual void EndTraverseKeyValueCollectionValue(Type type, IDictionary dictionary, int index, Type keyType, object key, Type valueType, object value) { }
            public virtual void EndTraverseKeyValueCollection(Type type, IDictionary dictionary) { }
        }

        public class PathVisitor : Visitor
        {
            public PathVisitor(Resources resources) : base(resources)
            {
            }

            public override void BeginTraverseMember(Type type, object obj, MetaSerializableMember member, object memberObj)
            {
                base.BeginTraverseMember(type, obj, member, memberObj);

                PushPathElement(new PathElement($".{member.Name}"));
            }

            public override void EndTraverseMember(Type type, object obj, MetaSerializableMember member, object memberObj)
            {
                base.EndTraverseMember(type, obj, member, memberObj);

                PopPathElement();
            }

            public override void BeginTraverseValueCollectionElement(Type type, IEnumerable enumerable, int index, Type elementType, object elem)
            {
                base.BeginTraverseValueCollectionElement(type, enumerable, index, elementType, elem);

                PushPathElement(new PathElement(Invariant($"[{index}]")));
            }

            public override void EndTraverseValueCollectionElement(Type type, IEnumerable enumerable, int index, Type elementType, object elem)
            {
                base.EndTraverseValueCollectionElement(type, enumerable, index, elementType, elem);

                PopPathElement();
            }

            public override void BeginTraverseKeyValueCollectionKey(Type type, IDictionary dictionary, int index, Type keyType, object key)
            {
                base.BeginTraverseKeyValueCollectionKey(type, dictionary, index, keyType, key);

                PushPathElement(new PathElement(Invariant($".Keys[{index}]")));
            }

            public override void EndTraverseKeyValueCollectionKey(Type type, IDictionary dictionary, int index, Type keyType, object key)
            {
                PopPathElement();
            }

            public override void BeginTraverseKeyValueCollectionValue(Type type, IDictionary dictionary, int index, Type keyType, object key, Type valueType, object value)
            {
                base.BeginTraverseKeyValueCollectionValue(type, dictionary, index, keyType, key, valueType, value);

                PushPathElement(new PathElement(Invariant($"[{key}]")));
            }

            public override void EndTraverseKeyValueCollectionValue(Type type, IDictionary dictionary, int index, Type keyType, object key, Type valueType, object value)
            {
                base.EndTraverseKeyValueCollectionValue(type, dictionary, index, keyType, key, valueType, value);

                PopPathElement();
            }

            public override void BeginTraverseNullableStruct(Type type, object obj)
            {
                base.BeginTraverseNullableStruct(type, obj);

                PushPathElement(new PathElement(".Value"));
            }

            public override void EndTraverseNullableStruct(Type type, object obj)
            {
                base.EndTraverseNullableStruct(type, obj);

                PopPathElement();
            }

            public override void BeginTraverseDerivedClass(Type type, object obj, Type derivedType)
            {
                base.BeginTraverseDerivedClass(type, obj, derivedType);

                PushPathElement(new PathElement($"{{as {derivedType.Name}}}"));
            }

            public override void EndTraverseDerivedClass(Type type, object obj, Type derivedType)
            {
                base.EndTraverseDerivedClass(type, obj, derivedType);

                PopPathElement();
            }

            protected struct PathElement
            {
                public string Str;

                public PathElement(string str)
                {
                    Str = str;
                }
            }

            protected List<PathElement> _currentPath = new List<PathElement>();

            protected void PushPathElement(PathElement pathElement)
            {
                _currentPath.Add(pathElement);
            }

            protected void PopPathElement()
            {
                _currentPath.RemoveAt(_currentPath.Count-1);
            }
        }
    }
}
