// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Legacy version of <see cref="PlayerAuthEntryBase"/>.
    /// </summary>
    [MetaSerializable]
    public class LegacyPlayerAuthEntry
    {
        [MetaMember(1)] public MetaTime AttachedAt { get; private set; }

        LegacyPlayerAuthEntry() { }
    }
}
