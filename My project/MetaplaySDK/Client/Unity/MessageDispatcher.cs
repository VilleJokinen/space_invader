// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Client.Messages;
using Metaplay.Core;
using Metaplay.Core.Message;
using System;

namespace Metaplay.Unity
{
    /// <summary>
    /// <inheritdoc cref="IMessageDispatcher"/>
    /// </summary>
    public class MessageDispatcher : BasicMessageDispatcher
    {
        MetaplayConnection _connection;

        public MessageDispatcher(LogChannel log) : base(log)
        {
        }

        protected override bool SendMessageInternal(MetaMessage message)
        {
            return _connection?.SendToServer(message) ?? false;
        }

        internal void SetConnection(MetaplayConnection connection)
        {
            _connection = connection;
        }

        internal void OnReceiveMessage(MetaMessage msg)
        {
            if (Log.IsDebugEnabled && ShouldLogMessage(msg))
                Log.Debug("Receive: {Message}", PrettyPrint.Compact(msg));

            if (!DispatchMessage(msg))
            {
                Log.Warning("No handlers for message {Type}", msg.GetType().Name);
            }
        }

        protected override void OnListenerInvocationException(Exception ex)
        {
            MetaplaySDK.IncidentTracker.ReportUnhandledException(ex);
        }

        static bool ShouldLogMessage(MetaMessage message)
        {
            if (message is MessageTransportInfoWrapperMessage)
                return false;
            return true;
        }
    }
}
