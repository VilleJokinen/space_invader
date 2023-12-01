// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.MultiplayerEntity.Messages;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Player Experiment Membership information. This contains the validated (effective)
    /// information of an experiment the player is (effectively) a member of. Note that if
    /// a player is a member of an invalid or disabled experiment or is assigned into an
    /// invalid or disabled variant, the experiment (variant) is not effectively enabled
    /// and there is no <see cref="ExperimentMembershipStatus"/>.
    /// </summary>
    public readonly struct ExperimentMembershipStatus
    {
        /// <summary>
        /// The Id of the experiment group.
        /// </summary>
        public readonly PlayerExperimentId ExperimentId;

        /// <summary>
        /// The assigned analytics id for this experiment.
        /// </summary>
        public readonly string ExperimentAnalyticsId;

        /// <summary>
        /// The Id of the variant group the player was assigned in. If the player is assigned
        /// into the control group, the value is <c>null</c>.
        /// </summary>
        public readonly ExperimentVariantId VariantId;

        /// <summary>
        /// The assigned analytics id for this variant in this experiment.
        /// </summary>
        public readonly string VariantAnalyticsId;

        /// <summary>
        /// True, if player is assigned into the control group in this experiment.
        /// </summary>
        public bool IsInControlGroup => VariantId == null;

        ExperimentMembershipStatus(PlayerExperimentId experimentId, string experimentAnalyticsId, ExperimentVariantId variantId, string variantAnalyticsId)
        {
            ExperimentId = experimentId;
            ExperimentAnalyticsId = experimentAnalyticsId;
            VariantId = variantId;
            VariantAnalyticsId = variantAnalyticsId;
        }

        public static ExperimentMembershipStatus FromSessionInfo(EntityActiveExperiment sessionExperiment)
        {
            return new ExperimentMembershipStatus(
                experimentId:           sessionExperiment.ExperimentId,
                experimentAnalyticsId:  sessionExperiment.ExperimentAnalyticsId,
                variantId:              sessionExperiment.VariantId,
                variantAnalyticsId:     sessionExperiment.VariantAnalyticsId);
        }
    }
}
