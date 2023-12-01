// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Client.Messages;
using Metaplay.Core.Message;
using System.Collections.Generic;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Provides a simple Healthy/Not-healthy assesment by reading MessageTransport Info stream.
    /// </summary>
    public class TransportQosMonitor
    {
        bool _isWriteHealthy;
        bool _isReadHealthy;
        bool _hasSession;
        bool _hasTransport;

        public bool IsHealthy { get; private set; }

        public TransportQosMonitor()
        {
            Reset();
        }

        public void Reset()
        {
            IsHealthy = false;
            _isWriteHealthy = true;
            _isReadHealthy = true;
            _hasSession = false;
            _hasTransport = false;
        }

        /// <summary>
        /// Updates internal model from messages and current timestamp. Returns <c>true</c> if QoS assesment changed
        /// during this call.
        /// </summary>
        public bool ProcessMessages(List<MetaMessage> messages)
        {
            foreach (MetaMessage message in messages)
            {
                switch (message)
                {
                    case MessageTransportInfoWrapperMessage wrapper:
                    {
                        switch (wrapper.Info)
                        {
                            case StreamMessageTransport.ReadDurationWarningInfo readDurationWarning:
                                _isReadHealthy = readDurationWarning.IsEnd;
                                break;

                            case StreamMessageTransport.WriteDurationWarningInfo writeDurationWarning:
                                _isWriteHealthy = writeDurationWarning.IsEnd;
                                break;

                            case ServerConnection.TransportLifecycleInfo transportEvent:
                            {
                                // reset transport's read/write healths when transport dies/spawns.
                                _isReadHealthy = true;
                                _isWriteHealthy = true;
                                _hasTransport = transportEvent.IsTransportAttached;
                                break;
                            }

                            case ServerConnection.SessionConnectionErrorLostInfo _:
                            {
                                _hasSession = false;
                                break;
                            }
                        }
                        break;
                    }

                    case SessionProtocol.SessionStartSuccess _:
                    {
                        _hasSession = true;
                        break;
                    }

                    case SessionProtocol.SessionResumeSuccess _:
                    {
                        _hasSession = true;
                        break;
                    }
                }
            }

            bool oldIsHealthy = IsHealthy;
            IsHealthy = _isWriteHealthy && _isReadHealthy && _hasSession && _hasTransport;
            return IsHealthy != oldIsHealthy;
        }
    }
}
