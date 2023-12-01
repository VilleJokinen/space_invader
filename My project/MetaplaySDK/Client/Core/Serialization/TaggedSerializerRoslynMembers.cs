// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if NET_4_6 || NETSTANDARD2_1 || NETSTANDARD2_1_OR_GREATER
using System.Reflection.Emit;
#endif

namespace Metaplay.Core.Serialization
{
    public delegate TMember GetMemberDelegate<TObject, TMember>(ref TObject obj);
    public delegate void SetMemberDelegate<TObject, TMember>(ref TObject obj, TMember value);
    public delegate void InvokeOnDeserializedMethodDelegate<TObject>(ref TObject obj);
    public delegate void InvokeOnDeserializedMethodWithParamsDelegate<TObject>(ref TObject obj, MetaOnDeserializedParams onDeserializedParams);
    public delegate TResult InvokeOnMemberDeserializationFailureMethodDelegate<TResult>(MetaMemberDeserializationFailureParams failureParams);
    public delegate TObject CreateInstanceDelegate<TObject>();
    public delegate TObject CreateInstanceWithParametersDelegate<TObject>(object[] parameters);

    /// <summary>
    /// Generates member accessors to private members of types. Used by the serializer when running
    /// inside Unity Editor where the direct accessing of members is not supported due to Mono runtime
    /// being more restrictive about what dynamically loaded .dlls access to internal/private members.
    ///
    /// If using .NET 4.x compatibility, use the more efficient ILGenerator for accessors. With .NET 2.0
    /// compatibility, ILGenerator is not available, so garbage-generating reflection is used. This is
    /// only used in the Editor, though, so should not be a big problem.
    /// </summary>
    // \todo [petri] is there a way to use runtime code generation even with .NET 2.0 in Unity?
    public static class MemberAccessGenerator
    {
        static MetaSerializableMember GetMemberByName(List<MetaSerializableMember> members, string memberName)
        {
            foreach (MetaSerializableMember member in members)
            {
                if (member.Name == memberName)
                    return member;
            }
            throw new InvalidOperationException("Did not find Member");
        }

        public static GetMemberDelegate<TObject, TMember> GenerateGetMember<TObject, TMember>(string memberName)
        {
            // On unity editor, we compute the method lazily on first access. This reduces the time unity editor is blocked.
            #if UNITY_EDITOR
            GetMemberDelegate<TObject, TMember> compiled = null;
            return (ref TObject obj) =>
            {
                if (compiled == null)
                    compiled = InternalGenerateGetMember<TObject, TMember>(memberName);
                return compiled(ref obj);
            };
            #else
            return InternalGenerateGetMember<TObject, TMember>(memberName);
            #endif
        }

        static GetMemberDelegate<TObject, TMember> InternalGenerateGetMember<TObject, TMember>(string memberName)
        {
            Type typeObject = typeof(TObject);
            MetaSerializableType typeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeObject);
            MetaSerializableMember member = GetMemberByName(typeSpec.Members, memberName);
#if NET_4_6 || NETSTANDARD2_1 || NETSTANDARD2_1_OR_GREATER
            Type typeMember = typeof(TMember);

            DynamicMethod method = new DynamicMethod($"GetMember<{typeObject.Name}, {typeMember.Name}>", typeMember, new Type[] { typeObject.MakeByRefType() }, member.MemberInfo.DeclaringType);
            ILGenerator il = method.GetILGenerator();

            // Dereference object (only for classes)
            il.Emit(OpCodes.Ldarg_0);
            if (typeObject.IsClass)
                il.Emit(OpCodes.Ldind_Ref);

            // Get field/property
            if (member.MemberInfo is FieldInfo fieldInfo)
                il.Emit(OpCodes.Ldfld, fieldInfo);
            else if (member.MemberInfo is PropertyInfo propInfo)
                il.Emit(OpCodes.Call, propInfo.GetGetMethod(nonPublic: true));
            else
                throw new InvalidOperationException($"Unknown member type for {typeSpec.Name}.{memberName} (expecting Field or Property)");

            // Return field/property
            il.Emit(OpCodes.Ret);

            return (GetMemberDelegate<TObject, TMember>)method.CreateDelegate(typeof(GetMemberDelegate<TObject, TMember>));
#else
            Func<object, object> getValue = member.GetValue;
            return (ref TObject obj) => (TMember)getValue(obj);
#endif
        }

        public static SetMemberDelegate<TObject, TMember> GenerateSetMember<TObject, TMember>(string memberName)
        {
            // On unity editor, we compute the method lazily on first access. This reduces the time unity editor is blocked.
            #if UNITY_EDITOR
            SetMemberDelegate<TObject, TMember> compiled = null;
            return (ref TObject obj, TMember value) =>
            {
                if (compiled == null)
                    compiled = InternalGenerateSetMember<TObject, TMember>(memberName);
                compiled(ref obj, value);
            };
            #else
            return InternalGenerateSetMember<TObject, TMember>(memberName);
            #endif
        }

        static SetMemberDelegate<TObject, TMember> InternalGenerateSetMember<TObject, TMember>(string memberName)
        {
            Type typeObject = typeof(TObject);
            MetaSerializableType typeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeObject);
            MetaSerializableMember member = GetMemberByName(typeSpec.Members, memberName);
#if NET_4_6 || NETSTANDARD2_1 || NETSTANDARD2_1_OR_GREATER
            Type typeMember = typeof(TMember);
            DynamicMethod method = new DynamicMethod($"SetMember<{typeObject.Name}, {typeMember.Name}>", null, new Type[] { typeObject.MakeByRefType(), typeMember }, member.MemberInfo.DeclaringType);

