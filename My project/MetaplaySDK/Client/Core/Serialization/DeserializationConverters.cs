// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;

namespace Metaplay.Core.Serialization
{
    #region User-facing, predefined converter attributes

    /// <summary>
    /// Allows changing a struct to a class, in a (serialization-wise) backwards compatible manner.
    /// <para>
    /// You can put this on a class type to allow it to be deserialized from struct format.
    /// </para>
    /// </summary>
    public class MetaDeserializationConvertFromStructAttribute : MetaDeserializationConverterAttributeBase
    {
        public override void ValidateForTargetType(Type targetType, Dictionary<Type, MetaSerializableType> typeInfo)
        {
            if (typeInfo[targetType].WireType != WireDataType.NullableStruct)
                throw new InvalidOperationException($"{targetType.ToGenericTypeString()}: {nameof(MetaDeserializationConvertFromStructAttribute)} can only be used on a concrete class type");
        }

        public override IEnumerable<MetaDeserializationConverter> CreateConverters(Type targetType)
        {
            return new MetaDeserializationConverter[]
            {
                new DeserializationConverters.StructToClass(classType: targetType),
            };
        }
    }

    /// <summary>
    /// Allows introducing a base-and-subclasses hierarchy where previously only a concrete type was used,
    /// in a (serialization-wise) backwards compatible manner.
    /// <para>
    /// You can put this on a base class and give it the Type of one of its subclasses,
    /// to allow the base class to be deserialized from data where previously a concrete
    /// class was used; such concrete-class data will be deserialized as the specified
    /// subclass.
    /// </para>
    /// </summary>
    public class MetaDeserializationConvertFromConcreteDerivedTypeAttribute : MetaDeserializationConverterAttributeBase
    {
        Type _concreteType;

        public MetaDeserializationConvertFromConcreteDerivedTypeAttribute(Type concreteType)
        {
            _concreteType = concreteType;
        }

        public override void ValidateForTargetType(Type targetType, Dictionary<Type, MetaSerializableType> typeInfo)
        {
            if (!targetType.IsAssignableFrom(_concreteType))
            {
                throw new InvalidOperationException(
                    $"{targetType.ToGenericTypeString()}: {nameof(MetaDeserializationConvertFromConcreteDerivedTypeAttribute)} specifies type {_concreteType.ToGenericTypeString()}"
                    + $", which does not inherit {targetType.ToGenericTypeString()}.");
            }

            if (typeInfo[_concreteType].WireType != WireDataType.NullableStruct)
            {
                throw new InvalidOperationException(
                    $"{targetType.ToGenericTypeString()}: {nameof(MetaDeserializationConvertFromConcreteDerivedTypeAttribute)} specifies type {_concreteType.ToGenericTypeString()}"
                    + $", which is not a concrete class.");
            }
        }

        public override IEnumerable<MetaDeserializationConverter> CreateConverters(Type targetType)
        {
            return new MetaDeserializationConverter[]
            {
                new DeserializationConverters.ConcreteClassToBase(baseType: targetType, derivedType: _concreteType),
            };
        }
    }

    /// <summary>
    /// Similar to <see cref="MetaDeserializationConvertFromConcreteDerivedTypeAttribute"/>,
    /// but for cases where the legacy type was a struct rather than a class.
    /// </summary>
    public class MetaDeserializationConvertFromConcreteDerivedTypeStructAttribute : MetaDeserializationConverterAttributeBase
    {
        Type _concreteType;

        public MetaDeserializationConvertFromConcreteDerivedTypeStructAttribute(Type concreteType)
        {
            _concreteType = concreteType;
        }

        public override void ValidateForTargetType(Type targetType, Dictionary<Type, MetaSerializableType> typeInfo)
        {
            if (!targetType.IsAssignableFrom(_concreteType))
            {
                throw new InvalidOperationException(
                    $"{targetType.ToGenericTypeString()}: {nameof(MetaDeserializationConvertFromConcreteDerivedTypeStructAttribute)} specifies type {_concreteType.ToGenericTypeString()}"
                    + $", which does not inherit {targetType.ToGenericTypeString()}.");
            }

            if (typeInfo[_concreteType].WireType != WireDataType.NullableStruct)
            {
                throw new InvalidOperationException(
                    $"{targetType.ToGenericTypeString()}: {nameof(MetaDeserializationConvertFromConcreteDerivedTypeStructAttribute)} specifies type {_concreteType.ToGenericTypeString()}"
                    + $", which is not a concrete class.");
            }
        }

