// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;

namespace Metaplay.Core.Activables
{
    public interface IMetaActivableInfo
    {
        MetaActivableParams ActivableParams { get; }
    }

    public interface IMetaActivableInfo<TId> : IMetaActivableInfo
    {
        TId                 ActivableId     { get; }
    }

    public interface IMetaActivableConfigData : IGameConfigData, IMetaActivableInfo
    {
        /// <summary> Display name for dashboard. </summary>
        string DisplayName { get; }
        /// <summary> Description for dashboard. </summary>
        string Description { get; }

        /// <summary>
        /// Additional optional short text shown for the activable in dashboard lists and such.
        /// Can be null if not needed.
        /// </summary>
        string DisplayShortInfo { get; }
    }

    public interface IMetaActivableConfigData<TKey> : IMetaActivableConfigData, IGameConfigData<TKey>, IMetaActivableInfo<TKey>
        // \note This IStringId constraint is not fundamentally necessary.
        //       It exists so that the collection of activation/consumption/etc counts for the dashboard
        //       can just use the `string` type for the activable id in certain data structures.
        //       Dealing with the concrete proper id types would be tricky as those types are
        //       not known by the SDK until runtime.
        //       #activable-id-type
        where TKey : IStringId
    {
    }
}
