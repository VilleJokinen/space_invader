// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Player
{
    public static class PlayerModelTypeExtensions
    {
        public static bool IsPlayerModelClass(this Type type)
        {
            return type.ImplementsInterface<IPlayerModelBase>()
                && !type.IsAbstract;
        }
    }
}
