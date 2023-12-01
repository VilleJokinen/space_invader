// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Network;

namespace Metaplay.BotClient
{
    public sealed class BotMessageDispatcher : BasicMessageDispatcher
    {
        ServerConnection _connection;

        public BotMessageDispatcher(LogChannel log) : base(log)
        {
        }

        public void SetConnection(ServerConnection connection)
        {
            _connection = connection;
        }

        protected override bool SendMessageInternal(MetaMessage message)
        {
            return _connection?.EnqueueSendMessage(message) ?? false;
        }

        public void OnReceiveMessage(MetaMessage msg)
        {
            DispatchMessage(msg);
        }
    }
}
