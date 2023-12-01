// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using static System.FormattableString;

namespace Metaplay.Cloud.Sharding
{
    public interface IRemoteShardResolver
    {
        string GetShardNodeAddress(int shardNdx);
    }

    public class StaticShardResolver : IRemoteShardResolver
    {
        public readonly string  BaseAddress;
        public readonly int     BasePort;

        public StaticShardResolver(string baseAddress, int basePort)
        {
            BaseAddress = baseAddress;
            BasePort    = basePort;
        }

        public string GetShardNodeAddress(int shardNdx)
        {
            return Invariant($"{BaseAddress}:{BasePort + shardNdx}");
        }
    }
}
