// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Metaplay.Server.ServerAnalyticsEvents
{
    [AnalyticsEvent(AnalyticsEventCodesCore.ServerExperimentInfo, displayName: "Experiment Updated", docString: AnalyticsEventDocsCore.ServerExperimentInfo)]
    public class ServerEventExperimentInfo : ServerEventBase
    {
        [MetaMember(1)] public PlayerExperimentId   ExperimentId            { get; private set; }
        [MetaMember(2)] public string               ExperimentAnalyticsId   { get; private set; }
        [MetaMember(3)] public bool                 IsActive                { get; private set; }
        [MetaMember(4)] public bool                 IsRollingOut            { get; private set; }
        [MetaMember(5)] public string               DisplayName             { get; private set; }
        [MetaMember(6)] public string               Description             { get; private set; }

        public override string EventDescription => $"{ExperimentId} updated.";

        ServerEventExperimentInfo(){ }
        public ServerEventExperimentInfo(PlayerExperimentId experimentId, string experimentAnalyticsId, bool isActive, bool isRollingOut, string displayName, string description)
        {
            ExperimentId = experimentId;
            ExperimentAnalyticsId = experimentAnalyticsId;
            IsActive = isActive;
            IsRollingOut = isRollingOut;
            DisplayName = displayName;
            Description = description;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.ServerVariantInfo, displayName: "Experiment Variant Updated", docString: AnalyticsEventDocsCore.ServerVariantInfo)]
    public class ServerEventExperimentVariantInfo : ServerEventBase
    {
        [MetaMember(1)] public PlayerExperimentId   ExperimentId            { get; private set; }
        [MetaMember(2)] public string               ExperimentAnalyticsId   { get; private set; }
        [MetaMember(3)] public ExperimentVariantId  VariantId               { get; private set; }
        [MetaMember(4)] public string               VariantAnalyticsId      { get; private set; }

        public override string EventDescription
        {
            get
            {
                if (VariantId != null)
                    return $"{ExperimentId} variant {VariantId} updated.";
                else
                    return $"{ExperimentId} control variant updated.";
            }
        }

        ServerEventExperimentVariantInfo(){ }
        public ServerEventExperimentVariantInfo(PlayerExperimentId experimentId, string experimentAnalyticsId, ExperimentVariantId variantId, string variantAnalyticsId)
        {
            ExperimentId = experimentId;
            ExperimentAnalyticsId = experimentAnalyticsId;
            VariantId = variantId;
            VariantAnalyticsId = variantAnalyticsId;
        }
    }
}
