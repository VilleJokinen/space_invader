// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Config
{
    public static class GameConfigTypeExtensions
    {
        public static bool IsGameConfigClass(this Type type)
        {
            return type.ImplementsInterface<IGameConfig>() && type.IsClass && !type.IsAbstract && !type.IsGenericTypeDefinition;
        }

        public static bool IsGameConfigLibrary(this Type type)
        {
            return type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(GameConfigLibrary<,>)
                && type.GenericTypeArguments.Length == 2;
        }
    }

    public static class GameConfigTypeUtil
    {
        public static IEnumerable<MemberInfo> EnumerateLibraryMembersOfGameConfig(Type gameConfigType)
        {
            if (!gameConfigType.IsGameConfigClass())
                throw new ArgumentException($"{gameConfigType} is not a game config class");

            return gameConfigType.EnumerateInstanceDataMembersInUnspecifiedOrder()
                   .Where(memberInfo => memberInfo.GetDataMemberType().IsGameConfigLibrary());
        }
    }
}
