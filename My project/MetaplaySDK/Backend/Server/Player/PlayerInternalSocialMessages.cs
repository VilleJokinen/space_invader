// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Authentication;
using System;

namespace Metaplay.Server
{
    /// <summary>
    /// Request that the player attaches all authentication from another player
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerCopyAuthFromRequest, MessageDirection.ServerInternal)]
    public class PlayerCopyAuthFromRequest : MetaMessage
    {
        public OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> AttachedAuthMethods { get; private set; }

        PlayerCopyAuthFromRequest() { }
        public PlayerCopyAuthFromRequest(OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> attachedAuthMethods)
        {
            AttachedAuthMethods = attachedAuthMethods;
        }
    }

    /// <summary>
    /// Request that the player detach all authentication
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerDetachAllAuthRequest, MessageDirection.ServerInternal)]
    public class PlayerDetachAllAuthRequest : MetaMessage
    {
        public PlayerDetachAllAuthRequest() { }
    }

    /// <summary>
    /// Request that the player detach single authentication
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerRemoveSingleAuthRequest, MessageDirection.ServerInternal)]
    public class PlayerRemoveSingleAuthRequest : MetaMessage
    {
        public AuthenticationKey Key { get; private set; }

        public PlayerRemoveSingleAuthRequest() { }
        public PlayerRemoveSingleAuthRequest(AuthenticationKey key)
        {
            Key = key;
        }
    }
    [MetaMessage(MessageCodesCore.PlayerRemoveSingleAuthResponse, MessageDirection.ServerInternal)]
    public class PlayerRemoveSingleAuthResponse : MetaMessage
    {
        public bool Success { get; private set; }

        public PlayerRemoveSingleAuthResponse() { }
        public PlayerRemoveSingleAuthResponse(bool success)
        {
            Success = success;
        }
    }

    [MetaMessage(MessageCodesCore.InternalPlayerFacebookAuthenticationRevokedRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerFacebookAuthenticationRevokedRequest : MetaMessage
    {
        [MetaSerializable]
        public enum RevocationSource
        {
            /// <summary>
            /// Data Deletion Request Callback
            /// </summary>
            DataDeletionRequest = 0,

            /// <summary>
            /// Deauthorize Callback URL
            /// </summary>
            DeauthorizationRequest = 1,
        }

        public RevocationSource     Source              { get; private set; }
        public AuthenticationKey    AuthenticationKey   { get; private set; }
        public string               ConfirmationCode    { get; private set; } // only set for Data Deletion

        InternalPlayerFacebookAuthenticationRevokedRequest() { }
        public InternalPlayerFacebookAuthenticationRevokedRequest(RevocationSource source, AuthenticationKey authenticationKey, string confirmationCode)
        {
            Source = source;
            AuthenticationKey = authenticationKey;
            ConfirmationCode = confirmationCode;
        }
    }

    [MetaMessage(MessageCodesCore.InternalPlayerFacebookAuthenticationRevokedResponse, MessageDirection.ServerInternal)]
    public class InternalPlayerFacebookAuthenticationRevokedResponse : MetaMessage
    {
        public static readonly InternalPlayerFacebookAuthenticationRevokedResponse Instance = new InternalPlayerFacebookAuthenticationRevokedResponse();
        public InternalPlayerFacebookAuthenticationRevokedResponse() { }
    }

    [MetaMessage(MessageCodesCore.InternalPlayerResolveSocialAuthConflictRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerResolveSocialAuthConflictRequest : MetaMessage
    {
        [MetaSerializable]
        public enum ResolveOperation
        {
            /// <summary>
            /// Social authentication is detached due to client choosing to sync it with another authentication method.
            /// <see cref="RemovedSocialKeys"/> is the social auth to be removed. <br/>
            /// <see cref="OtherPlayerId"/> is the id of the player the auth will be associated with. <br/>
            /// </summary>
            SocialAuthMigrationSource = 2,

            /// <summary>
            /// Social authentication is attached due to client choosing to sync it with another authentication method.
            /// <see cref="AddedSocialKeys"/> is the social auth to be added. <br/>
            /// <see cref="OtherPlayerId"/> is the id of the player the auth was previously associated with. <br/>
            /// </summary>
            SocialAuthMigrationDestination = 3,

            /// <summary>
            /// Social authentication is attached due existing social auth resolving both to the existing key but also to a migration key.
            /// <see cref="AddedSocialKeys"/> is the new migration auth to be added. <br/>
            /// <see cref="OtherPlayerId"/> is None. <br/>
            /// </summary>
            SocialAuthAddNewMigrationKey = 4,
        }
        public ResolveOperation                                             Operation           { get; private set; }

        /// <summary>
        /// Added authentication keys, and OPTIONAL auth entry to be attached. IF no existing auth entry is available the value for the key is null.
        /// </summary>
        public OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase>    AddedSocialKeys     { get; private set; }
        public AuthenticationKey[]                                          RemovedSocialKeys   { get; private set; }
        public EntityId                                                     OtherPlayerId       { get; private set; }

        InternalPlayerResolveSocialAuthConflictRequest() { }
        InternalPlayerResolveSocialAuthConflictRequest(ResolveOperation operation, OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> addedKeys, AuthenticationKey[] removedKeys, EntityId otherPlayerId)
        {
            if (!otherPlayerId.IsOfKind(EntityKindCore.Player) && otherPlayerId != EntityId.None)
                throw new ArgumentException("otherPlayerId must be a Player or None", nameof(otherPlayerId));

            Operation = operation;
            AddedSocialKeys = addedKeys;
            RemovedSocialKeys = removedKeys;
            OtherPlayerId = otherPlayerId;
        }

        public static InternalPlayerResolveSocialAuthConflictRequest ForSocialAuthMigrationSource(AuthenticatedSocialClaimKeys socialIdKeys, EntityId otherPlayerId)
        {
            return new InternalPlayerResolveSocialAuthConflictRequest(ResolveOperation.SocialAuthMigrationSource, addedKeys: null, socialIdKeys.AllAuthenticationKeys, otherPlayerId);
        }
        public static InternalPlayerResolveSocialAuthConflictRequest ForSocialAuthMigrationDestination(OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> addedSocialKeys, EntityId otherPlayerId)
        {
            return new InternalPlayerResolveSocialAuthConflictRequest(ResolveOperation.SocialAuthMigrationDestination, addedKeys: addedSocialKeys, removedKeys: null, otherPlayerId);
        }
        public static InternalPlayerResolveSocialAuthConflictRequest ForSocialAuthAddNewMigrationKey(OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> addedSocialKeys)
        {
            return new InternalPlayerResolveSocialAuthConflictRequest(ResolveOperation.SocialAuthAddNewMigrationKey, addedKeys: addedSocialKeys, removedKeys: null, EntityId.None);
        }
    }
    [MetaMessage(MessageCodesCore.InternalPlayerResolveSocialAuthConflictResponse, MessageDirection.ServerInternal)]
    public class InternalPlayerResolveSocialAuthConflictResponse : MetaMessage
    {
        /// <summary>
        /// The removed authentication keys that were requested to be removed and for each, the auth entry if it existed in the player. If the entry does not exist in the player model,
        /// the key-value pair is still present but the auth entry value will be null.
        /// </summary>
        public OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> RemovedSocialKeys { get; private set; }

        InternalPlayerResolveSocialAuthConflictResponse() { }
        public InternalPlayerResolveSocialAuthConflictResponse(OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> removedSocialKeys)
        {
            RemovedSocialKeys = removedSocialKeys;
        }
    }

    [MetaMessage(MessageCodesCore.InternalPlayerResolveDeviceIdAuthConflictRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerResolveDeviceIdAuthConflictRequest : MetaMessage
    {
        [MetaSerializable]
        public enum ResolveOperation
        {
            /// <summary>
            /// Device removed from the player due to client choosing to follow social auth. <br/>
            /// <see cref="DeviceIdKey"/> is the device Id to be removed. <br/>
            /// <see cref="ContributedSocialAccounts"/> is the social auth keys the client chose to follow. <br/>
            /// <see cref="OtherPlayerId"/> is the id of the player associated with social id. <br/>
            /// </summary>
            DeviceMigrationSource = 0,

            /// <summary>
            /// Device added to the player due to client choosing to follow social auth. <br/>
            /// <see cref="DeviceIdKey"/> is the device Id to be added. <br/>
            /// <see cref="AddedDeviceIdAuthOrNull"/> is the device auth to be added, or null if new default value should be added instead. <br/>
            /// <see cref="ContributedSocialAccounts"/> is the social auth keys the client chose to follow. <br/>
            /// <see cref="OtherPlayerId"/> is the id of the player previously associated with the device Id. <br/>
            /// </summary>
            DeviceMigrationDestination = 1,
        }
        public ResolveOperation             Operation                   { get; private set; }
        public AuthenticationKey            DeviceIdKey                 { get; private set; }
        public PlayerDeviceIdAuthEntry      AddedDeviceIdAuthOrNull     { get; private set; }
        public AuthenticationKey[]          ContributedSocialAccounts   { get; private set; }
        public EntityId                     OtherPlayerId               { get; private set; }

        InternalPlayerResolveDeviceIdAuthConflictRequest() { }
        InternalPlayerResolveDeviceIdAuthConflictRequest(ResolveOperation operation, AuthenticationKey deviceIdKey, PlayerDeviceIdAuthEntry addedDeviceIdAuthOrNull, AuthenticationKey[] contributedSocialAccounts, EntityId otherPlayerId)
        {
            if (!otherPlayerId.IsOfKind(EntityKindCore.Player) && otherPlayerId != EntityId.None)
                throw new ArgumentException("otherPlayerId must be a Player or None", nameof(otherPlayerId));

            Operation = operation;
            DeviceIdKey = deviceIdKey;
            AddedDeviceIdAuthOrNull = addedDeviceIdAuthOrNull;
            ContributedSocialAccounts = contributedSocialAccounts;
            OtherPlayerId = otherPlayerId;
        }

        public static InternalPlayerResolveDeviceIdAuthConflictRequest ForDeviceMigrationSource(AuthenticationKey deviceIdKey, AuthenticatedSocialClaimKeys socialIdKeys, EntityId otherPlayerId)
        {
            return new InternalPlayerResolveDeviceIdAuthConflictRequest(ResolveOperation.DeviceMigrationSource, deviceIdKey, addedDeviceIdAuthOrNull: null, contributedSocialAccounts: socialIdKeys.AllAuthenticationKeys, otherPlayerId);
        }
        public static InternalPlayerResolveDeviceIdAuthConflictRequest ForDeviceMigrationDestination(AuthenticationKey deviceIdKey, PlayerDeviceIdAuthEntry addedDeviceIdAuthOrNull, AuthenticatedSocialClaimKeys socialIdKeys, EntityId otherPlayerId)
        {
            return new InternalPlayerResolveDeviceIdAuthConflictRequest(ResolveOperation.DeviceMigrationDestination, deviceIdKey, addedDeviceIdAuthOrNull, contributedSocialAccounts: socialIdKeys.AllAuthenticationKeys, otherPlayerId);
        }
    }

    [MetaMessage(MessageCodesCore.InternalPlayerResolveDeviceIdAuthConflictResponse, MessageDirection.ServerInternal)]
    public class InternalPlayerResolveDeviceIdAuthConflictResponse : MetaMessage
    {
        public PlayerDeviceIdAuthEntry RemovedAuthEntryOrNull { get; private set; }

        InternalPlayerResolveDeviceIdAuthConflictResponse() { }
        public InternalPlayerResolveDeviceIdAuthConflictResponse(PlayerDeviceIdAuthEntry removedAuthEntryOrNull)
        {
            RemovedAuthEntryOrNull = removedAuthEntryOrNull;
        }
    }

    /// <summary>
    /// Sent when player adds a new social account during session
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalPlayerAddNewSocialAccountRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerAddNewSocialAccountRequest : MetaMessage
    {
        public AuthenticatedSocialClaimKeys SocialKeys { get; private set; }

        InternalPlayerAddNewSocialAccountRequest() { }
        public InternalPlayerAddNewSocialAccountRequest(AuthenticatedSocialClaimKeys socialKeys)
        {
            SocialKeys = socialKeys;
        }
    }

    /// <summary>
    /// Sent when player logs in and has a social claim but that social claim is not yet bound to any game account.
    /// The player attempts to bind it.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalPlayerLoginWithNewSocialAccountRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerLoginWithNewSocialAccountRequest : MetaMessage
    {
        public AuthenticatedSocialClaimKeys SocialKeys { get; private set; }

        InternalPlayerLoginWithNewSocialAccountRequest() { }
        public InternalPlayerLoginWithNewSocialAccountRequest(AuthenticatedSocialClaimKeys socialKeys)
        {
            SocialKeys = socialKeys;
        }
    }

    [MetaMessage(MessageCodesCore.InternalPlayerLoginWithNewSocialAccountResponse, MessageDirection.ServerInternal)]
    public class InternalPlayerLoginWithNewSocialAccountResponse : MetaMessage
    {
        [MetaSerializable]
        public enum ResultCode
        {
            SocialAccountAdded = 1,
        }
        public ResultCode               Result              { get; private set; }

        InternalPlayerLoginWithNewSocialAccountResponse() { }
        public InternalPlayerLoginWithNewSocialAccountResponse(ResultCode result)
        {
            Result = result;
        }
    }
}
