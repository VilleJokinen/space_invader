// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;

namespace Metaplay.Cloud.Sharding
{
    public class EntityShardingProvider : ExtensionIdProvider<EntitySharding>
    {
        public override EntitySharding CreateExtension(ExtendedActorSystem system)
        {
            return new EntitySharding(system);
        }
    }
}
