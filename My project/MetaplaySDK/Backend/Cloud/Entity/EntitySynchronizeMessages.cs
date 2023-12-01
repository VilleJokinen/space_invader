// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Cloud.Entity.Synchronize.Messages
{
    /// <summary>
    /// EntityActor -> Shard node-local message for starting synchronization. Shard replies by completing the promise.
    /// </summary>
    public class EntitySynchronizationE2SBeginRequest
    {
        public EntityId                                     TargetEntityId  { get; private set; }
        public EntityId                                     FromEntityId    { get; private set; }
        public MetaSerialized<MetaMessage>                  Message         { get; private set; }
        public TaskCompletionSource<int>                    OpeningPromise  { get; private set; } // replies with E2S channel Id
        public ChannelWriter<MetaSerialized<MetaMessage>>   WriterPeer      { get; private set; } // after channel is open, messages piped here

        public EntitySynchronizationE2SBeginRequest(EntityId targetEntityId, EntityId fromEntityId, MetaSerialized<MetaMessage> message, TaskCompletionSource<int> openingPromise, ChannelWriter<MetaSerialized<MetaMessage>> writerPeer)
        {
            TargetEntityId = targetEntityId;
            FromEntityId = fromEntityId;
            Message = message;
            OpeningPromise = openingPromise;
            WriterPeer = writerPeer;
        }

        public EntitySynchronizationE2SBeginRequest()
        {
        }
    }

    /// <summary>
    /// Shard -> Shard inter-node message for starting synchronization. Replied with EntitySynchronizationS2SBeginResponse.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntitySynchronizationS2SBeginRequest, MessageDirection.ServerInternal)]
    public class EntitySynchronizationS2SBeginRequest : MetaMessage
    {
        public int                                          SourceChannelId { get; private set; }
        public EntityId                                     TargetEntityId  { get; private set; }
        public EntityId                                     FromEntityId    { get; private set; }
        public MetaSerialized<MetaMessage>                  Message         { get; private set; }

        public EntitySynchronizationS2SBeginRequest(int sourceChannelId, EntityId targetEntityId, EntityId fromEntityId, MetaSerialized<MetaMessage> message)
        {
            SourceChannelId = sourceChannelId;
            TargetEntityId = targetEntityId;
            FromEntityId = fromEntityId;
            Message = message;
        }

        public EntitySynchronizationS2SBeginRequest()
        {
        }
    }
    [MetaMessage(MessageCodesCore.EntitySynchronizationS2SBeginResponse, MessageDirection.ServerInternal)]
    public class EntitySynchronizationS2SBeginResponse : MetaMessage
    {
        public EntityId                                     TargetEntityId  { get; private set; }
        public int                                          SourceChannelId { get; private set; }
        public int                                          TargetChannelId { get; private set; }

        public EntitySynchronizationS2SBeginResponse(EntityId targetEntityId, int sourceChannelId, int targetChannelId)
        {
            TargetEntityId = targetEntityId;
            SourceChannelId = sourceChannelId;
            TargetChannelId = targetChannelId;
        }

        public EntitySynchronizationS2SBeginResponse()
        {
        }
    }

    /// <summary>
    /// Shard -> Entity node-local message for starting synchronization. Replied with EntitySynchronizationS2EBeginResponse.
    /// </summary>
    public class EntitySynchronizationS2EBeginRequest : IRoutedMessage
    {
        public int                                          ChannelId       { get; private set; }
        public EntityId                                     TargetEntityId  { get; private set; }
        public EntityId                                     FromEntityId    { get; private set; }
        public MetaSerialized<MetaMessage>                  Message         { get; private set; }

        public EntitySynchronizationS2EBeginRequest(int channelId, EntityId targetEntityId, EntityId fromEntityId, MetaSerialized<MetaMessage> message)
        {
            ChannelId = channelId;
            TargetEntityId = targetEntityId;
            FromEntityId = fromEntityId;
            Message = message;
        }

        public EntitySynchronizationS2EBeginRequest()
        {
        }
    }
    public class EntitySynchronizationS2EBeginResponse
    {
        public int                                          ChannelId       { get; private set; }
        public EntityId                                     FromEntityId    { get; private set; }
        public ChannelWriter<MetaSerialized<MetaMessage>>   WriterPeer      { get; private set; }

        public EntitySynchronizationS2EBeginResponse(int channelId, EntityId fromEntityId, ChannelWriter<MetaSerialized<MetaMessage>> writerPeer)
        {
            ChannelId = channelId;
            FromEntityId = fromEntityId;
            WriterPeer = writerPeer;
        }

        public EntitySynchronizationS2EBeginResponse()
        {
        }
    }

    /// <summary>
    /// Entity -> Shard message for synchronization channel. Empty message terminates channel.
    /// </summary>
    public class EntitySynchronizationE2SChannelMessage
    {
        public EntityId                                     FromEntityId    { get; private set; }
        public int                                          ChannelId       { get; private set; }
        public MetaSerialized<MetaMessage>                  Message         { get; private set; }

        public EntitySynchronizationE2SChannelMessage(EntityId fromEntityId, int channelId, MetaSerialized<MetaMessage> message)
        {
            FromEntityId = fromEntityId;
            ChannelId = channelId;
            Message = message;
        }

        public EntitySynchronizationE2SChannelMessage()
        {
        }
    }

    /// <summary>
    /// Shard <-> Shard message for synchronization channel. Empty message terminates channel.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntitySynchronizationS2SChannelMessage, MessageDirection.ServerInternal)]
    public class EntitySynchronizationS2SChannelMessage : MetaMessage
    {
        public EntityId                                 TargetEntityId  { get; private set; }
        public int                                      ChannelId       { get; private set; }
        public MetaSerialized<MetaMessage>              Message         { get; private set; }

        public EntitySynchronizationS2SChannelMessage(EntityId targetEntityId, int channelId, MetaSerialized<MetaMessage> message)
        {
            TargetEntityId = targetEntityId;
            ChannelId = channelId;
            Message = message;
        }

        public EntitySynchronizationS2SChannelMessage()
        {
        }
    }
}
