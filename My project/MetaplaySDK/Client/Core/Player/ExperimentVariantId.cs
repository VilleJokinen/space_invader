// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System.ComponentModel;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Identifier for an Variant in an Player Experiment. By convention, null instance refers to the control group variant.
    /// </summary>
    [MetaSerializable]
    public class ExperimentVariantId : StringId<ExperimentVariantId>
    {
    }
}
