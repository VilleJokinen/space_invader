// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    public interface IOfflineServer : IMetaIntegrationConstructible<IOfflineServer>
    {
        ConfigArchive GameConfigArchive { get; }

        Task InitializeAsync(MetaplayOfflineOptions offlineOptions);
        void RegisterTransport(OfflineServerTransport transport);
        void Update();
        void HandleMessage(MetaMessage message);
        void TryPersistState();
        void SetPlayerJournal(IClientPlayerModelJournal playerJournal);
        void SetListeners(MetaplayClientStore clientStore);
    }
}
