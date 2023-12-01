using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers;

public static class GameDataControllerUtility
{
    public static (GameDataControllerUtility.GameDataBuildStatus, string) GetConfigStatus(PersistedGameData config, Dictionary<MetaGuid, BackgroundTaskStatus> statuses)
    {
        if (!string.IsNullOrEmpty(config.VersionHash))
        {
            return (GameDataControllerUtility.GameDataBuildStatus.Succeeded, null);
        }
        else if (config.FailureInfo != null)
        {
            return (GameDataControllerUtility.GameDataBuildStatus.Failed, config.FailureInfo);
        }
        try
        {
            if (config.TaskId != null && statuses.TryGetValue(MetaGuid.Parse(config.TaskId), out BackgroundTaskStatus taskStatus))
            {
                if (!taskStatus.Completed)
                    return (GameDataControllerUtility.GameDataBuildStatus.Building, null);
                if (taskStatus.Failure != null)
                    return (GameDataControllerUtility.GameDataBuildStatus.Failed, taskStatus.Failure);
            }
            return (GameDataControllerUtility.GameDataBuildStatus.Failed, "Build task was probably interrupted by a server shutdown or crash. The task was neither completed nor found among the ongoing tasks.");
        }
        catch (Exception ex)
        {
            return (GameDataControllerUtility.GameDataBuildStatus.Failed, ex.ToString());
        }
    }

    public static async Task<T> GetPersistedGameDataByIdStringOr404Async<T>(string idString, MetaGuid activeId) where T : PersistedGameData
    {
        MetaGuid configId;
        if (idString == "$active")
        {
            // The special string "$active" means "use the active game config version"
            configId = activeId;
        }
        else
        {
            configId = MetaplayController.ParseMetaGuidStr(idString);
        }

        MetaDatabase db        = MetaDatabase.Get(QueryPriority.Normal);
        T            persisted = await db.TryGetAsync<T>(configId.ToString());
        if (persisted == null)
            throw new Exceptions.MetaplayHttpException(404, "Game data not found.", $"Cannot find {typeof(T)} with id {configId}.");
        return persisted;
    }

    [MetaSerializable]
    public struct GameDataEditableProperties
    {
        [MetaMember(1)] public string Name        { get; set; }
        [MetaMember(2)] public string Description { get; set; }
        [MetaMember(3)] public bool?  IsArchived  { get; set; }

        public GameDataEditableProperties FillEmpty(GameDataEditableProperties other)
        {
            GameDataEditableProperties ret = this;
            if (Name == null)
                ret.Name = other.Name;
            if (Description == null)
                ret.Description = other.Description;
            if (!IsArchived.HasValue)
                ret.IsArchived = other.IsArchived;
            return ret;
        }

        public IEnumerable<string> EditedProperties(GameDataEditableProperties oldValues)
        {
            if (oldValues.Name != Name)
                yield return nameof(Name);
            if (oldValues.Description != Description)
                yield return nameof(Description);
            if (oldValues.IsArchived != IsArchived)
                yield return nameof(IsArchived);
        }

        public string GetSummary(string type, GameDataEditableProperties oldValues)
        {
            IEnumerable<string> fields = EditedProperties(oldValues);
            if (!fields.Any())
                return $"{type} id edited, no changes.";
            return $"{type} id updated {MaybeFormatMultiple(string.Join(", ", fields), fields.Count() > 1)}";
        }

        string MaybeFormatMultiple(string content, bool multiple)
        {
            return multiple ? $"({content})" : content;
        }
    }

    public struct GameDataIdInput
    {
        public MetaGuid Id { get; private set; }
    }

    public enum GameDataBuildStatus
    {
        Building,
        Succeeded,
        Failed,
    };
}
