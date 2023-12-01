// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.Model;

namespace Metaplay.Core.Client
{
    [AnalyticsEvent(AnalyticsEventCodesCore.ClientConnectionFailure, displayName: "Connection Failure", docString: AnalyticsEventDocsCore.ClientConnectionFailure)]
    public class ClientEventConnectionFailure : ClientEventBase
    {
        /// <summary>
        /// Technical but human-readable string describing the error.
        /// Same as ConnectionLostEvent.TechnicalErrorString.
        ///
        /// For example: logic_version_client_too_old
        /// </summary>
        [MetaMember(1)] public string   TechnicalErrorString    { get; private set; }
        /// <summary>
        /// Optional info augmenting <see cref="TechnicalErrorString"/>.
        /// Same as ConnectionLostEvent.ExtraTechnicalInfo.
        ///
        /// For example: client_5_to_6_server_7_to_8
        /// </summary>1
        [MetaMember(2)] public string   ExtraTechnicalInfo      { get; private set; }
        /// <summary>
        /// Numerical error code corresponding to <see cref="TechnicalErrorString"/>.
        /// Same as ConnectionLostEvent.TechnicalErrorCode.
        ///
        /// For example: 2400
        /// </summary>
        [MetaMember(3)] public int      TechnicalErrorCode      { get; private set; }
        /// <summary>
        /// Corresponds to the client-side ConnectionLostReason enum,
        /// but stringified to avoid backwards compatibility concerns.
        /// Provides a less detailed reason than <see cref="TechnicalErrorString"/>.
        ///
        /// For example: ClientVersionTooOld
        /// </summary>
        [MetaMember(4)] public string   PlayerFacingReason      { get; private set; }

        // \todo Something from NetworkDiagnosticReport?

        ClientEventConnectionFailure(){ }
        public ClientEventConnectionFailure(string technicalErrorString, string extraTechnicalInfo, int technicalErrorCode, string playerFacingReason)
        {
            TechnicalErrorString = technicalErrorString;
            ExtraTechnicalInfo = extraTechnicalInfo;
            TechnicalErrorCode = technicalErrorCode;
            PlayerFacingReason = playerFacingReason;
        }
    }
}