            ILGenerator il = method.GetILGenerator();

            // Dereference object (only for classes)
            il.Emit(OpCodes.Ldarg_0);
            if (typeObject.IsClass)
                il.Emit(OpCodes.Ldind_Ref);

            // Get value
            il.Emit(OpCodes.Ldarg_1);

            // Store field/property
            if (member.MemberInfo is FieldInfo fieldInfo)
                il.Emit(OpCodes.Stfld, fieldInfo);
            else if (member.MemberInfo is PropertyInfo propInfo)
                il.Emit(OpCodes.Call, propInfo.GetSetMethodOnDeclaringType());
            else
                throw new InvalidOperationException($"Unknown member type for {typeSpec.Name}.{memberName} (expecting Field or Property)");

            il.Emit(OpCodes.Ret);

            return (SetMemberDelegate<TObject, TMember>)method.CreateDelegate(typeof(SetMemberDelegate<TObject, TMember>));
#else
            Action<object, object> setValue = member.SetValue;

            // For value types, box the object first, and then copy back (otherwise setter has no effect as only the copied boxed value is mutated)
            if (typeObject.IsValueType)
            {
                return (ref TObject obj, TMember value) =>
                {
                    object boxed = (object)obj;
                    setValue(boxed, value);
                    obj = (TObject)boxed;
                };
            }
            else
                return (ref TObject obj, TMember value) => setValue(obj, value);
#endif
        }

        public static InvokeOnDeserializedMethodDelegate<TObject> GenerateInvokeOnDeserializedMethod<TObject>(string methodName)
        {
            // \todo [nuutti] ILGenerator version

            Type typeObject = typeof(TObject);
            MetaSerializableType typeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeObject);
            MethodInfo method = typeSpec.OnDeserializedMethods.Single(m => m.Name == methodName);

            // For value types, box the object first, and then copy back (otherwise any mutations to the object done by the method have no effect as only the copied boxed value is mutated)
            if (typeObject.IsValueType)
            {
                return (ref TObject obj) =>
                {
                    object boxed = obj;
                    method.InvokeWithoutWrappingError(boxed, null);
                    obj = (TObject)boxed;
                };
            }
            else
                return (ref TObject obj) => method.InvokeWithoutWrappingError(obj, null);
        }

        public static InvokeOnDeserializedMethodWithParamsDelegate<TObject> GenerateInvokeOnDeserializedMethodWithParams<TObject>(string methodName)
        {
            // \todo [nuutti] ILGenerator version

            Type typeObject = typeof(TObject);
            MetaSerializableType typeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeObject);
            MethodInfo method = typeSpec.OnDeserializedMethods.Single(m => m.Name == methodName);

            // For value types, box the object first, and then copy back (otherwise any mutations to the object done by the method have no effect as only the copied boxed value is mutated)
            if (typeObject.IsValueType)
            {
                return (ref TObject obj, MetaOnDeserializedParams par) =>
                {
                    object boxed = obj;
                    method.InvokeWithoutWrappingError(boxed, new object[]{ par });
                    obj = (TObject)boxed;
                };
            }
            else
                return (ref TObject obj, MetaOnDeserializedParams par) => method.InvokeWithoutWrappingError(obj, new object[]{ par });
        }

        public static InvokeOnMemberDeserializationFailureMethodDelegate<TResult> GenerateInvokeOnMemberDeserializationFailureMethod<TResult>(Type containingType, string memberName)
        {
            // \todo [nuutti] ILGenerator version

            MetaSerializableType containingTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(containingType);
            MetaSerializableMember member = containingTypeSpec.Members.Single(m => m.Name == memberName);

            MethodInfo method = member.OnDeserializationFailureMethod;
            if (method == null)
                throw new InvalidOperationException($"Member has no {nameof(member.OnDeserializationFailureMethod)}");

            return (MetaMemberDeserializationFailureParams failureParams) => (TResult)method.InvokeWithoutWrappingError(obj: null, parameters: new object[]{ failureParams });
        }

        public static CreateInstanceDelegate<TObject> GenerateCreateInstance<TObject>() where TObject : class // \note Require reference type, just because this shouldn't be needed for value types.
        {
            // \note No separate ILGenerator version for this. I think this just as good(?), when TObject is not a value type.
            //       And this is not used for value types, because those are always publicly default-constructible.

            Type            type        = typeof(TObject);
            ConstructorInfo constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null)
                                          ?? throw new InvalidOperationException($"No default constructor found for {type.ToNamespaceQualifiedTypeString()}");

            return () => (TObject)constructor.Invoke(null);
        }

        public static CreateInstanceWithParametersDelegate<TObject> GenerateCreateInstanceWithParameters<TObject>(params Type[] parameterTypes)
        {
            Type type = typeof(TObject);
            ConstructorInfo constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, parameterTypes, modifiers: null)
                ?? throw new InvalidOperationException($"No deserialization constructor found for {type.ToNamespaceQualifiedTypeString()}");

            return parameters => (TObject)constructor.Invoke(parameters);
        }
    }
}
