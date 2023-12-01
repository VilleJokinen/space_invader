// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Player;
using Metaplay.Core.Tasks;
using System;

namespace Metaplay.Unity
{
    /// <summary>
    /// Client-side API for performing social authentication validation via the server.
    /// </summary>
    public class SocialAuthManager
    {
        static LogChannel _log = MetaplaySDK.Logs.CreateChannel("social");

        enum Mode
        {
            Buffering,
            Running,
            Stopped,
        }

        IMetaplayClientSocialAuthenticationDelegate _socialAuthenticationDelegate;
        Mode _mode;
        OrderedDictionary<AuthenticationPlatform, SocialAuthenticationClaimBase> _bufferedClaims;
        bool _connected;

        internal OrderedDictionary<AuthenticationPlatform, SocialAuthenticationClaimBase> BufferedClaims => _bufferedClaims;

        internal void Stop()
        {
            _mode = Mode.Stopped;
        }

        internal static SocialAuthManager CreateRealManager(IMetaplayClientSocialAuthenticationDelegate clientDelegate, OrderedDictionary<AuthenticationPlatform, SocialAuthenticationClaimBase> bufferedClaims)
        {
            SocialAuthManager manager = new SocialAuthManager();
            manager._mode = Mode.Running;
            manager._socialAuthenticationDelegate = clientDelegate;
            manager._bufferedClaims = bufferedClaims ?? new OrderedDictionary<AuthenticationPlatform, SocialAuthenticationClaimBase>();
            manager._connected = false;
            return manager;
        }

        internal static SocialAuthManager CreateBufferingManager()
        {
            SocialAuthManager manager = new SocialAuthManager();
            manager._mode = Mode.Buffering;
            manager._bufferedClaims = new OrderedDictionary<AuthenticationPlatform, SocialAuthenticationClaimBase>();
            return manager;
        }

        internal static SocialAuthManager CreateStoppedManager()
        {
            SocialAuthManager manager = new SocialAuthManager();
            manager._mode = Mode.Stopped;
            return manager;
        }

        internal void OnSessionStarted()
        {
            if (_bufferedClaims != null)
            {
                foreach (SocialAuthenticationClaimBase claim in _bufferedClaims.Values)
                    InternalStartValidation(claim);
            }
            _bufferedClaims = null;
            _connected = true;
        }

        internal void OnSessionStopped()
        {
            _bufferedClaims = _bufferedClaims ?? new OrderedDictionary<AuthenticationPlatform, SocialAuthenticationClaimBase>();
            _connected = false;
        }

        public void StartValidation(SocialAuthenticationClaimBase claim)
        {
            if (claim == null)
                throw new ArgumentNullException(nameof(claim));

            if (_mode == Mode.Stopped)
            {
                _log.Warning("Attempted to StartValidation after SDK was stopped. Ignored.");
                return;
            }

            if (_mode == Mode.Buffering)
            {
                _bufferedClaims[claim.Platform] = claim;
                return;
            }

            if (_socialAuthenticationDelegate == null)
                throw new InvalidOperationException("SocialAuthenticationDelegate needs to be set when initializing the SDK to use Social Authentication.");

            if (!_connected)
            {
                _log.Info("Buffered validation of {ClaimType}: platform={Platform}", claim.GetType().ToGenericTypeString(), claim.Platform);
                _bufferedClaims[claim.Platform] = claim;
                return;
            }

            InternalStartValidation(claim);
        }

        void InternalStartValidation(SocialAuthenticationClaimBase claim)
        {
            _log.Info("Starting validation of {ClaimType}: platform={Platform}", claim.GetType().ToGenericTypeString(), claim.Platform);
            MetaTask.Run(async () =>
            {
                try
                {
                    SocialAuthenticateResult result = await MetaplaySDK.MessageDispatcher.SendRequestAsync<SocialAuthenticateResult>(new SocialAuthenticateRequest(claim));
                    await MetaplaySDK.RunOnUnityThreadAsync(() => OnSocialAuthenticateResult(result));
                }
                catch (Exception ex)
                {
                    _log.Error("Social auth validation failed with {ex}", ex);
                }
            });
        }

        public void ResolveConflict(int conflictResolutionId, bool useOther)
        {
            _log.Debug("Resolving social authentication conflict for {ConflictResolutionId}: useOther={UseOther}", conflictResolutionId, useOther);

            if (_mode == Mode.Stopped)
            {
                _log.Warning("Attempted to ResolveConflict after SDK was stopped. Ignored.");
                return;
            }
            else if (_mode == Mode.Buffering)
            {
                _log.Warning("Attempted to ResolveConflict before SDK was started. Ignored.");
                return;
            }
            else if (!_connected)
            {
                _log.Warning("Attempted to ResolveConflict but there is no ongoing session. Ignored.");
                return;
            }

            // \todo [petri] Keep track of active conflicts?
            MetaplaySDK.MessageDispatcher.SendMessage(new SocialAuthenticateResolveConflict(conflictResolutionId, useOther));
        }

        void OnSocialAuthenticateResult(SocialAuthenticateResult result)
        {
            if (_mode == Mode.Stopped)
            {
                _log.Warning("Got social authentication request completed after SDK was stopped. Ignored.");
                return;
            }
            else if (_mode == Mode.Buffering)
            {
                _log.Warning("Got social authentication request before SDK was started. Ignored.");
                return;
            }
            else if (!_connected)
            {
                _log.Warning("Got social authentication session but there is no ongoing session. Ignored.");
                return;
            }

            // Check whether the delegate exists
            if (_socialAuthenticationDelegate == null)
            {
                _log.Warning("Received SocialAuthenticateResult from the server, but the SocialAuthenticationDelegate is null -- the delegate needs to be set when initializing the SDK for the social authentication feature to work properly.");
                return;
            }

            // Handle a conflicting playerId already connected to the social platform userId
            ISharedGameConfig   gameConfig          = MetaplaySDK.Connection.SessionStartResources.GameConfigs[ClientSlotCore.Player];
            int                 logicVersion        = MetaplaySDK.Connection.SessionStartResources.LogicVersion;
            IPlayerModelBase    conflictingPlayer   = result.ConflictingPlayerIfAvailable.Deserialize(gameConfig, logicVersion);

            if (result.Result == SocialAuthenticateResult.ResultCode.Success && conflictingPlayer != null)
            {
                // There was already a player state associated with the given social authentication. Let the game decide which one to keep.
                conflictingPlayer.GameConfig = gameConfig;
                conflictingPlayer.LogicVersion = logicVersion;
                _socialAuthenticationDelegate.OnSocialAuthenticationConflict(result.Platform, result.ConflictResolutionId, conflictingPlayer);
            }
            else if (result.Result == SocialAuthenticateResult.ResultCode.Success && result.ConflictingPlayerId != EntityId.None)
            {
                // There was already a player state associated with the given social authentication, but there was a server-side error in handling its state.
                _socialAuthenticationDelegate.OnSocialAuthenticationConflictWithFailingOtherPlayer(result.Platform, result.ConflictResolutionId, result.ConflictingPlayerId);
            }
            else if (result.Result == SocialAuthenticateResult.ResultCode.Success)
            {
                // The social account is now associated with this player.
                _socialAuthenticationDelegate.OnSocialAuthenticationSuccess(result.Platform);
            }
            else
            {
                // The authentication attempt failed.
                _socialAuthenticationDelegate.OnSocialAuthenticationFailure(result.Platform, result.Result, result.DebugOnlyErrorMessage);
            }
        }
    }
}
