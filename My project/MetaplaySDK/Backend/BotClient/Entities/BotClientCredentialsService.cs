// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Network;
using System;
using System.Threading.Tasks;

namespace Metaplay.BotClient
{
    class BotClientCredentialsService : ISessionCredentialService
    {
        public EntityId ExpectedPlayerId { get; }

        public BotClientCredentialsService(EntityId playerId)
        {
            ExpectedPlayerId = playerId;
        }

        public Task<ISessionCredentialService.LoginMethod> GetCurrentLoginMethodAsync()
        {
            // Use deterministic randomization for deviceId/authToken
            Random rnd = new Random((int)MiniMD5.ComputeMiniMD5(ExpectedPlayerId.ToString())); // \todo [petri] just use _playerId.Value as seed? breaks compat, though
            string deviceId = SecureTokenUtil.GenerateRandomStringTokenUnsafe(rnd, DeviceAuthentication.DeviceIdLength);
            string authToken = SecureTokenUtil.GenerateRandomStringTokenUnsafe(rnd, DeviceAuthentication.AuthTokenLength);

            ISessionCredentialService.GuestCredentials botCredentials = new ISessionCredentialService.GuestCredentials(deviceId, authToken, ExpectedPlayerId);
            return Task.FromResult<ISessionCredentialService.LoginMethod>(new ISessionCredentialService.BotLoginMethod(botCredentials));
        }

        public Task<EntityId> InitializeAsync() => Task.FromResult<EntityId>(ExpectedPlayerId);
        Task ISessionCredentialService.OnGuestAccountCreatedAsync(ISessionCredentialService.GuestCredentials guestCredentials) => Task.CompletedTask;
        Task ISessionCredentialService.OnPlayerIdUpdatedAsync(AuthenticationPlatform platform, EntityId playerId) => Task.CompletedTask;
    }
}