        public override IEnumerable<MetaDeserializationConverter> CreateConverters(Type targetType)
        {
            return new MetaDeserializationConverter[]
            {
                new DeserializationConverters.StructToBase(baseType: targetType, derivedType: _concreteType),
            };
        }
    }

    /// <summary>
    /// Similar to <see cref="MetaDeserializationConvertFromConcreteDerivedTypeAttribute"/>,
    /// but the designated concrete derived type is decided based on the <see cref="IMetaIntegration{T}"/> mechanism.
    /// This attribute can only be used on a <see cref="IMetaIntegrationConstructible"/> type.
    /// </summary>
    public class MetaDeserializationConvertFromIntegrationImplementationAttribute : MetaDeserializationConverterAttributeBase
    {
        public override void ValidateForTargetType(Type targetType, Dictionary<Type, MetaSerializableType> typeInfo)
        {
            if (!targetType.ImplementsInterface<IMetaIntegrationConstructible>())
                throw new InvalidOperationException($"{targetType.ToGenericTypeString()} has {nameof(MetaDeserializationConvertFromIntegrationImplementationAttribute)}, but is not {nameof(IMetaIntegrationConstructible)}");
        }

        public override IEnumerable<MetaDeserializationConverter> CreateConverters(Type targetType)
        {
            Type concreteType = IntegrationRegistry.GetSingleIntegrationType(targetType);

            return new MetaDeserializationConverter[]
            {
                new DeserializationConverters.ConcreteClassToBase(baseType: targetType, derivedType: concreteType),
            };
        }
    }

    #endregion

    #region Base classes for deserialization converters

    /// <summary>
    /// Defines a deserialization converter for a type.
    /// A deserialization converter permits deserializing a type from a wire format
    /// other than its normal format. Deserialization converters are designed
    /// to help with making serialization schema changes.
    /// <para>
    /// When a type has a deserialization converter, then upon deserialization,
    /// if the input <see cref="WireDataType"/> does not match the type's normal
    /// wire type, but does match that of a converter, then the converter is used:
    /// the input is deserialized as <see cref="SourceType"/>, and converted to the
    /// output type <see cref="TargetType"/> with <see cref="Convert"/> (which shall
    /// return an object of type <see cref="TargetType"/>).
    /// </para>
    /// </summary>
    /// <remarks>
    /// <see cref="Convert"/> returning <see cref="TargetType"/> is not currently
    /// checked at init time (some static typing acrobatics are probably needed
    /// to achieve that). If the conversion output fails to be assigned to the
    /// destination type, it will fail at deserialization time.
    /// </remarks>
    public abstract class MetaDeserializationConverter
    {
        /// <summary>
        /// The input wire type this converter accepts.
        /// </summary>
        public abstract WireDataType AcceptedWireDataType { get; }
        /// <summary>
        /// The type as which the input is deserialized.
        /// </summary>
        public abstract Type SourceType { get; }
        /// <summary>
        /// How the input is deserialized. See <see cref="SourceDeserializationKind"/> for explanation.
        /// The default is <see cref="SourceDeserializationKind.Normal"/>.
        /// </summary>
        public virtual SourceDeserializationKind SourceDeserialization => SourceDeserializationKind.Normal;
        /// <summary>
        /// The type to which the converter converts.
        /// </summary>
        public abstract Type TargetType { get; }
        /// <summary>
        /// Given the deserialized <see cref="SourceType"/>-typed object,
        /// convert it to a <see cref="TargetType"/>-typed object.
        /// </summary>
        public abstract object Convert(object source);

