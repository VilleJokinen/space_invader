// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Network;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    public class UnityCredentialService : ISessionCredentialService
    {
        ISessionCredentialService.GuestCredentials? _guestCredentials;

        public async Task<EntityId> InitializeAsync()
        {
            GuestCredentials guestCredentials = await CredentialsStore.TryGetGuestCredentialsAsync();

            if (guestCredentials == null)
            {
                _guestCredentials = null;
                return EntityId.None;
            }

            _guestCredentials = new ISessionCredentialService.GuestCredentials(guestCredentials.DeviceId, guestCredentials.AuthToken, guestCredentials.PlayerId);
            return _guestCredentials.Value.PlayerIdHint;
        }

        public Task<ISessionCredentialService.LoginMethod> GetCurrentLoginMethodAsync()
        {
            // If there are no guest credentials, create new.
            if (_guestCredentials == null)
                return Task.FromResult<ISessionCredentialService.LoginMethod>(new ISessionCredentialService.NewGuestAccountLoginMethod());

            // Otherwise, use the guest credentials
            return Task.FromResult<ISessionCredentialService.LoginMethod>(new ISessionCredentialService.GuestAccountLoginMethod(_guestCredentials.Value));
        }

        public async Task OnGuestAccountCreatedAsync(ISessionCredentialService.GuestCredentials guestCredentials)
        {
            _guestCredentials = guestCredentials;

            // The created account PlayerId becomes the current
            MetaplaySDK.PlayerId = guestCredentials.PlayerIdHint;

            GuestCredentials guestCredentialsToSave = new GuestCredentials()
            {
                DeviceId = guestCredentials.DeviceId,
                AuthToken = guestCredentials.AuthToken,
                PlayerId = guestCredentials.PlayerIdHint,
            };
            await CredentialsStore.StoreGuestCredentialsAsync(guestCredentialsToSave);
        }

        public async Task OnPlayerIdUpdatedAsync(AuthenticationPlatform platform, EntityId playerId)
        {
            // Latest PlayerId change always becomes the current
            MetaplaySDK.PlayerId = playerId;

            // Sync playerId change to the persisted store
            if (platform == AuthenticationPlatform.DeviceId && _guestCredentials.HasValue)
            {
                GuestCredentials guestCredentialsToSave = new GuestCredentials()
                {
                    DeviceId = _guestCredentials.Value.DeviceId,
                    AuthToken = _guestCredentials.Value.AuthToken,
                    PlayerId = playerId,
                };
                await CredentialsStore.StoreGuestCredentialsAsync(guestCredentialsToSave);
            }
        }

        // For compatibility
        public ISessionCredentialService.GuestCredentials? TryGetGuestCredentials() => _guestCredentials;
    }

    class OfflineCredentialService : ISessionCredentialService
    {
        public Task<ISessionCredentialService.LoginMethod> GetCurrentLoginMethodAsync()
        {
            ISessionCredentialService.GuestCredentials offlineCredentials = new ISessionCredentialService.GuestCredentials("offlinedevice", "offlinetoken", DefaultOfflineServer.OfflinePlayerId);
            return Task.FromResult<ISessionCredentialService.LoginMethod>(new ISessionCredentialService.GuestAccountLoginMethod(offlineCredentials));
        }

        public Task<EntityId> InitializeAsync() => Task.FromResult<EntityId>(DefaultOfflineServer.OfflinePlayerId);
        public Task OnGuestAccountCreatedAsync(ISessionCredentialService.GuestCredentials guestCredentials) => Task.CompletedTask;
        public Task OnPlayerIdUpdatedAsync(AuthenticationPlatform platform, EntityId playerId) => Task.CompletedTask;
    }
}
