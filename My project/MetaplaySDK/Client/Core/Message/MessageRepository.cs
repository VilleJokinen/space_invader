// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Message
{
    // MetaMessageSpec

    public class MetaMessageSpec
    {
        public readonly int                 TypeCode;           // Unique integer id for the message type
        public readonly Type                Type;               // Type of the message class
        public readonly string              Name;               // Name of the message (Type.ToGenericTypeString())
        public readonly MessageDirection    MessageDirection;
        public readonly MessageRoutingRule  RoutingRule;

        public MetaMessageSpec(int typeCode, Type type, MetaMessageAttribute msgAttrib, MessageRoutingRule routingRule)
        {
            TypeCode            = typeCode;
            Type                = type;
            Name                = type.ToGenericTypeString();
            MessageDirection    = msgAttrib.Direction;
            RoutingRule         = routingRule;
        }
    }

    // MetaMessageRepository

    public class MetaMessageRepository
    {
        static MetaMessageRepository _instance = null;
        public static MetaMessageRepository Instance => _instance ?? throw new InvalidOperationException("MetaMessageRepository not yet initialized");

        Dictionary<int, MetaMessageSpec>    _specFromCode   = new Dictionary<int, MetaMessageSpec>();
        Dictionary<Type, MetaMessageSpec>   _specFromType   = new Dictionary<Type, MetaMessageSpec>();

        public static void Initialize()
        {
            if (_instance != null)
                throw new InvalidOperationException($"Duplicate initialization of {nameof(EntityKindRegistry)}");

            _instance = new MetaMessageRepository();
        }

        MetaMessageRepository()
        {
            RegisterMessageTypes();
        }

        public MetaMessageSpec GetFromType(Type type)
        {
            if (_specFromType.TryGetValue(type, out MetaMessageSpec spec))
                return spec;
            else
                throw new KeyNotFoundException($"No registered MetaMessage for type '{type.ToGenericTypeString()}'");
        }

        public bool TryGetFromType(Type type, out MetaMessageSpec msgSpec)
        {
            return _specFromType.TryGetValue(type, out msgSpec);
        }

        public MetaMessageSpec GetFromTypeCode(int typeCode)
        {
            if (_specFromCode.TryGetValue(typeCode, out MetaMessageSpec spec))
                return spec;
            else
                throw new KeyNotFoundException($"No registered MetaMessage with typeCode '{typeCode}'");
        }

        public bool TryGetFromTypeCode(int typeCode, out MetaMessageSpec msgSpec)
        {
            return _specFromCode.TryGetValue(typeCode, out msgSpec);
        }

        private void RegisterMessageTypes()
        {
            // Iterate own assemblies for MetaMessages
            foreach (Type msgType in TypeScanner.GetConcreteDerivedTypes<MetaMessage>())
            {
                MetaMessageAttribute msgAttrib = msgType.GetCustomAttribute<MetaMessageAttribute>();
                MetaDebug.Assert(msgAttrib != null, string.Format(CultureInfo.InvariantCulture, "Missing MetaMessageAttribute from {0}", msgType));
                int typeCode = msgAttrib.TypeCode;
                MetaDebug.Assert(typeCode >= 1, "Message type codes must be positive integers");

                // Check for conflicts
                if (_specFromCode.TryGetValue(typeCode, out MetaMessageSpec conflictingSpec))
                    MetaDebug.Assert(false, string.Format(CultureInfo.InvariantCulture, "Duplicate message type code #{0}: {1}, {2}", typeCode, msgType, conflictingSpec.Type));

                // Ignore message if it's not enabled. Note that we check the basic well-formedness checks anyway.
                if (!msgType.IsMetaFeatureEnabled())
                    continue;

                if (!MetaSerializerTypeRegistry.TryGetTypeSpec(msgType, out MetaSerializableType typeSpec))
                    throw new ArgumentException($"Message type {msgType} must be included in the serializable types. Check the namespace/assembly exclusions of the serialization generation.");

                // Get routing rule, if any
                MessageRoutingRule routingRule = msgType.GetCustomAttribute<MessageRoutingRule>();

                if ((msgAttrib.Direction == MessageDirection.ClientToServer || msgAttrib.Direction == MessageDirection.Bidirectional) && routingRule == null)
                    throw new ArgumentException($"ClientToServer and Bidirectional messages must have a routing rule (on message type {msgType} (code {typeCode}))");
                if ((msgAttrib.Direction == MessageDirection.ServerToClient || msgAttrib.Direction == MessageDirection.ClientInternal) && routingRule != null)
                    throw new ArgumentException($"ServerToClient and ClientInternal messages must not have a routing rule (on message type {msgType} (code {typeCode}))");

                bool isProtocolMessage = msgAttrib.Direction == MessageDirection.ClientToServer || msgAttrib.Direction == MessageDirection.ServerToClient || msgAttrib.Direction == MessageDirection.Bidirectional;
                if (isProtocolMessage && !typeSpec.IsPublic)
                {
                    throw new ArgumentException(
                        $"Message {msgType.ToGenericTypeString()} is defined as a client <-> server message (with MessageDirection), but it is not included in the shared logic namespaces. "
                        + "Shared logic namespaces/assemblies are logic that is shared between the server and the client. "
                        + "Check the direction and the namespace/assembly of the message definition are correct.");
                }

                bool isInternalMessage = msgAttrib.Direction == MessageDirection.ClientInternal || msgAttrib.Direction == MessageDirection.ServerInternal;
                if (isInternalMessage && typeSpec.IsPublic)
                {
                    throw new ArgumentException(
                        $"Message {msgType.ToGenericTypeString()} is defined as a {msgAttrib.Direction} message (with MessageDirection), but it is included in the shared logic namespaces. "
                        + "Shared logic namespaces/assemblies are logic that is shared between the server and the client, internal messages should not be shared. "
                        + "Check the direction and the namespace/assembly of the message definition are correct.");
                }

                // Store spec
                MetaMessageSpec spec = new MetaMessageSpec(typeCode, msgType, msgAttrib, routingRule);
                _specFromCode.Add(typeCode, spec);
                _specFromType.Add(msgType, spec);
            }

            // Check messages don't contain any invalid data (even transitively)
            CheckMessageMembers();
        }

        static void CheckMembersRecursive(MetaMessageSpec msgSpec, Type type, HashSet<Type> visited)
        {
            // If type already visited, ignore
            if (visited.Contains(type))
                return;
            visited.Add(type);

            // Ignore serializer built-in types
            if (TaggedWireSerializer.IsBuiltinType(type))
                return;

            if (MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType typeSpec))
            {
                // Only conrete objects have members
                // \todo [petri] recurse into collections?
                if (typeSpec.IsConcreteObject)
                {
                    foreach (MetaSerializableMember member in typeSpec.Members)
                    {
                        Type memberType = member.Type;
                        //DebugLog.Info("{0}.{1} : {2}", msgSpec.Name, member.Name, memberType.ToGenericTypeString());

                        if (typeof(ModelAction).IsAssignableFrom(memberType))
                            throw new InvalidOperationException($"MetaMessage {msgSpec.Name} contains {typeSpec.Name}.{member.Type.ToGenericTypeString()} is an Action reference. Action members in Messages must be wrapped in MetaSerialized<>");

                        if (memberType.ImplementsGenericInterface(typeof(IGameConfigData<>)))
                            throw new InvalidOperationException($"MetaMessage {msgSpec.Name} contains {typeSpec.Name}.{member.Type.ToGenericTypeString()} is an IGameConfigData reference, which is not allowed!");

                        // Recurse to children
                        CheckMembersRecursive(msgSpec, memberType, visited);
                    }
                }
            }
#if NETCOREAPP // cloud
            else if (type == typeof(Akka.Actor.IActorRef))
            {
                // IActorRefs are valid
            }
#endif
            else if (type.IsCollection())
            {
                if (type.IsDictionary())
                {
                    (Type keyType, Type valueType) = type.GetDictionaryKeyAndValueTypes();
                    CheckMembersRecursive(msgSpec, keyType, visited);
                    CheckMembersRecursive(msgSpec, valueType, visited);
                }
                else
                {
                    Type elemType = type.GetCollectionElementType();
                    CheckMembersRecursive(msgSpec, elemType, visited);
                }
            }
            else if (type.IsInterface)
                throw new InvalidOperationException($"Interface container type {type.ToGenericTypeString()} within message {msgSpec.Name}, only concrete collection types can be used");
            else
                DebugLog.Warning("Message {MsgType} contains a member (or member of a member) with unknown type: {Type}", msgSpec.Name, type.ToGenericTypeString());
        }

        void CheckMessageMembers()
        {
            HashSet<Type> visited = new HashSet<Type>();

            foreach (MetaMessageSpec msgSpec in _specFromCode.Values)
                CheckMembersRecursive(msgSpec, msgSpec.Type, visited);
        }

        public void Print()
        {
            // Print in sorted order
            Console.WriteLine("Messages:");
            var sorted = _specFromCode.ToList();
            sorted.Sort((a, b) => a.Key - b.Key);
            foreach (var entry in sorted)
            {
                MetaMessageSpec spec = entry.Value;
                Console.WriteLine("  #{0}: {1}", spec.TypeCode, spec.Type);
            }
            Console.WriteLine();
        }
    }
}
