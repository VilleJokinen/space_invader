// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;

namespace Metaplay.Cloud.Entity.PubSub
{
    /// <summary>
    /// Entity wants to subscribe to events of another Entity. The target Entity should
    /// respond with <see cref="SubscribeAck"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntitySubscribe, MessageDirection.ServerInternal)]
    public class Subscribe : MetaMessage
    {
        public EntityId     TargetEntityId  { get; private set; }
        public EntityId     SubscriberId    { get; private set; }
        public IActorRef    SubscriberActor { get; private set; }
        public EntityTopic  Topic           { get; private set; }
        public int          ChannelId       { get; private set; }
        public MetaMessage  Message         { get; private set; }

        public Subscribe() { }
        public Subscribe(EntityId targetEntityId, EntityId subscriberId, IActorRef subscriberActor, EntityTopic topic, int channelId, MetaMessage message)
        {
            TargetEntityId  = targetEntityId;
            SubscriberId    = subscriberId;
            SubscriberActor = subscriberActor;
            Topic           = topic;
            ChannelId       = channelId;
            Message         = message;
        }
    }

    /// <summary>
    /// Acknowledge a subscription has been created. Response to <see cref="Subscribe"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntitySubscribeAck, MessageDirection.ServerInternal)]
    public class SubscribeAck : MetaMessage
    {
        public IActorRef                    TargetActor { get; private set; }
        public int                          ChannelId   { get; private set; }
        public MetaSerialized<MetaMessage>  Response    { get; private set; }

        public SubscribeAck() { }
        public SubscribeAck(IActorRef targetActor, int channelId, MetaSerialized<MetaMessage> response)
        {
            TargetActor = targetActor ?? throw new ArgumentNullException(nameof(targetActor));
            ChannelId   = channelId;
            Response    = response;
        }
    }

    /// <summary>
    /// An Entity wants to unsubscribe a given subscription channel.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntityUnsubscribe, MessageDirection.ServerInternal)]
    public class Unsubscribe : MetaMessage
    {
        public EntityId     TargetEntityId  { get; private set; }
        public EntityId     SubscriberId    { get; private set; }
        public int          ChannelId       { get; private set; }

        public Unsubscribe() { }
        public Unsubscribe(EntityId targetEntityId, EntityId subscriberId, int channelId)
        {
            TargetEntityId  = targetEntityId;
            SubscriberId    = subscriberId;
            ChannelId       = channelId;
        }
    }

    /// <summary>
    /// Result of a <see cref="Unsubscribe"/> to an actor.
    /// </summary>
    [MetaSerializable]
    public enum UnsubscribeResult
    {
        Success,            // Successfully unsubscribed
        UnknownSubscriber,  // The subscriber isn't known, can happen if was just kicked
    }

    /// <summary>
    /// Response to a <see cref="Unsubscribe"/> confirming that the subscription has been stopped.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntityUnsubscribeAck, MessageDirection.ServerInternal)]
    public class UnsubscribeAck : MetaMessage
    {
        public UnsubscribeResult Result { get; private set; }

        public UnsubscribeAck() { }
        public UnsubscribeAck(UnsubscribeResult result) { Result = result; }
    }

    /// <summary>
    /// Envelope for messages sent over a PubSub channel (in either direction).
    /// </summary>
    [MetaMessage(MessageCodesCore.EntityPubSubMessage, MessageDirection.ServerInternal)]
    public class PubSubMessage : MetaMessage
    {
        public EntityId                     FromEntityId    { get; private set; }
        public int                          ChannelId       { get; private set; }
        public MetaSerialized<MetaMessage>  Payload         { get; private set; }

        public PubSubMessage() { }
        public PubSubMessage(EntityId fromEntityId, int channelId, MetaSerialized<MetaMessage> payload)
        {
            FromEntityId    = fromEntityId;
            ChannelId       = channelId;
            Payload         = payload;
        }
    }

    [MetaMessage(MessageCodesCore.EntitySubscriberKicked, MessageDirection.ServerInternal)]
    public class SubscriberKicked : MetaMessage
    {
        public EntityId                     FromEntityId    { get; private set; }
        public int                          ChannelId       { get; private set; }
        public MetaSerialized<MetaMessage>  Message         { get; private set; }

        public SubscriberKicked() { }
        public SubscriberKicked(EntityId fromEntityId, int channelId, MetaSerialized<MetaMessage> message)
        {
            FromEntityId    = fromEntityId;
            ChannelId       = channelId;
            Message         = message;
        }
    }

    /// <summary>
    /// A watched Entity has terminated. Sent to EntityShards and EntityActors
    /// to handle accordingly.
    /// </summary>
    [MetaMessage(MessageCodesCore.EntityWatchedEntityTerminated, MessageDirection.ServerInternal)]
    public class WatchedEntityTerminated : MetaMessage
    {
        public EntityId EntityId { get; private set; }

        public WatchedEntityTerminated() { }
        public WatchedEntityTerminated(EntityId entityId) { EntityId = entityId; }
    }
}