        /// <summary>
        /// Specifies how a source object is deserialized.
        /// </summary>
        public enum SourceDeserializationKind
        {
            /// <summary>
            /// Deserialize in normal manner, the same way as if the converter was not
            /// being used and the input was being parsed as <see cref="SourceType"/>.
            /// </summary>
            Normal,
            /// <summary>
            /// Applicable to by-members-serialized types: deserialize only the members
            /// and not the object's header (if any). In particular, when used with
            /// <see cref="WireDataType.NullableStruct"/>, the <c>IsNotNull</c> is not
            /// expected to be present in the input.
            /// This is only really useful for converters which convert from (non-nullable)
            /// struct to nullable or abstract class: this way a nullable class type
            /// can be directly used as the source type, instead of needing the source
            /// type to be a struct type that is otherwise identical to the target type.
            /// </summary>
            Members,
        }
    }

    /// <summary>
    /// Base class for attributes which define the deserialization converters for a type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true)]
    public abstract class MetaDeserializationConverterAttributeBase : Attribute
    {
        /// <summary>
        /// Validate that `CreateConverters` can be called for a given <paramref name="targetType"/>.
        /// <paramref name="targetType"/> is the type on which this attribute appears.
        /// Validation errors are communicated by throwing exceptions.
        /// </summary>
        public abstract void ValidateForTargetType(Type targetType, Dictionary<Type, MetaSerializableType> typeInfo);

        /// <summary>
        /// Create the converters for <paramref name="targetType"/>.
        /// <paramref name="targetType"/> is the type on which this attribute appears.
        /// </summary>
        public abstract IEnumerable<MetaDeserializationConverter> CreateConverters(Type targetType);
    }

    #endregion

    /// <summary>
    /// Predefined converter implementations. Intended to be SDK-internal; user code should
    /// use them via the attributes such as <see cref="MetaDeserializationConvertFromConcreteDerivedTypeAttribute"/>.
    /// </summary>
    public static class DeserializationConverters
    {
        /// <summary>
        /// Given a class type as target type, this takes input which was serialized
        /// from a struct type that was otherwise compatible with the class type.
        /// </summary>
        public class StructToClass : MetaDeserializationConverter
        {
            Type _classType;

            public StructToClass(Type classType)
            {
                _classType = classType;
            }

            // Parse Struct input as the _classType, but use the `Members` deserialization kind.
            // This achieves the same kind of input format as if _classType was instead
            // and equivalent struct type and using `Normal` deserialization kind.
            // Now, target type is now the same as source type, and therefore conversion is trivial.

            public override WireDataType AcceptedWireDataType => WireDataType.Struct;
            public override Type SourceType => _classType;
            public override SourceDeserializationKind SourceDeserialization => SourceDeserializationKind.Members;
            public override Type TargetType => _classType;

            public override object Convert(object obj)
            {
                return obj;
            }
        }

        /// <summary>
        /// Deserializes as a given concrete type and converts to its base type.
        /// </summary>
        public class ConcreteClassToBase : MetaDeserializationConverter
        {
            Type _baseType;
            Type _derivedType;

            public ConcreteClassToBase(Type baseType, Type derivedType)
            {
                _baseType = baseType;
                _derivedType = derivedType;
            }

            public override WireDataType AcceptedWireDataType => WireDataType.NullableStruct;
            public override Type SourceType => _derivedType;
            public override SourceDeserializationKind SourceDeserialization => SourceDeserializationKind.Normal;
            public override Type TargetType => _baseType;

            public override object Convert(object concrete)
            {
                // Conversion is trivial because the derived-type object is also of the base type.
                return concrete;
            }
        }

        /// <summary>
        /// Deserializes as a given concrete type, from Struct format,
        /// and converts to its base type.
        /// </summary>
        public class StructToBase : MetaDeserializationConverter
        {
            Type _baseType;
            Type _derivedType;

            public StructToBase(Type baseType, Type derivedType)
            {
                _baseType = baseType;
                _derivedType = derivedType;
            }

            public override WireDataType AcceptedWireDataType => WireDataType.Struct;
            public override Type SourceType => _derivedType;
            public override SourceDeserializationKind SourceDeserialization => SourceDeserializationKind.Members;
            public override Type TargetType => _baseType;

            public override object Convert(object concrete)
            {
                // Conversion is trivial because the derived-type object is also of the base type.
                return concrete;
            }
        }
    }
}
