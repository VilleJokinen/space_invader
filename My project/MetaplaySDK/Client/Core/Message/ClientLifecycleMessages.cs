// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Sent to server when client application goes into the background
    /// </summary>
    [MetaMessage(MessageCodesCore.ClientLifecycleHintPausing, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class ClientLifecycleHintPausing : MetaMessage
    {
        public MetaDuration? MaxPauseDuration { get; private set; } // null if not declared
        public string PauseReason { get; private set; } // null if no reason

        ClientLifecycleHintPausing() { }
        public ClientLifecycleHintPausing(MetaDuration? maxPauseDuration, string pauseReason)
        {
            MaxPauseDuration = maxPauseDuration;
            PauseReason = pauseReason;
        }
    }

    /// <summary>
    /// Sent to server when client application resumes from background.
    /// </summary>
    [MetaMessage(MessageCodesCore.ClientLifecycleHintUnpausing, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class ClientLifecycleHintUnpausing : MetaMessage
    {
    }

    /// <summary>
    /// Sent to server when client application has resumed from background and has run one frame.
    /// </summary>
    [MetaMessage(MessageCodesCore.ClientLifecycleHintUnpaused, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class ClientLifecycleHintUnpaused : MetaMessage
    {
    }
}
