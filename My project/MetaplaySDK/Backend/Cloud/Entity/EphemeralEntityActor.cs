// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;

namespace Metaplay.Cloud.Entity
{
    /// <summary>
    /// Entity actor, which has no persisted state (ie, the state is forgotten whenever
    /// the entity shuts down).
    /// </summary>
    public abstract class EphemeralEntityActor : EntityActor
    {
        protected EphemeralEntityActor(EntityId entityId) : base(entityId)
        {
        }

        protected override void PreStart()
        {
            base.PreStart();
        }
    }
}
