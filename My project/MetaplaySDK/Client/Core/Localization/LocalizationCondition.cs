// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;

namespace Metaplay.Server
{
    public class LocalizationsEnabledCondition : MetaplayFeatureEnabledConditionAttribute
    {
        public override bool IsEnabled => MetaplayCore.Options.FeatureFlags.EnableLocalizations;
    }
}
