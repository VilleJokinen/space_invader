// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Player's information related to push notifications.
    /// </summary>
    [MetaSerializable]
    public class PlayerPushNotifications
    {
        [MetaMember(1)] OrderedDictionary<string, PlayerDevicePushNotifications> _devices = new OrderedDictionary<string, PlayerDevicePushNotifications>();

        /// <summary>
        /// Get all known Firebase Messaging tokens.
        /// </summary>
        public IEnumerable<string> GetFirebaseMessagingTokens()
        {
            return _devices.Values
                .Select(device => device.FirebaseMessagingToken)
                .Where(token => token != null);
        }

        /// <summary>
        /// Whether <paramref name="token"/> is currently registered as the Firebase Messaging token for device <paramref name="deviceId"/>.
        /// </summary>
        public bool HasFirebaseMessagingToken(string deviceId, string token)
        {
            if (deviceId == null)
                throw new ArgumentNullException(nameof(deviceId));

            if (token == null)
                throw new ArgumentNullException(nameof(token));

            return _devices.TryGetValue(deviceId, out PlayerDevicePushNotifications device)
                && device.FirebaseMessagingToken == token;
        }

        /// <summary>
        /// Register <paramref name="token"/> as the Firebase Messaging token for device <paramref name="deviceId"/>.
        /// Any existing Firebase Messaging token for the device will be removed.
        /// If the token is already registered for another device, then the token
        /// will be removed for that other device.
        /// </summary>
        public void SetFirebaseMessagingToken(string deviceId, string token)
        {
            if (deviceId == null)
                throw new ArgumentNullException(nameof(deviceId));

            if (token == null)
                throw new ArgumentNullException(nameof(token));

            // If the token is already on some other device, remove it from there
            RemoveFirebaseMessagingToken(token);

            // Register token for this device
            EnsureHasDevice(deviceId).FirebaseMessagingToken = token;
        }

        /// <summary>
        /// Remove <paramref name="token"/>.
        /// If it's not registered for any device, nothing is done.
        /// </summary>
        public void RemoveFirebaseMessagingToken(string token)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            // \note The loop stops at the first match, since we maintain the invariant
            //       that no two devices on a player have the same token.
            foreach ((string deviceId, PlayerDevicePushNotifications device) in _devices)
            {
                if (device.FirebaseMessagingToken == token)
                {
                    device.FirebaseMessagingToken = null;
                    if (device.IsEmpty)
                        _devices.Remove(deviceId);
                    break;
                }
            }
        }

        /// <summary>
        /// Remove information stored regarding device <paramref name="deviceId"/>.
        /// </summary>
        public void RemoveDevice(string deviceId)
        {
            if (deviceId == null)
                throw new ArgumentNullException(nameof(deviceId));

            _devices.Remove(deviceId);
        }

        PlayerDevicePushNotifications EnsureHasDevice(string deviceId)
        {
            if (!_devices.TryGetValue(deviceId, out PlayerDevicePushNotifications device))
            {
                device = new PlayerDevicePushNotifications();
                _devices.Add(deviceId, device);
            }

            return device;
        }

        /// <summary>
        /// Clears all push notification tokens.
        /// </summary>
        public void Clear()
        {
            _devices.Clear();
        }
    }

    /// <summary>
    /// Contains push notification information for a specific device.
    /// </summary>
    [MetaSerializable]
    public class PlayerDevicePushNotifications
    {
        /// <summary> The most recent known Firebase Messaging token; null if none. </summary>
        [MetaMember(1)] public string FirebaseMessagingToken;

        [IgnoreDataMember] public bool IsEmpty => FirebaseMessagingToken == null;
    }

    /// <summary>
    /// Utilities for dealing with push notifications
    /// </summary>
    public static class PushNotificationUtil
    {
        public const int FirebaseMessagingTokenMaxLength = 256;
    }

    /// <summary>
    /// Client has received a messaging token from Firebase and wishes to register it for their game account.
    /// If the token has already been registered, nothing is changed.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerAddFirebaseMessagingToken)]
    public class PlayerAddFirebaseMessagingToken : PlayerActionCore<IPlayerModelBase>
    {
        public string Token { get; private set; }

        public PlayerAddFirebaseMessagingToken() { }
        public PlayerAddFirebaseMessagingToken(string token) { Token = token; }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (string.IsNullOrEmpty(Token))
            {
                player.Log.Warning("Firebase messaging token is null or empty ({Token})", Token);
                return MetaActionResult.InvalidFirebaseMessagingToken;
            }

            if (Token.Length > PushNotificationUtil.FirebaseMessagingTokenMaxLength)
            {
                player.Log.Warning("Firebase messaging token is too long (length {TokenLength})", Token.Length);
                return MetaActionResult.InvalidFirebaseMessagingToken;
            }

            if (player.SessionDeviceGuid == null)
                return MetaActionResult.NoSessionDeviceId;

            if (commit)
            {
                if (!player.PushNotifications.HasFirebaseMessagingToken(player.SessionDeviceGuid, Token))
                {
                    // \note First, remove from legacy list so it doesn't duplicate the token
                    player.FirebaseMessagingTokensLegacy.Remove(Token);

                    player.PushNotifications.SetFirebaseMessagingToken(player.SessionDeviceGuid, Token);
                    player.ServerListenerCore.FirebaseMessagingTokenAdded(Token);
                }
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Remove the given Firebase Messaging token from the player.
    /// This is done when Firebase reports to the server that the token is unregistered and thus no longer valid.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerServerCleanupRemoveFirebaseMessagingToken)]
    public class PlayerServerCleanupRemoveFirebaseMessagingToken : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public string Token { get; private set; }

        PlayerServerCleanupRemoveFirebaseMessagingToken() { }
        public PlayerServerCleanupRemoveFirebaseMessagingToken(string token) { Token = token; }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                player.FirebaseMessagingTokensLegacy.Remove(Token);
                player.PushNotifications.RemoveFirebaseMessagingToken(Token);
            }

            return MetaActionResult.Success;
        }
    }
}
