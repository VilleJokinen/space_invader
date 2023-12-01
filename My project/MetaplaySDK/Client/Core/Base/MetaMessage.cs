// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;

namespace Metaplay.Core
{
    // MessageDirection

    public enum MessageDirection
    {
        Bidirectional,
        ClientToServer,
        ServerToClient,
        ServerInternal,
        ClientInternal,
    }

    // MessageRoutingRule

    [AttributeUsage(AttributeTargets.Class)]
    public abstract class MessageRoutingRule : Attribute { }

    /// <summary> Message is a part of the core connection or session protocol. </summary>
    public class MessageRoutingRuleProtocol     : MessageRoutingRule{ }
    /// <summary> Message is handled by SessionActor (or SessionActorBase). </summary>
    public class MessageRoutingRuleSession      : MessageRoutingRule{ public static MessageRoutingRuleSession Instance = new MessageRoutingRuleSession(); }
    /// <summary> Message is routed to the owned player. </summary>
    public class MessageRoutingRuleOwnedPlayer  : MessageRoutingRule{ }
    #if !METAPLAY_DISABLE_GUILDS
    /// <summary> Message is routed to the current guild. </summary>
    public class MessageRoutingRuleCurrentGuild : MessageRoutingRule{ }
    #endif
    /// <summary> Message is routed to the other peer of the entity channel. This may only be sent from a context where entity channel is established, e.g MultiplayerEntityClientContext, and MultiplayerEntityActorBase. </summary>
    public class MessageRoutingRuleEntityChannel  : MessageRoutingRule{ }

    // MetaMessageAttribute

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MetaMessageAttribute : Attribute, ISerializableTypeCodeProvider, ISerializableFlagsProvider
    {
        public readonly int                 MessageTypeCode;
        public readonly MessageDirection    Direction;

        // Extra information for serializer: integer typeCode of message, and set implicit members
        public int                          TypeCode    => MessageTypeCode;
        public MetaSerializableFlags        ExtraFlags  { get; } = MetaSerializableFlags.ImplicitMembers;

        public MetaMessageAttribute(int typeCode, MessageDirection direction, bool hasExplicitMembers = false)
        {
            MessageTypeCode = typeCode;
            Direction       = direction;

            if (hasExplicitMembers)
                ExtraFlags &= ~MetaSerializableFlags.ImplicitMembers;
        }
    }

    // MetaMessage

    [MetaSerializable]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public abstract class MetaMessage
    {
    };
}
