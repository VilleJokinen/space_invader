// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Threading.Tasks;

namespace Metaplay.BotClient
{
    public sealed class BotSubClientServices : IMetaplaySubClientServices
    {
        public IMessageDispatcher MessageDispatcher { get; }
        public MetaplayClientStore ClientStore { get; }
        public ITimelineHistory TimelineHistory => null;
        public bool EnableConsistencyChecks { get; }

        IMetaLogger _logger;

        public BotSubClientServices(IMessageDispatcher messageDispatcher, MetaplayClientStore clientStore, bool enableConsistencyChecks, IMetaLogger logger)
        {
            MessageDispatcher = messageDispatcher;
            ClientStore = clientStore;
            EnableConsistencyChecks = enableConsistencyChecks;
            _logger = logger;
        }

        public LogChannel CreateLogChannel(string name)
        {
            return new LogChannel(name, _logger, MetaLogger.MetaLogLevelSwitch);
        }

        public void DefaultHandleConfigFetchFailed(Exception configLoadError)
        {
            throw configLoadError;
        }

        public void DefaultHandleEntityTimelineUpdateFailed()
        {
            throw new InvalidOperationException("Entity timeline update failed.");
        }

        public async Task<ISharedGameConfig> GetConfigAsync(ContentHash configVersion, ContentHash patchesVersion, OrderedDictionary<PlayerExperimentId, ExperimentVariantId> experimentAssignment)
        {
            return await BotGameConfigProvider.Instance.GetSpecializedGameConfigAsync(configVersion, patchesVersion, experimentAssignment);
        }
    }
}
