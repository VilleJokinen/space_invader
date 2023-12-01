// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core
{
    [MetaSerializable]
    public enum ClientPlatform
    {
        Unknown = 0,
        iOS = 1,
        Android = 2,
        WebGL = 3,
        UnityEditor = 4,
    };
}
