// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.League
{
    public interface IDivisionModelClientListenerCore
    {
        /// <summary>
        /// Called when the season concludes. The division will not accept score updates after this.
        /// </summary>
        void OnSeasonConcluded();
    }

    public interface IDivisionModelServerListenerCore
    {
        void OnSeasonDebugConcluded(); // \todo remove? Just used for testing now.
        void OnSeasonDebugEnded();
    }
}
