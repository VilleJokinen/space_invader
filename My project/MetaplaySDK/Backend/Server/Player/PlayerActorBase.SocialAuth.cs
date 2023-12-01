// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Server.Authentication;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    public abstract partial class PlayerActorBase<TModel, TPersisted>
    {
        /// <summary>
        /// AdminAPI. Copy auth from one player to another. Immediately persists the player after completion
        /// </summary>
        [EntityAskHandler]
        async Task<EntityAskOk> HandlePlayerCopyAuthFromRequest(PlayerCopyAuthFromRequest request)
        {
            // Attach all auth to this player
            foreach ((AuthenticationKey authKey, PlayerAuthEntryBase entry) in request.AttachedAuthMethods)
            {
                entry.RefreshAttachedAt(MetaTime.Now);
                ExecuteServerActionImmediately(new PlayerAttachAuthentication(authKey, entry));
            }

            // Immediately persist the new player state
            await PersistStateIntermediate();

            // Switch all auth methods to this player
            foreach ((AuthenticationKey authKey, PlayerAuthEntryBase entry) in request.AttachedAuthMethods)
            {
                if (authKey.Platform == AuthenticationPlatform.DeviceId)
                    await Authenticator.UpdateDeviceToPlayerMappingAsync(deviceId: authKey.Id, _entityId);
                else
                    await Authenticator.UpdateSocialAuthenticationAsync(authKey: authKey, _entityId);
            }

            return EntityAskOk.Instance;
        }

        /// <summary>
        /// AdminAPI. Detach all auth from a player. Immediately persists the player after completion
        /// </summary>
        [EntityAskHandler]
        async Task<EntityAskOk> HandlePlayerDetachAllAuthRequest(PlayerDetachAllAuthRequest _)
        {
            if (Model.AttachedAuthMethods.Count > 0)
            {
                // Detach all auth from this player
                // todo [paul] might be a good idea to keep the data but flag it as detached instead of removing it
                AuthenticationKey[] authKeysCopy = Model.AttachedAuthMethods.Keys.ToArray();
                foreach (AuthenticationKey authKey in authKeysCopy)
                    ExecuteServerActionImmediately(new PlayerDetachAuthentication(authKey));

                // Player can no longer connect, so kick them out now
                KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);

                // Ensure that state is persisted immediately
                await PersistStateIntermediate();
            }

            return EntityAskOk.Instance;
        }

        /// <summary>
        /// AdminAPI. Remove a single auth from a player. Immediately persists the player after completion
        /// </summary>
        [EntityAskHandler]
        async Task<PlayerRemoveSingleAuthResponse> HandlePlayerRemoveSingleAuthRequest(PlayerRemoveSingleAuthRequest request)
        {
            // Detach single auth from this player
            MetaActionResult result = ExecuteServerActionImmediately(new PlayerDetachAuthentication(request.Key));
            if (result == MetaActionResult.Success)
            {
                // Kick the player out in case they are logged in with this auth method
                KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);

                // Ensure that state is persisted immediately
                await PersistStateIntermediate();

                // Remove any social auth entries
                await Authenticator.RemoveAuthenticationEntryAsync(request.Key);

                // Finally, log the client out from Facebook if this was a Facebook account
                if (request.Key.Platform == AuthenticationPlatform.FacebookLogin)
                {
                    bool wasSuccess = await FacebookLoginService.Instance.RevokeUserLoginAsync(request.Key.Id);
                    if (!wasSuccess)
                        _log.Warning("Could not revoke Facebook login for player");
                }
            }

            return new PlayerRemoveSingleAuthResponse(result == MetaActionResult.Success);
        }

        /// <summary>
        /// Facebook webhook. Remove certain (revoked) facebook auth from a player. Immediately persists the player before replying.
        /// </summary>
        [EntityAskHandler]
        async Task<InternalPlayerFacebookAuthenticationRevokedResponse> HandleInternalPlayerFacebookAuthenticationRevokedRequest(InternalPlayerFacebookAuthenticationRevokedRequest request)
        {
            if (Model.AttachedAuthMethods.ContainsKey(request.AuthenticationKey))
            {
                PlayerEventFacebookAuthenticationRevoked.RevocationSource eventSource;

                switch (request.Source)
                {
                    case InternalPlayerFacebookAuthenticationRevokedRequest.RevocationSource.DataDeletionRequest:
                    {
                        _log.Info("Handling Facebook Data Deletion request. Detaching fb user from this player. ConfirmationCode={ConfirmationCode}.", request.ConfirmationCode);
                        eventSource = PlayerEventFacebookAuthenticationRevoked.RevocationSource.DataDeletionRequest;
                        break;
                    }

                    case InternalPlayerFacebookAuthenticationRevokedRequest.RevocationSource.DeauthorizationRequest:
                    {
                        _log.Info("Handling Facebook User Deauthorization request. Detaching fb user from this player.");
                        eventSource = PlayerEventFacebookAuthenticationRevoked.RevocationSource.DeauthorizationRequest;
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"unknown source {request.Source}");
                }

                ExecuteServerActionImmediately(new PlayerDetachAuthentication(request.AuthenticationKey));
                Model.EventStream.Event(new PlayerEventFacebookAuthenticationRevoked(eventSource, request.ConfirmationCode));
                await PersistStateIntermediate();
            }
            else
            {
                _log.Warning("Received Facebook Data Deletion request, but the fb user was not attached the player. Ignored. ConfirmationCode={ConfirmationCode}.", request.ConfirmationCode);
            }

            return InternalPlayerFacebookAuthenticationRevokedResponse.Instance;
        }

        /// <summary>
        /// User self-serve auth method conflict resolution (when deviceId is moved).
        /// </summary>
        [EntityAskHandler]
        async Task<InternalPlayerResolveDeviceIdAuthConflictResponse> HandleInternalPlayerResolveDeviceIdAuthConflictRequest(InternalPlayerResolveDeviceIdAuthConflictRequest request)
        {
            InternalPlayerResolveDeviceIdAuthConflictResponse result;

            switch (request.Operation)
            {
                case InternalPlayerResolveDeviceIdAuthConflictRequest.ResolveOperation.DeviceMigrationSource:
                {
                    _log.Info("Social conflict resolution. Device {DeviceId} was removed, and moved to {PlayerId} to sync with [{SocialIds}].", request.DeviceIdKey, request.OtherPlayerId, string.Join<AuthenticationKey>(", ", request.ContributedSocialAccounts));

                    result = new InternalPlayerResolveDeviceIdAuthConflictResponse(removedAuthEntryOrNull: (PlayerDeviceIdAuthEntry)Model.AttachedAuthMethods.GetValueOrDefault(request.DeviceIdKey));

                    // Device was detached from this player
                    ExecuteServerActionImmediately(new PlayerDetachAuthentication(request.DeviceIdKey));

                    // Mark event for all social auth keys that contributed
                    foreach (AuthenticationKey socialKey in request.ContributedSocialAccounts)
                        Model.EventStream.Event(new PlayerEventSocialAuthConflictResolved(PlayerEventSocialAuthConflictResolved.ResolutionOperation.DeviceMigrationSource, request.DeviceIdKey, socialKey, request.OtherPlayerId));
                    break;
                }

                case InternalPlayerResolveDeviceIdAuthConflictRequest.ResolveOperation.DeviceMigrationDestination:
                {
                    _log.Info("Social conflict resolution. Device {DeviceId} was added (and removed from {PlayerId}) to sync with [{SocialIds}].", request.DeviceIdKey, request.OtherPlayerId, string.Join<AuthenticationKey>(", ", request.ContributedSocialAccounts));

                    result = new InternalPlayerResolveDeviceIdAuthConflictResponse(removedAuthEntryOrNull: null);

                    // Device was attached to this player
                    PlayerDeviceIdAuthEntry deviceIdAuth = request.AddedDeviceIdAuthOrNull ?? new PlayerDeviceIdAuthEntry(attachedAt: MetaTime.Now, deviceModel: "unknown");
                    deviceIdAuth.RefreshAttachedAt(MetaTime.Now);
                    ExecuteServerActionImmediately(new PlayerAttachAuthentication(request.DeviceIdKey, deviceIdAuth));

                    // Mark event for all social auth keys that contributed
                    foreach (AuthenticationKey socialKey in request.ContributedSocialAccounts)
                        Model.EventStream.Event(new PlayerEventSocialAuthConflictResolved(PlayerEventSocialAuthConflictResolved.ResolutionOperation.DeviceMigrationDestination, request.DeviceIdKey, socialKey, request.OtherPlayerId));
                    break;
                }

                default:
                    throw new InvalidOperationException($"unknown resolution operation: {request.Operation}");
            }

            await PersistStateIntermediate();
            return result;
        }

        /// <summary>
        /// User self-serve auth method conflict resolution (when moving social auths).
        /// Also used for silent migrations in social authentication (i.e. when a single claim resolves to multiple different accounts and the result is canonized).
        /// </summary>
        [EntityAskHandler]
        async Task<InternalPlayerResolveSocialAuthConflictResponse> HandleInternalPlayerResolveSocialAuthConflictRequest(InternalPlayerResolveSocialAuthConflictRequest request)
        {
            InternalPlayerResolveSocialAuthConflictResponse result;

            switch (request.Operation)
            {
                case InternalPlayerResolveSocialAuthConflictRequest.ResolveOperation.SocialAuthMigrationSource:
                {
                    // Social auth detached from this player
                    OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> removedSocialKeys = new OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase>();
                    foreach (AuthenticationKey socialKey in request.RemovedSocialKeys)
                    {
                        _log.Info("Social conflict resolution. Social auth {SocialId} was detached and moved to {PlayerId}.", socialKey, request.OtherPlayerId);
                        removedSocialKeys.Add(socialKey, Model.AttachedAuthMethods.GetValueOrDefault(socialKey));

                        ExecuteServerActionImmediately(new PlayerDetachAuthentication(socialKey));
                        Model.EventStream.Event(new PlayerEventSocialAuthConflictResolved(PlayerEventSocialAuthConflictResolved.ResolutionOperation.SocialMigrationSource, deviceIdKey: null, socialKey, request.OtherPlayerId));
                    }

                    // Respond with the removed auth entries
                    result = new InternalPlayerResolveSocialAuthConflictResponse(removedSocialKeys);
                    break;
                }

                case InternalPlayerResolveSocialAuthConflictRequest.ResolveOperation.SocialAuthMigrationDestination:
                {
                    // Social auth attached from this player
                    foreach ((AuthenticationKey socialKey, PlayerAuthEntryBase existingEntryOrNull) in request.AddedSocialKeys)
                    {
                        _log.Info("Social conflict resolution. Social auth {SocialId} was attached. (Migrated from {PlayerId}).", socialKey, request.OtherPlayerId);

                        // Use the migrated entry if such is available. If not, create a new default.
                        PlayerAuthEntryBase entry;
                        if (existingEntryOrNull != null)
                        {
                            entry = existingEntryOrNull;
                            entry.RefreshAttachedAt(MetaTime.Now);
                        }
                        else
                        {
                            entry = CreateSocialAuthenticationEntry(socialKey);
                        }

                        ExecuteServerActionImmediately(new PlayerAttachAuthentication(socialKey, entry));
                        Model.EventStream.Event(new PlayerEventSocialAuthConflictResolved(PlayerEventSocialAuthConflictResolved.ResolutionOperation.SocialMigrationDestination, deviceIdKey: null, socialKey, request.OtherPlayerId));
                    }

                    result = new InternalPlayerResolveSocialAuthConflictResponse(removedSocialKeys: null);
                    break;
                }

                case InternalPlayerResolveSocialAuthConflictRequest.ResolveOperation.SocialAuthAddNewMigrationKey:
                {
                    // Social auth attached from existing key
                    foreach ((AuthenticationKey socialKey, PlayerAuthEntryBase existingEntryOrNull) in request.AddedSocialKeys)
                    {
                        _log.Info("Social conflict migration key add. Social auth {SocialId} was attached.", socialKey);

                        // Use the migrated entry if such is available. If not, create a new default.
                        PlayerAuthEntryBase entry;
                        if (existingEntryOrNull != null)
                        {
                            entry = existingEntryOrNull;
                            entry.RefreshAttachedAt(MetaTime.Now);
                        }
                        else
                        {
                            entry = CreateSocialAuthenticationEntry(socialKey);
                        }

                        ExecuteServerActionImmediately(new PlayerAttachAuthentication(socialKey, entry));
                        Model.EventStream.Event(new PlayerEventSocialAuthConflictResolved(PlayerEventSocialAuthConflictResolved.ResolutionOperation.SocialMigrationKeyAdded, deviceIdKey: null, socialKey, EntityId.None));
                    }

                    result = new InternalPlayerResolveSocialAuthConflictResponse(removedSocialKeys: null);
                    break;
                }

                default:
                    throw new InvalidOperationException($"unknown resolution operation: {request.Operation}");
            }

            await PersistStateIntermediate();
            return result;
        }

        /// <summary>
        /// User self serve social login happy path.
        /// </summary>
        [EntityAskHandler]
        EntityAskOk HandleInternalPlayerAddNewSocialAccountRequest(InternalPlayerAddNewSocialAccountRequest request)
        {
            foreach (AuthenticationKey authKey in request.SocialKeys.AllAuthenticationKeys)
                ExecuteServerActionImmediately(new PlayerAttachAuthentication(authKey, CreateSocialAuthenticationEntry(authKey)));

            return EntityAskOk.Instance;
        }

        /// <summary>
        /// User self serve social detach.
        /// </summary>
        [MessageHandler]
        async Task HandleSocialAuthenticateDetach(SocialAuthenticateDetach detach)
        {
            _log.Info("Detach of social account {AuthKey} requested.", detach.AuthKey);

            // Don't allow detaching DeviceIds (only actual social authentications)
            if (detach.AuthKey.Platform == AuthenticationPlatform.DeviceId)
                throw new InvalidOperationException($"Cannot detach DeviceId: {detach.AuthKey}");

            // Check that AuthEntry exists and is actually attached to this player
            PersistedAuthenticationEntry authEntry = await Authenticator.TryGetAuthenticationEntryAsync(detach.AuthKey);
            if (authEntry == null)
                throw new InvalidOperationException($"Trying to detach non-existent authentication method: {detach.AuthKey}");
            else if (authEntry.PlayerEntityId != _entityId)
                throw new InvalidOperationException($"Trying to detach non-owned authentication method: {authEntry.AuthKey}");

            // Detach the authentication method from PlayerModel
            ExecuteServerActionImmediately(new PlayerDetachAuthentication(detach.AuthKey));
            await PersistStateIntermediate();

            // Remove the social attachment from database
            await Authenticator.RemoveAuthenticationEntryAsync(detach.AuthKey);
        }

        /// <summary>
        /// Attempt to add a new social account to the player, such that the new account can be used to log in.
        /// On success this updates the Auth tables.
        /// </summary>
        [EntityAskHandler]
        async Task<InternalPlayerLoginWithNewSocialAccountResponse> HandleInternalPlayerLoginWithNewSocialAccountRequest(InternalPlayerLoginWithNewSocialAccountRequest request)
        {
            // Check if we can attach the account

            // No blockers, add to state

            foreach (AuthenticationKey addedKey in request.SocialKeys.AllAuthenticationKeys)
                ExecuteServerActionImmediately(new PlayerAttachAuthentication(addedKey, CreateSocialAuthenticationEntry(addedKey)));

            // Save to auth table
            await Authenticator.StoreSocialAuthenticationEntryAsync(request.SocialKeys, _entityId);

            // Save the state
            await PersistStateIntermediate();

            return new InternalPlayerLoginWithNewSocialAccountResponse(InternalPlayerLoginWithNewSocialAccountResponse.ResultCode.SocialAccountAdded);
        }

        /// <summary>
        /// Detaches and deletes authentication records of this player.
        /// </summary>
        async Task RemoveAllAuthenticationMethodsAsync()
        {
            AuthenticationKey[] authKeysCopy = Model.AttachedAuthMethods.Keys.ToArray();
            foreach (AuthenticationKey authKey in authKeysCopy)
            {
                ExecuteServerActionImmediately(new PlayerDetachAuthentication(authKey));
                await Authenticator.RemoveAuthenticationEntryAsync(authKey);
            }
        }
    }
}
