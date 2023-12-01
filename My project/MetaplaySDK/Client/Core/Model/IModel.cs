// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Interface for all game Model classes.
    /// </summary>
    [MetaSerializable]
    public interface IModel : ISchemaMigratable
    {
        int LogicVersion { get; set; }

        IGameConfigDataResolver GetDataResolver();

        void Tick(IChecksumContext checksumCtx);
    }

    public interface IModel<TModel> : IModel
        where TModel : IModel<TModel>
    {
        IModelRuntimeData<TModel> GetRuntimeData();
    }
}
