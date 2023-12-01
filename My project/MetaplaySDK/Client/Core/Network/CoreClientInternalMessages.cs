// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Network;
using Metaplay.Core.TypeCodes;
using System.Runtime.Serialization;

namespace Metaplay.Client.Messages
{
    /// <summary>
    /// Pseudo-message sent on the client when socket connection is established to server.
    /// </summary>
    [MetaMessage(MessageCodesCore.ConnectedToServer, MessageDirection.ClientInternal)]
    public class ConnectedToServer : MetaMessage
    {
        public string           ChosenHostname;
        public bool             IsIPv4;
        public string           TlsPeerDescription;

        ConnectedToServer() { }
        public ConnectedToServer(string chosenHostname, bool isIPv4, string tlsPeerDescription)
        {
            ChosenHostname = chosenHostname;
            IsIPv4 = isIPv4;
            TlsPeerDescription = tlsPeerDescription;
        }
    }

    /// <summary>
    /// Pseudo-message sent on the client when socket connection to server is lost.
    /// </summary>
    [MetaMessage(MessageCodesCore.DisconnectedFromServer, MessageDirection.ClientInternal)]
    public class DisconnectedFromServer : MetaMessage
    {
    }

    /// <summary>
    /// Pseudo-message sent on the client if the connection protocol handshake fails.
    /// </summary>
    [MetaMessage(MessageCodesCore.ConnectionHandshakeFailure, MessageDirection.ClientInternal)]
    public class ConnectionHandshakeFailure : MetaMessage
    {
        public ProtocolStatus   ProtocolStatus  { get; private set; }

        ConnectionHandshakeFailure() { }
        public ConnectionHandshakeFailure(ProtocolStatus protocolStatus) { ProtocolStatus = protocolStatus; }
    }

    /// <summary>
    /// Pseudo-message sent on the client to deliver info-events from underlying MessageTransport.
    /// </summary>
    [MetaMessage(MessageCodesCore.MessageTransportInfoWrapperMessage, MessageDirection.ClientInternal)]
    public class MessageTransportInfoWrapperMessage : MetaMessage
    {
        // \note: This message is a client-internal and will never be serialized. As such, can avoid
        //        having to make the wrapped Info a serializeable type.
        [IgnoreDataMember]
        public MessageTransport.Info Info { get; private set; }

        MessageTransportInfoWrapperMessage() { }
        public MessageTransportInfoWrapperMessage(MessageTransport.Info info) { Info = info; }
    }
}
