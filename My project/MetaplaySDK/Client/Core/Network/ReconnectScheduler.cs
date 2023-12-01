// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Computes the timing of reconnection attempts.
    /// </summary>
    public static class ReconnectScheduler
    {
        /// <summary>
        /// Computes the time remaining to the time next reconnect should be attempted. This may be in the past. Returns <c>false</c> if reconnect shouldn't be attempted.
        /// </summary>
        public static bool TryGetDurationToSessionResumeAttempt(ServerConnection.SessionResumptionAttempt resumptionAttempt, DateTime deadlineAt, out TimeSpan outDurationToReconnect)
        {
            DateTime currentTime = DateTime.UtcNow;

            // First reconnection attempt of a resumption attempt has zero delay. Second after 1 second, and then every 2 seconds.
            DateTime reconnectAt;
            if (resumptionAttempt.NumConnectionAttempts == 1)
                reconnectAt = currentTime;
            else if (resumptionAttempt.NumConnectionAttempts == 2)
                reconnectAt = resumptionAttempt.LatestErrorTime + TimeSpan.FromSeconds(1);
            else
                reconnectAt = resumptionAttempt.LatestErrorTime + TimeSpan.FromSeconds(2);

            if (reconnectAt >= deadlineAt)
            {
                outDurationToReconnect = default;
                return false;
            }

            outDurationToReconnect = reconnectAt - currentTime;
            return true;
        }
    }
}
