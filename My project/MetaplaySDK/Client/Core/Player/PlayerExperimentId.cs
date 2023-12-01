// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System.ComponentModel;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Identifier for an A/B testing Experiment.
    /// </summary>
    [MetaSerializable]
    public class PlayerExperimentId : StringId<PlayerExperimentId> { }
}
