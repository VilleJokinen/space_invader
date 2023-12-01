// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Server.Authentication.Authenticators;
using Metaplay.Server.Database;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication
{
    /// <summary>
    /// Exception for failed authentication / registration.
    /// </summary>
    public class AuthenticationError : Exception
    {
        public AuthenticationError(string reason, Exception inner = null) : base(reason, inner) { }
    }

    /// <summary>
    /// Authentication could not complete due to a temporary error in validation services.
    /// </summary>
    public class AuthenticationTemporarilyUnavailable : Exception
    {
        public AuthenticationTemporarilyUnavailable(string reason) : base(reason) { }
    }

    /// <summary>
    /// Login with social authentication failed because the claim was valid but there is no
    /// bound game account to the social account.
    /// </summary>
    public class NoBoundPlayerAccountForValidSocialAccount : AuthenticationError
    {
        public readonly AuthenticatedSocialClaimKeys AuthKeys;

        public NoBoundPlayerAccountForValidSocialAccount(AuthenticatedSocialClaimKeys authKeys, string reason) : base(reason)
        {
            AuthKeys = authKeys;
        }
    }

    /// <summary>
    /// Result for a social authentication request. Includes the <see cref="AuthenticationKey"/>s which identifies
    /// the social user (platform and id) as well as <see cref="ExistingPlayerId"/> which identifies to which player
    /// the social user is currently attached to (or <see cref="EntityId.None"/> if social user is previously unknown).
    /// </summary>
    public class SocialAuthenticationResult
    {
        public AuthenticatedSocialClaimKeys AuthKeys            { get; private set; }
        public EntityId                     ExistingPlayerId    { get; private set; }

        public SocialAuthenticationResult(AuthenticatedSocialClaimKeys authKeys, EntityId existingPlayerId)
        {
            AuthKeys            = authKeys;
            ExistingPlayerId    = existingPlayerId;
        }
    }

    /// <summary>
    /// Authentication Keys resolved from an authenticated Social Claim.
    /// </summary>
    [MetaSerializable]
    public struct AuthenticatedSocialClaimKeys
    {
        /// <summary>
        /// The primary authentication key of the claim. This always exists.
        /// </summary>
        [MetaMember(1)] public AuthenticationKey PrimaryAuthenticationKey;

        /// <summary>
        /// The secondary authentication keys of the claim. These always are updated to match primary key if an authentication
        /// record exists for the primary key. These allow authenticator to support migrations by storing secondary migration
        /// keys which are only used as a fallback. Note that these secondary keys may be unsuitable for authentication (for
        /// example due to lack of proper verification) in which case they are only useful for informational uses, such as
        /// storing Apple GameCenter GamePlayerIds.
        /// </summary>
        [MetaMember(2)] public AuthenticationKey[] SecondaryAuthenticationKeys;

        /// <summary>
        /// Convenience getter for all authentication keys in the order of [Primary] + [Secondary..].
        /// </summary>
        public AuthenticationKey[] AllAuthenticationKeys
        {
            get
            {
                AuthenticationKey[] keys = new AuthenticationKey[1 + SecondaryAuthenticationKeys.Length];
                keys[0] = PrimaryAuthenticationKey;
                SecondaryAuthenticationKeys.CopyTo(keys, index: 1);
                return keys;
            }
        }

        AuthenticatedSocialClaimKeys(AuthenticationKey primaryAuthenticationKey, AuthenticationKey[] secondaryAuthenticationKeys)
        {
            if (!Authenticator.SocialAuthenticationPlatformIsSuitableForAuthentication(primaryAuthenticationKey.Platform))
                throw new ArgumentException($"Primary authentication key must be suitable for authentication. Platform is {primaryAuthenticationKey.Platform}.");
            if (primaryAuthenticationKey.Platform == AuthenticationPlatform.DeviceId)
                throw new ArgumentException("Social claim keys cannot contain DeviceId-based AuthenticationKey");
            foreach (AuthenticationKey secondaryKey in secondaryAuthenticationKeys)
            {
                if (secondaryKey.Platform == AuthenticationPlatform.DeviceId)
                    throw new ArgumentException("Social claim keys cannot contain DeviceId-based AuthenticationKey");
            }

            PrimaryAuthenticationKey = primaryAuthenticationKey;
            SecondaryAuthenticationKeys = secondaryAuthenticationKeys;
        }

        public static AuthenticatedSocialClaimKeys FromSingleKey(AuthenticationKey primaryAuthenticationKey)
        {
            return new AuthenticatedSocialClaimKeys(primaryAuthenticationKey, secondaryAuthenticationKeys: Array.Empty<AuthenticationKey>());
        }
        public static AuthenticatedSocialClaimKeys FromPrimaryAndSecondaryKeys(AuthenticationKey primaryAuthenticationKey, params AuthenticationKey[] secondaryAuthenticationKeys)
        {
            return new AuthenticatedSocialClaimKeys(primaryAuthenticationKey, secondaryAuthenticationKeys);
        }
    }

    /// <summary>
    /// Database-persisted authentication entry. Contains the <see cref="AuthenticationKey"/> identifying the
    /// player (either via deviceId/authToken or as a social platfor user). For deviceId-based authentication,
    /// the hashed authToken is also included for validating login requests.
    /// </summary>
    [Table("AuthEntries")]
    public class PersistedAuthenticationEntry : IPersistedItem
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(160)]
        [Column(TypeName = "varchar(160)")]
        public string   AuthKey         { get; set; }

        [Column(TypeName = "varchar(160)")]
        public string   HashedAuthToken { get; set; }   // Used with deviceId-based authentication, null for social platforms

        [Required]
        [Column(TypeName = "varchar(64)")]
        public string   PlayerId        { get; set; }

        public EntityId PlayerEntityId => EntityId.ParseFromString(PlayerId);

        public PersistedAuthenticationEntry() { }
        public PersistedAuthenticationEntry(AuthenticationKey authKey, string hashedAuthToken, EntityId playerId)
        {
            AuthKey         = authKey.ToString();
            HashedAuthToken = hashedAuthToken;
            PlayerId        = playerId.ToString();
        }
    }

    public class RegisterAccountResponse
    {
        public EntityId PlayerId    { get; private set; }
        public string   DeviceId    { get; private set; }
        [Sensitive]
        public string   AuthToken   { get; private set; }

        public RegisterAccountResponse() { }
        public RegisterAccountResponse(EntityId playerId, string deviceId, string authToken)
        {
            PlayerId    = playerId;
            DeviceId    = deviceId;
            AuthToken   = authToken;
        }
    }

    /// <summary>
    /// Utility functions for authenticating players. Supports authentication based on deviceId/authToken
    /// and platform/social authentications (Google Play, Game Center, etc.).
    /// </summary>
    public static class Authenticator
    {
        public static ulong NumReservedBotIds = 1UL << 32; // reserve 4 billion lowest ids for bots to use

        public static Task<bool> RemoveAuthenticationEntryAsync(AuthenticationKey authKey) =>
            MetaDatabase.Get(QueryPriority.Normal).RemoveAsync<PersistedAuthenticationEntry>(authKey.ToString());

        public static Task<PersistedAuthenticationEntry> TryGetAuthenticationEntryAsync(AuthenticationKey authKey) =>
            MetaDatabase.Get(QueryPriority.Normal).TryGetAsync<PersistedAuthenticationEntry>(authKey.ToString());

        static Task InsertAuthenticationEntryAsync(PersistedAuthenticationEntry entry) =>
            MetaDatabase.Get(QueryPriority.Normal).InsertAsync(entry);

        static Task UpdateAuthenticationEntryAsync(PersistedAuthenticationEntry entry) =>
            MetaDatabase.Get(QueryPriority.Normal).UpdateAsync(entry);

        static Task InsertOrUpdateAuthenticationEntryAsync(PersistedAuthenticationEntry entry) =>
            MetaDatabase.Get(QueryPriority.Normal).InsertOrUpdateAsync(entry);

        /// <summary>
        /// Validates the authentication claim and returns the validated authentication key(s). On failure, throws.
        /// </summary>
        /// <param name="playerId">EntityId of the player making the claim</param>
        /// <exception cref="AuthenticationError">if claim is not valid</exception>
        /// <exception cref="AuthenticationTemporarilyUnavailable">if validation services are temporarily unavailable</exception>
        static async Task<AuthenticatedSocialClaimKeys> AuthenticateSocialAuthenticationClaimAsync(EntityId playerId, SocialAuthenticationClaimBase claim)
        {
            switch (claim)
            {
                case SocialAuthenticationClaimDevelopment development:
                    return await DevelopmentAuthenticator.AuthenticateAsync(development);

                case SocialAuthenticationClaimGooglePlayV1 googlePlayV1:
                    return await GooglePlayV1Authenticator.AuthenticateAsync(googlePlayV1);

                case SocialAuthenticationClaimGoogleSignIn googleSignIn:
                    return await GoogleSignInAuthenticator.AuthenticateAsync(googleSignIn);

                case SocialAuthenticationClaimGooglePlayV2 googlePlayV2:
                    return await GooglePlayV2Authenticator.AuthenticateAsync(googlePlayV2);

                case SocialAuthenticationClaimSignInWithApple signInWithApple:
                    return await SignInWithAppleAuthenticator.AuthenticateAsync(signInWithApple);

                case SocialAuthenticationClaimFacebookLogin facebookLogin:
                    return await FacebookAuthenticator.AuthenticateAsync(facebookLogin);

                case SocialAuthenticationClaimGameCenter gameCenter:
                    return await GameCenterAuthenticator.AuthenticateAsync(gameCenter);

                case SocialAuthenticationClaimGameCenter2020 gameCenter:
                    return await GameCenter2020Authenticator.AuthenticateAsync(gameCenter);

                case SocialAuthenticationClaimImmutableX immutableX:
                    return await ImmutableXAuthenticator.AuthenticateAsync(playerId, immutableX);

                default:
                    throw new AuthenticationError($"Unsupported social authentication claim: {claim.GetType().Name}");
            }
        }

        /// <summary>
        /// Player attached to a <see cref="AuthenticatedSocialClaimKeys"/>.
        /// </summary>
        readonly struct SocialAuthenticationAttachedPlayer
        {
            public readonly EntityId PlayerId;

            /// <summary>
            /// Authentication entry for the <see cref="AuthenticatedSocialClaimKeys.PrimaryAuthenticationKey"/>. Not null.
            /// </summary>
            public readonly PersistedAuthenticationEntry PrimaryEntry;

            /// <summary>
            /// Authentication entries for the  <see cref="AuthenticatedSocialClaimKeys.SecondaryAuthenticationKeys"/>. No entry is null.
            /// </summary>
            public readonly PersistedAuthenticationEntry[] SecondaryEntries;

            public SocialAuthenticationAttachedPlayer(EntityId playerId, PersistedAuthenticationEntry primaryEntry, PersistedAuthenticationEntry[] secondaryEntries)
            {
                PlayerId = playerId;
                PrimaryEntry = primaryEntry;
                SecondaryEntries = secondaryEntries;
            }
        }

        readonly struct MaintenanceMigration
        {
            public readonly AuthenticationKey   SocialKey;

            /// <summary>
            /// None if the Social Id was not migrated from any source player, e.g.
            /// the social key was added as a result of the sync.
            /// </summary>
            public readonly EntityId            FromPlayer;
            public readonly EntityId            ToPlayer;

            public MaintenanceMigration(AuthenticationKey socialKey, EntityId fromPlayer, EntityId toPlayer)
            {
                SocialKey = socialKey;
                FromPlayer = fromPlayer;
                ToPlayer = toPlayer;
            }
        }

        /// <summary>
        /// Fetches the player id of persisted authentication entry for the claim if such exists. If the authentication entries exist, also
        /// ensures all entries are in sync, i.e. point to the same player and returns the migrations done. If no authentication record exist,
        /// or if the ones that exist are unsuitable for authentication, returns None and empty set of migrations.
        /// </summary>
        /// <exception cref="AuthenticationError">if claim is not valid</exception>
        /// <exception cref="AuthenticationTemporarilyUnavailable">if validation services are temporarily unavailable</exception>
        static async Task<SocialAuthenticationAttachedPlayer?> TryGetAndSyncSocialAuthenticationAttachedPlayerAsync(IMetaLogger log, IEntityAsker asker, AuthenticatedSocialClaimKeys authenticatedClaim)
        {
            PersistedAuthenticationEntry primaryEntry = await TryGetAuthenticationEntryAsync(authenticatedClaim.PrimaryAuthenticationKey);
            PersistedAuthenticationEntry[] secondaryEntries;

            if (authenticatedClaim.SecondaryAuthenticationKeys.Length == 0)
            {
                secondaryEntries = Array.Empty<PersistedAuthenticationEntry>();
            }
            else
            {
                secondaryEntries = new PersistedAuthenticationEntry[authenticatedClaim.SecondaryAuthenticationKeys.Length];
                for (int ndx = 0; ndx < authenticatedClaim.SecondaryAuthenticationKeys.Length; ++ndx)
                    secondaryEntries[ndx] = await TryGetAuthenticationEntryAsync(authenticatedClaim.SecondaryAuthenticationKeys[ndx]);
            }

            // Find first player id from the primary and suitable secondary auth records.
            OrderedSet<EntityId> playerIdsSuitableForAuthentication = new OrderedSet<EntityId>(capacity: 1);
            if (primaryEntry != null)
                playerIdsSuitableForAuthentication.Add(primaryEntry.PlayerEntityId);

            for (int ndx = 0; ndx < authenticatedClaim.SecondaryAuthenticationKeys.Length; ++ndx)
            {
                if (secondaryEntries[ndx] == null)
                    continue;
                if (!SocialAuthenticationPlatformIsSuitableForAuthentication(authenticatedClaim.SecondaryAuthenticationKeys[ndx].Platform))
                    continue;

                playerIdsSuitableForAuthentication.Add(secondaryEntries[ndx].PlayerEntityId);
            }

            // If no suitable entry exists, then there is no player attached for the social account and not migrations need to be done
            if (playerIdsSuitableForAuthentication.Count == 0)
                return null;

            EntityId chosenPlayerId;
            if (playerIdsSuitableForAuthentication.Count == 1)
            {
                // Unambiguous player id
                chosenPlayerId = playerIdsSuitableForAuthentication.First();
            }
            else
            {
                // Claim maps to multiple players. Let game logic choose the best option.
                // \note: this should be very exceptional.
                chosenPlayerId = await IntegrationRegistry.Get<AuthenticationConflictAutoResolverBase>().ResolveInternallyConflictingSocialPlatformAsync(asker, authenticatedClaim, playerIdsSuitableForAuthentication.ToArray());
                log.Warning("Ambiguous {Platform} social claim resolved to {Resolved}. Candidates were [{Candidates}]", authenticatedClaim.PrimaryAuthenticationKey.Platform, chosenPlayerId, string.Join(",", playerIdsSuitableForAuthentication));
            }

            // There is a player attached the social account. Sync missing or mismatching records.

            List<MaintenanceMigration> migrations = new List<MaintenanceMigration>();

            if (primaryEntry == null || primaryEntry.PlayerEntityId != chosenPlayerId)
            {
                PersistedAuthenticationEntry newEntry = new PersistedAuthenticationEntry(authenticatedClaim.PrimaryAuthenticationKey, hashedAuthToken: null, chosenPlayerId);
                await InsertOrUpdateAuthenticationEntryAsync(newEntry);
                migrations.Add(new MaintenanceMigration(authenticatedClaim.PrimaryAuthenticationKey, primaryEntry?.PlayerEntityId ?? EntityId.None, chosenPlayerId));
                primaryEntry = newEntry;
            }

            for (int ndx = 0; ndx < authenticatedClaim.SecondaryAuthenticationKeys.Length; ++ndx)
            {
                if (secondaryEntries[ndx] == null || secondaryEntries[ndx].PlayerEntityId != chosenPlayerId)
                {
                    PersistedAuthenticationEntry newEntry = new PersistedAuthenticationEntry(authenticatedClaim.SecondaryAuthenticationKeys[ndx], hashedAuthToken: null, chosenPlayerId);
                    await InsertOrUpdateAuthenticationEntryAsync(newEntry);
                    migrations.Add(new MaintenanceMigration(authenticatedClaim.SecondaryAuthenticationKeys[ndx], secondaryEntries[ndx]?.PlayerEntityId ?? EntityId.None, chosenPlayerId));
                    secondaryEntries[ndx] = newEntry;
                }
            }

            /// Silent migration operations have now been performed to Auth table.
            /// Syncs these migrations from into the affected players' PlayerModel.AuthenticationMethods.
            foreach (MaintenanceMigration migration in migrations)
            {
                if (migration.FromPlayer != EntityId.None)
                {
                    // migration
                    InternalPlayerResolveSocialAuthConflictResponse removed = await asker.EntityAskAsync<InternalPlayerResolveSocialAuthConflictResponse>(migration.FromPlayer, InternalPlayerResolveSocialAuthConflictRequest.ForSocialAuthMigrationSource(AuthenticatedSocialClaimKeys.FromSingleKey(migration.SocialKey), migration.ToPlayer));
                    _ = await asker.EntityAskAsync<InternalPlayerResolveSocialAuthConflictResponse>(migration.ToPlayer, InternalPlayerResolveSocialAuthConflictRequest.ForSocialAuthMigrationDestination(removed.RemovedSocialKeys, migration.FromPlayer));
                }
                else
                {
                    // new key
                    await asker.EntityAskAsync<InternalPlayerResolveSocialAuthConflictResponse>(migration.ToPlayer, InternalPlayerResolveSocialAuthConflictRequest.ForSocialAuthAddNewMigrationKey(new OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase>() { { migration.SocialKey, null } }));
                }
            }

            return new SocialAuthenticationAttachedPlayer(chosenPlayerId, primaryEntry, secondaryEntries);
        }

        /// <summary>
        /// Validates the social authentication claim and determines the existing player account for it.
        /// If there is no existing playerId for a valid social account, it must be created with
        /// <see cref="StoreSocialAuthenticationEntryAsync(AuthenticatedSocialClaimKeys, EntityId)"/>.
        /// If there is a existing playerId (conflict) on authentication. it must be resolved separately by calling
        /// <see cref="UpdateDeviceToPlayerMappingAsync(string, EntityId)"/> or
        /// <see cref="UpdateSocialAuthenticationAsync(AuthenticationKey, EntityId)"/>.
        /// </summary>
        /// <param name="playerId">EntityId of the player making the claim</param>
        /// <param name="claim">Authentication claim made by the client</param>
        /// <returns>AuthenticationKeys for validated social authentication and EntityId of the an existing player already attached to the social platform userId, or EntityId.None in case of no conflict</returns>
        /// <exception cref="AuthenticationError">if claim is not valid</exception>
        /// <exception cref="AuthenticationTemporarilyUnavailable">if validation services are temporarily unavailable</exception>
        public static async Task<SocialAuthenticationResult> AuthenticateSocialAccountAsync(IMetaLogger log, IEntityAsker asker, EntityId playerId, SocialAuthenticationClaimBase claim)
        {
            if (!playerId.IsOfKind(EntityKindCore.Player))
                throw new ArgumentException($"Trying to authenticate invalid PlayerId {playerId}", nameof(playerId));

            // Authenticate (or throw) the social claim
            AuthenticatedSocialClaimKeys authenticatedSocialClaim = await AuthenticateSocialAuthenticationClaimAsync(playerId, claim).ConfigureAwait(false);

            // Check whether the social id is already attached to a player
            SocialAuthenticationAttachedPlayer? existingPlayerMaybe = await TryGetAndSyncSocialAuthenticationAttachedPlayerAsync(log, asker, authenticatedSocialClaim);

            if (existingPlayerMaybe is SocialAuthenticationAttachedPlayer existingPlayer)
            {
                // Social profile already attached to an existing playerId, let the caller handle the conflict
                return new SocialAuthenticationResult(authenticatedSocialClaim, existingPlayer.PlayerId);
            }
            else
            {
                return new SocialAuthenticationResult(authenticatedSocialClaim, EntityId.None);
            }
        }

        public static async Task<EntityId> AuthenticateAccountByDeviceIdAsync(IMetaLogger log, string deviceId, string authToken, EntityId claimedPlayerId, bool isBot)
        {
            if (!DeviceAuthentication.IsValidDeviceId(deviceId))
                throw new AuthenticationError($"Invalid device id in authentication request: {deviceId}");
            if (!DeviceAuthentication.IsValidAuthToken(authToken))
                throw new AuthenticationError($"Invalid auth token in authentication request: {authToken}");

            AuthenticationKey authKey = new AuthenticationKey(AuthenticationPlatform.DeviceId, deviceId);
            PersistedAuthenticationEntry authEntry = await TryGetAuthenticationEntryAsync(authKey).ConfigureAwait(false);

            if (authEntry != null)
            {
                EntityId authPlayerId = authEntry.PlayerEntityId;

                // Check that authToken is valid
                if (!CompareHashEqual(HashAuthToken(authToken), authEntry.HashedAuthToken))
                {
                    // If also player id is wrong too, log that to help with debugging.
                    if (authPlayerId == claimedPlayerId)
                        throw new AuthenticationError($"AuthToken mismatch for {claimedPlayerId} (deviceId={deviceId})");
                    else
                        throw new AuthenticationError($"AuthToken and PlayerId mismatch in login attempt. AuthToken was incorrect. PlayerId in attempt was {claimedPlayerId} but actual is {authPlayerId} (deviceId={deviceId}).");
                }

                // The requested and persisted playerId may not match in cases where social platform authentication
                // is used to re-attach a given deviceId to a pre-existing playerId.
                if (authPlayerId != claimedPlayerId)
                    log.Info("Mismatched playerId at login: claimed={PlayerId}, actual={PersistedPlayerId}. This can be caused by account transfer due to social login.", claimedPlayerId, authPlayerId);

                // Sanity check playerId
                if (!authPlayerId.IsOfKind(EntityKindCore.Player))
                    throw new AuthenticationError($"Invalid PlayerId {authPlayerId} found for device {deviceId}!");

                // Success!
                log.Debug("Authentication succeeded for {PlayerId} (deviceId={DeviceId})", authPlayerId, deviceId);
                return authPlayerId;
            }
            else // AuthEntry not found in database
            {
                if (isBot)
                {
                    // \note Bots do not know if the account was already created or not so they always pretend to log in to an existing account. Server creates new accounts on demand.
                    log.Info("Creating new bot account {PlayerId} (deviceId={DeviceId}).", claimedPlayerId, deviceId);

                    // Must be a valid botId to avoid risk of hijacking real player accounts
                    if (!claimedPlayerId.IsOfKind(EntityKindCore.Player))
                        throw new AuthenticationError($"Supplied PlayerId {claimedPlayerId} was not a PlayerId!");
                    if (claimedPlayerId.Value >= NumReservedBotIds)
                        throw new AuthenticationError($"Claimed bot tried to hijack a real account: deviceId={deviceId}, claimedPlayerId={claimedPlayerId}");

                    // For bots, we trust the playerId that they claim (as long as it was in the bot range).
                    try
                    {
                        await DatabaseEntityUtil.PersistEmptyPlayerAsync(claimedPlayerId).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        // Player with the desired Id already exists (or some unlikely DB error). We don't want to allow bot logins to any
                        // non-bot accounts, and the only way to guarantee is this is to allow attaching login only to freshly created accounts.
                        //
                        // Note that we cannot detect botness from the existing player entity. Hence if the login is interrupted between succesful
                        // PersistEmptyPlayerAsync and before StoreDeviceAuthenticationEntryAsync, a new account will be created but no bot auth
                        // records attached to it. This leaves the account in orphaned state, into which the bot client cannot log into. This is
                        // expected to be rare and relatively harmless.
                        throw new AuthenticationError($"Cannot create new player for bot with the desired PlayerId {claimedPlayerId}", exception);
                    }

                    // Attach bot to the newly created player account.
                    await StoreDeviceAuthenticationEntryAsync(deviceId, authToken, claimedPlayerId).ConfigureAwait(false);

                    return claimedPlayerId;
                }
                else
                {
                    // \note These should be very rare and only happen if AuthEntry was lost from database!
                    //       Can happen when:
                    //       * if production client with existing account is redirected to staging environment.
                    //       * if production client is redirected to staging environment, it creates account there, and the redirection is torn down such that client connects to production.
                    //       * player account is deleted.
                    EntityId newPlayerId = await DatabaseEntityUtil.CreateNewPlayerAsync(log).ConfigureAwait(false);
                    log.Warning("Successfully re-created lost player state with new {PlayerId} (claimedPlayerId={ClaimedPlayerId}, deviceId={DeviceId})", newPlayerId, claimedPlayerId, deviceId);

                    // Persist authentication entry
                    await StoreDeviceAuthenticationEntryAsync(deviceId, authToken, newPlayerId).ConfigureAwait(false);

                    return newPlayerId;
                }
            }
        }

        /// <summary>
        /// Determines if a valid social claim on a platform can be used in client login request, bypassing the normal device-id login.
        /// </summary>
        static bool SocialAuthenticationPlatformSupportsLogin(AuthenticationPlatform platform)
        {
            if (platform == AuthenticationPlatform.Development)
                return true;
            return false;
        }

        /// <summary>
        /// Determines if a valid social login claim on a certain platform is sufficient for game account authentication. This is
        /// false for platforms for which server-side validation is not secure.
        /// </summary>
        public static bool SocialAuthenticationPlatformIsSuitableForAuthentication(AuthenticationPlatform platform)
        {
            return platform != AuthenticationPlatform.GameCenter2020UAGT;
        }

        /// <summary>
        /// Validates the social authentication claim and determines the existing player account for it.
        /// </summary>
        /// <exception cref="AuthenticationError"></exception>
        /// <exception cref="AuthenticationTemporarilyUnavailable"></exception>
        /// <exception cref="NoBoundPlayerAccountForValidSocialAccount"></exception>
        public static async Task<SocialAuthenticationResult> AuthenticateAccountViaSocialPlatform(IMetaLogger log, IEntityAsker asker, SocialAuthenticationClaimBase claim, EntityId claimedPlayerId, bool isBot)
        {
            if (!SocialAuthenticationPlatformSupportsLogin(claim.Platform))
                throw new AuthenticationError($"Social authentication platform {claim.Platform} doesn't support direct login!");

            // Validate the claim and find pre-existing account
            SocialAuthenticationResult result = await AuthenticateSocialAccountAsync(log, asker, claimedPlayerId, claim);

            // \note: Not creating a new player if auth entry is lost when authenticating via social platform.
            if (result.ExistingPlayerId == EntityId.None)
                throw new NoBoundPlayerAccountForValidSocialAccount(result.AuthKeys, $"No account binding exists for platform {result.AuthKeys.PrimaryAuthenticationKey.Platform} Id {result.AuthKeys.PrimaryAuthenticationKey.Id} (claimed player id {claimedPlayerId})");

            // Due to account transfer the client may claim a mismatching player id
            if (result.ExistingPlayerId != claimedPlayerId)
                log.Info("Mismatched playerId at login via social platform: claimed={claimedPlayerId}, actual={PlayerEntityId}.", claimedPlayerId, result.ExistingPlayerId);

            return result;
        }

        static string HashAuthToken(string authToken)
        {
            // \todo [petri] use more secure random or at least a salt
            return Util.ComputeSHA256(authToken);
        }

        static bool CompareHashEqual(string a, string b)
        {
            if (a.Length != b.Length)
                return false;

            // Use constant-time comparison to avoid timing attacks.
            int sum = 0;
            for (int ndx = 0; ndx < a.Length; ndx++)
                sum |= a[ndx] ^ b[ndx];
            return sum == 0;
        }

        public static async Task UpdateDeviceToPlayerMappingAsync(string deviceId, EntityId newPlayerId)
        {
            if (!newPlayerId.IsOfKind(EntityKindCore.Player))
                throw new ArgumentException($"Trying to persist invalid PlayerId {newPlayerId} for device {deviceId}", nameof(newPlayerId));

            // Fetch the AuthEntry for deviceId and replace the mapped-to-playerId
            AuthenticationKey               authKey     = new AuthenticationKey(AuthenticationPlatform.DeviceId, deviceId);
            PersistedAuthenticationEntry    authEntry   = await TryGetAuthenticationEntryAsync(authKey).ConfigureAwait(false);
            authEntry.PlayerId = newPlayerId.ToString();
            await UpdateAuthenticationEntryAsync(authEntry).ConfigureAwait(false);
        }

        public static async Task UpdateSocialAuthenticationAsync(AuthenticationKey authKey, EntityId playerId)
        {
            if (authKey.Platform == AuthenticationPlatform.DeviceId)
                throw new ArgumentException("UpdateSocialAuthentication() called with DeviceId-based AuthenticationKey", nameof(authKey));

            if (!playerId.IsOfKind(EntityKindCore.Player))
                throw new ArgumentException($"Trying to persist invalid PlayerId {playerId}", nameof(playerId));

            PersistedAuthenticationEntry authEntry = new PersistedAuthenticationEntry(authKey, hashedAuthToken: null, playerId);
            await UpdateAuthenticationEntryAsync(authEntry).ConfigureAwait(false);
        }

        public static async Task UpdateSocialAuthenticationAsync(AuthenticatedSocialClaimKeys authKeys, EntityId playerId)
        {
            if (!playerId.IsOfKind(EntityKindCore.Player))
                throw new ArgumentException($"Trying to persist invalid PlayerId {playerId}", nameof(playerId));

            foreach (AuthenticationKey authKey in authKeys.AllAuthenticationKeys)
                await UpdateAuthenticationEntryAsync(new PersistedAuthenticationEntry(authKey, hashedAuthToken: null, playerId)).ConfigureAwait(false);
        }

        static Task StoreDeviceAuthenticationEntryAsync(string deviceId, string authToken, EntityId playerId)
        {
            if (!playerId.IsOfKind(EntityKindCore.Player))
                throw new ArgumentException($"Trying to persist invalid PlayerId {playerId} for device {deviceId}", nameof(playerId));

            AuthenticationKey   authKey         = new AuthenticationKey(AuthenticationPlatform.DeviceId, deviceId);
            string              hashedAuthToken = HashAuthToken(authToken);
            PersistedAuthenticationEntry authEntry = new PersistedAuthenticationEntry(authKey, hashedAuthToken, playerId);
            return InsertAuthenticationEntryAsync(authEntry);
        }

        public static async Task StoreSocialAuthenticationEntryAsync(AuthenticatedSocialClaimKeys authenticatedSocialClaim, EntityId playerId)
        {
            await InsertAuthenticationEntryAsync(new PersistedAuthenticationEntry(authenticatedSocialClaim.PrimaryAuthenticationKey, hashedAuthToken: null, playerId)).ConfigureAwait(false);

            // \note: all secondary keys are upserted as they may already exist if they were not suitable for authentication
            foreach (AuthenticationKey secondaryKey in authenticatedSocialClaim.SecondaryAuthenticationKeys)
                await InsertOrUpdateAuthenticationEntryAsync(new PersistedAuthenticationEntry(secondaryKey, hashedAuthToken: null, playerId)).ConfigureAwait(false);
        }

        public static async Task<RegisterAccountResponse> RegisterAccountAsync(IMetaLogger log)
        {
            // Allocate random Device ID
            string deviceId = SecureTokenUtil.GenerateRandomStringToken(DeviceAuthentication.DeviceIdLength);
            log.Debug("Request register player: deviceId={DeviceId}", deviceId);

            // Allocate random playerId for the player and store empty Player in database
            EntityId playerId = await DatabaseEntityUtil.CreateNewPlayerAsync(log);

            // Generate secure authToken for player
            string authToken = SecureTokenUtil.GenerateRandomStringToken(DeviceAuthentication.AuthTokenLength);

            // Try to register account & handle response
            await StoreDeviceAuthenticationEntryAsync(deviceId, authToken, playerId).ConfigureAwait(false);
            return new RegisterAccountResponse(playerId, deviceId, authToken);
        }
    }
}
