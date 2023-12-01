// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    /// <summary>
    /// Default client services implemenation for the Unity client.
    /// </summary>
    public class MetaplayUnitySubClientServices : IMetaplaySubClientServices
    {
        public IMessageDispatcher MessageDispatcher => MetaplaySDK.MessageDispatcher;
        public ITimelineHistory TimelineHistory => MetaplaySDK.TimelineHistory;
        public MetaplayClientStore ClientStore { get; private set; }
        public bool EnableConsistencyChecks { get; private set; }

        public MetaplayUnitySubClientServices(MetaplayClientStore clientStore, bool enableConsistencyChecks)
        {
            ClientStore = clientStore;
            EnableConsistencyChecks = enableConsistencyChecks;
        }

        public void DefaultHandleConfigFetchFailed(Exception configLoadError)
        {
            MetaplaySDK.Connection.CloseWithError(flushEnqueuedMessages: true, new Metaplay.Unity.ConnectionStates.TransientError.ConfigFetchFailed(configLoadError, Metaplay.Unity.ConnectionStates.TransientError.ConfigFetchFailed.FailureSource.ResourceFetch));
        }

        public void DefaultHandleEntityTimelineUpdateFailed()
        {
            MetaplaySDK.Connection.CloseWithError(flushEnqueuedMessages: true, new Metaplay.Unity.ConnectionStates.TerminalError.Unknown());
        }

        public Task<ISharedGameConfig> GetConfigAsync(ContentHash configVersion, ContentHash patchesVersion, OrderedDictionary<PlayerExperimentId, ExperimentVariantId> experimentAssignment)
        {
            return MetaplaySDK.Connection.GetSpecializedGameConfigAsync(configVersion, patchesVersion, experimentAssignment);
        }

        public LogChannel CreateLogChannel(string name)
        {
            return MetaplaySDK.Logs.CreateChannel(name);
        }
    }
}
