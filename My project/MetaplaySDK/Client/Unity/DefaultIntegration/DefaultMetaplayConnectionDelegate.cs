// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using System;
using System.Threading.Tasks;

namespace Metaplay.Unity.DefaultIntegration
{
    public class DefaultMetaplayConnectionDelegate : IMetaplayClientConnectionDelegate
    {
        public ISessionStartHook        SessionStartHook    { protected get; set; }
        public ISessionContextProvider  SessionContext      { protected get; set; }

        public virtual void Init()
        {
        }

        public virtual void Update()
        {
        }

        public virtual void OnHandshakeComplete()
        {
        }

        public virtual Handshake.ILoginRequestGamePayload GetLoginPayload() => null;
        public virtual ISessionStartRequestGamePayload GetSessionStartRequestPayload() => null;

        public virtual void OnSessionStarted(ClientSessionStartResources startResources)
        {
            SessionStartHook?.OnSessionStarted(startResources);
        }

        public virtual LoginDebugDiagnostics GetLoginDebugDiagnostics(bool isSessionResumption)
        {
            try
            {
                MetaTime currentTime = MetaTime.Now;
                IPlayerClientContext playerContextMaybe = isSessionResumption ? SessionContext?.PlayerContext : null;

                MetaDuration? currentPauseDuration;
                MetaDuration? durationSincePauseEnd;
                if (MetaplaySDK.ApplicationPauseStatus == ApplicationPauseStatus.Pausing)
                {
                    currentPauseDuration = currentTime - MetaplaySDK.ApplicationLastPauseBeganAt;
                    durationSincePauseEnd = null;
                }
                else if (MetaplaySDK.ApplicationPauseStatus == ApplicationPauseStatus.Running
                      && MetaplaySDK.ApplicationLastPauseBeganAt != MetaTime.Epoch
                      && MetaplaySDK.ApplicationLastPauseDuration != MetaDuration.Zero)
                {
                    currentPauseDuration = null;
                    durationSincePauseEnd = currentTime - (MetaplaySDK.ApplicationLastPauseBeganAt + MetaplaySDK.ApplicationLastPauseDuration);
                }
                else
                {
                    currentPauseDuration = null;
                    durationSincePauseEnd = null;
                }

                return new LoginDebugDiagnostics
                {
                    Timestamp                           = currentTime,
                    Session                             = MetaplaySDK.Connection.TryGetLoginSessionDebugDiagnostics(),
                    ServerConnection                    = MetaplaySDK.Connection.TryGetLoginServerConnectionDebugDiagnostics(),
                    Transport                           = MetaplaySDK.Connection.TryGetLoginTransportDebugDiagnostics(),
                    IncidentReport                      = MetaplaySDK.IncidentUploader?.GetLoginIncidentReportDebugDiagnostics(),
                    //MainLoop                            = StateManager.Instance?.GetDebugDiagnostics(), // \todo #helloworld
                    CurrentPauseDuration                = currentPauseDuration,
                    DurationSincePauseEnd               = durationSincePauseEnd,
                    DurationSinceConnectionUpdate       = currentTime - MetaplaySDK.ApplicationPreviousEndOfTheFrameAt,
                    DurationSincePlayerContextUpdate    = currentTime - playerContextMaybe?.LastUpdateTimeDebug, // \note Nullable
                    ExpectSessionResumptionPing         = true,
                };
            }
            catch (Exception ex)
            {
                return new LoginDebugDiagnostics{ DiagnosticsError = ex.ToString() };
            }
        }

        public virtual void OnFullProtocolHashMismatch(uint clientProtocolHash, uint serverProtocolHash) { }

        public virtual void FlushPendingMessages()
        {
            // Flush player context
            SessionContext?.PlayerContext?.FlushActions();

            // Flush all clients in EntityClientStore
            SessionContext?.ClientStore?.FlushPendingMessages();
        }
    }
}
