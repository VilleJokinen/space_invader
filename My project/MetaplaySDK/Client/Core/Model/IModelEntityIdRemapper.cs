// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Threading.Tasks;

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Interface for EntityId remapping logic, i.e. changing entityId references from
    /// one set to another. This is used when Models are copied with new set of Ids, such
    /// when importing Entity Archives.
    /// </summary>
    public interface IModelEntityIdRemapper
    {
        /// <summary>
        /// Gets the new EntityId for an old EntityId.
        /// </summary>
        Task<EntityId> RemapEntityIdAsync(EntityId originalId);
    }
}
