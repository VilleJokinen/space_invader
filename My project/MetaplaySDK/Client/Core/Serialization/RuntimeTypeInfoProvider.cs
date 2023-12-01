// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// <see cref="IMetaSerializerTypeInfoProvider"/> based on a pre-baked, compiled registry. Used by Unity Client.
    /// </summary>
    public class RuntimeTypeInfoProvider : IMetaSerializerTypeInfoProvider
    {
        delegate uint GetTypeInfoMethod(Dictionary<Type, MetaSerializableType> types);
        readonly GetTypeInfoMethod _generatedMethod;

        RuntimeTypeInfoProvider(GetTypeInfoMethod getter)
        {
            _generatedMethod = getter;
        }

        public static bool TryCreateFromGeneratedCode(Type roslynGeneratedTypeInfo, out RuntimeTypeInfoProvider provider)
        {
            MethodInfo method = roslynGeneratedTypeInfo.GetMethod("GetTypeInfo");
            if (method == null)
            {
                provider = null;
                return false;
            }
            provider = new RuntimeTypeInfoProvider((GetTypeInfoMethod)method.CreateDelegate(typeof(GetTypeInfoMethod)));
            return true;
        }

        public MetaSerializerTypeInfo GetTypeInfo()
        {
            MetaSerializerTypeInfo ret;
            ret.Specs = new Dictionary<Type, MetaSerializableType>();
            ret.FullTypeHash = _generatedMethod(ret.Specs);
            return ret;
        }
    }

}
