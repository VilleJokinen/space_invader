// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.League.Player
{
    /// <summary>
    /// Base class for Division actions that affect Player Divisions.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    [MetaSerializable]
    public abstract class PlayerDivisionActionBase : DivisionActionBase
    {
        public sealed override MetaActionResult InvokeExecute(IDivisionModel model, bool commit)
        {
            return InvokeExecute((IPlayerDivisionModel)model, commit);
        }

        public abstract MetaActionResult InvokeExecute(IPlayerDivisionModel model, bool commit);
    }
}
