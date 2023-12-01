// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Player;

namespace Metaplay.Core.Config
{
    public class ServerGameConfigBase : GameConfigBase, IServerGameConfig
    {
        public const string PlayerExperimentsEntryName = "PlayerExperiments";

        static readonly IGameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo>   _defaultPlayerExperiments     = new EmptyGameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo>();
        IGameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo>                   _playerExperimentsIntegration = null;
        IGameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo> IServerGameConfig.PlayerExperiments => _playerExperimentsIntegration;

        protected override sealed void RegisterSDKIntegrations()
        {
            _playerExperimentsIntegration = RegisterIntegration(PlayerExperimentsEntryName, _defaultPlayerExperiments);
        }
    }

    public abstract class LegacyServerGameConfigBase : ServerGameConfigBase
    {
        [GameConfigEntry(PlayerExperimentsEntryName)]
        [GameConfigSyntaxAdapter(headerReplaces: new string[] { "ExperimentId -> ExperimentId #key" })]
        public GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo> PlayerExperiments { get; protected set; }
    }
}
