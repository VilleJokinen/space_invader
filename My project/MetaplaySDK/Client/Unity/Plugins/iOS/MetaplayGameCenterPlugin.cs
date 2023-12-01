// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_IOS

using AOT;
using Metaplay.Core;
using Metaplay.Core.Tasks;
using System.Runtime.InteropServices;

namespace Metaplay.Unity
{
    public static class MetaplayGameCenterPlugin
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LegacyNativeCallback(
            string legacyPlayerId,
            string legacyPublicKeyUrl,
            string legacySaltBase64,
            string legacySignatureBase64,
            ulong legacyTimestamp,
            string bundleId,
            string errorString);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Wwdc2020NativeCallback(
            string wwdc2020TeamPlayerId,
            string wwdc2020GamePlayerId,
            string wwdc2020PublicKeyUrl,
            string wwdc2020SaltBase64,
            string wwdc2020SignatureBase64,
            ulong wwdc2020Timestamp,
            string bundleId,
            string errorString);

        [DllImport("__Internal")]
        static extern void MetaplayGameCenter_GetSocialClaimLegacy([MarshalAs(UnmanagedType.FunctionPtr)]LegacyNativeCallback nativeCallback);

        [DllImport("__Internal")]
        static extern void MetaplayGameCenter_GetSocialClaimWWDC2020([MarshalAs(UnmanagedType.FunctionPtr)]Wwdc2020NativeCallback nativeCallback);

        public struct VerificationSignatureResult
        {
            /// <summary>
            /// Legacy authentication signature, or <c>null</c> if was not requested or it could not be fetched.
            /// </summary>
            public LegacyGameCenterSignature? LegacySignature;

            /// <summary>
            /// Wwdc2020 authentication signature, or <c>null</c> if was not requested or it could not be fetched.
            /// </summary>
            public TeamScopedGameCenterSignature? TeamScopedSignature;

            /// <summary>
            /// Description of errors encountered, or <c>null</c> if there were no errors. Note that the query succeeds partially, the
            /// both error and a single signature are set.
            /// </summary>
            public string ErrorString;
        }
        public struct LegacyGameCenterSignature
        {
            public string PlayerId;
            public string PublicKeyUrl;
            public string SaltBase64;
            public string SignatureBase64;
            public long Timestamp;
            public string BundleId;

            public LegacyGameCenterSignature(string playerId, string publicKeyUrl, string saltBase64, string signatureBase64, long timestamp, string bundleId)
            {
                PlayerId = playerId;
                PublicKeyUrl = publicKeyUrl;
                SaltBase64 = saltBase64;
                SignatureBase64 = signatureBase64;
                Timestamp = timestamp;
                BundleId = bundleId;
            }

            /// <summary>
            /// Creates Social Login Claim from the signature.
            /// </summary>
            /// <param name="optionalMigrationClaim">Same claim in GameCenter2020 form to support migrations, or null if not available.</param>
            public SocialAuthenticationClaimGameCenter ToSocialLoginClaim(SocialAuthenticationClaimGameCenter2020 optionalMigrationClaim)
            {
                return new SocialAuthenticationClaimGameCenter(PlayerId, PublicKeyUrl, (ulong)Timestamp, SignatureBase64, SaltBase64, BundleId, optionalMigrationClaim);
            }
        }
        public struct TeamScopedGameCenterSignature
        {
            /// <summary>
            /// Note that GamePlayerId is not signed and hence is not usable for authentication.
            /// </summary>
            public string GamePlayerId;
            public string TeamPlayerId;
            public string PublicKeyUrl;
            public string SaltBase64;
            public string SignatureBase64;
            public long Timestamp;
            public string BundleId;

            public TeamScopedGameCenterSignature(string gamePlayerId, string teamPlayerId, string publicKeyUrl, string saltBase64, string signatureBase64, long timestamp, string bundleId)
            {
                GamePlayerId = gamePlayerId;
                TeamPlayerId = teamPlayerId;
                PublicKeyUrl = publicKeyUrl;
                SaltBase64 = saltBase64;
                SignatureBase64 = signatureBase64;
                Timestamp = timestamp;
                BundleId = bundleId;
            }

            /// <summary>
            /// Creates Social Login Claim from the signature.
            /// </summary>
            public SocialAuthenticationClaimGameCenter2020 ToSocialLoginClaim()
            {
                return new SocialAuthenticationClaimGameCenter2020(TeamPlayerId, GamePlayerId, PublicKeyUrl, (ulong)Timestamp, SignatureBase64, SaltBase64, BundleId);
            }
        }

        class PendingQuery
        {
            public VerificationSignatureResult Result;
            public LoginResultCallback Callback;
            public bool RequestLegacySignature;

            public PendingQuery(LoginResultCallback callback, bool requestLegacySignature)
            {
                Result = new VerificationSignatureResult();
                Callback = callback;
                RequestLegacySignature = requestLegacySignature;
            }
        }
        public delegate void LoginResultCallback(VerificationSignatureResult result);
        static PendingQuery _pendingQuery;

        /// <summary>
        /// Retrieves the verification signatures for the currently logged in GameCenter player and delivers it to the result callback. If
        /// there is already an verification signature retrieaval ongoing, the call is ignored. The callback is called on Unity thread.
        /// </summary>
        /// <param name="requestTeamScopedSignature">If set, attempts to retrieve validation signature for team-scoped player Id</param>
        /// <param name="requestLegacySignature">If set, attempts to retrieve validation signature for legacy player Id</param>
        public static void GetVerificationSignature(LoginResultCallback callback, bool requestTeamScopedSignature = true, bool requestLegacySignature = true)
        {
            if (_pendingQuery != null)
            {
                // There is already a login ongoing. Just ignore.
                return;
            }

            _pendingQuery = new PendingQuery(callback, requestLegacySignature);

            if (requestTeamScopedSignature)
            {
                MetaplayGameCenter_GetSocialClaimWWDC2020(CompleteWwdc2020Claim);
            }
            else if (requestLegacySignature)
            {
                MetaplayGameCenter_GetSocialClaimLegacy(CompleteLegacy2020Claim);
            }
            else
            {
                _pendingQuery.Result.ErrorString = "no platforms requested";
                CompleteQuery();
            }
        }

        [MonoPInvokeCallback(typeof(Wwdc2020NativeCallback))]
        static void CompleteWwdc2020Claim(
            string wwdc2020TeamPlayerId,
            string wwdc2020GamePlayerId,
            string wwdc2020PublicKeyUrl,
            string wwdc2020SaltBase64,
            string wwdc2020SignatureBase64,
            ulong wwdc2020Timestamp,
            string bundleId,
            string errorString)
        {
            PendingQuery query = _pendingQuery;
            if (query == null)
                return;

            if (!string.IsNullOrEmpty(wwdc2020TeamPlayerId)
                && !string.IsNullOrEmpty(wwdc2020GamePlayerId)
                && !string.IsNullOrEmpty(wwdc2020PublicKeyUrl)
                && !string.IsNullOrEmpty(wwdc2020SaltBase64)
                && !string.IsNullOrEmpty(wwdc2020SignatureBase64))
            {
                query.Result.TeamScopedSignature = new TeamScopedGameCenterSignature(
                    wwdc2020GamePlayerId,
                    wwdc2020TeamPlayerId,
                    wwdc2020PublicKeyUrl,
                    wwdc2020SaltBase64,
                    wwdc2020SignatureBase64,
                    (long)wwdc2020Timestamp,
                    bundleId);
            }

            if (errorString != null)
                query.Result.ErrorString = errorString;

            if (query.RequestLegacySignature)
                MetaplayGameCenter_GetSocialClaimLegacy(CompleteLegacy2020Claim);
            else
                CompleteQuery();
        }

        [MonoPInvokeCallback(typeof(LegacyNativeCallback))]
        static void CompleteLegacy2020Claim(
            string legacyPlayerId,
            string legacyPublicKeyUrl,
            string legacySaltBase64,
            string legacySignatureBase64,
            ulong legacyTimestamp,
            string bundleId,
            string errorString)
        {
            PendingQuery query = _pendingQuery;
            if (query == null)
                return;

            if (!string.IsNullOrEmpty(legacyPlayerId)
                && !string.IsNullOrEmpty(legacyPublicKeyUrl)
                && !string.IsNullOrEmpty(legacySaltBase64)
                && !string.IsNullOrEmpty(legacySignatureBase64))
            {
                query.Result.LegacySignature = new LegacyGameCenterSignature(
                    legacyPlayerId,
                    legacyPublicKeyUrl,
                    legacySaltBase64,
                    legacySignatureBase64,
                    (long)legacyTimestamp,
                    bundleId);
            }

            if (errorString != null)
            {
                if (query.Result.ErrorString == null)
                    query.Result.ErrorString = errorString;
                else
                    query.Result.ErrorString += "; " + errorString;
            }

            CompleteQuery();
        }

        static void CompleteQuery()
        {
            PendingQuery query = _pendingQuery;
            _pendingQuery = null;
            if (query == null)
                return;

            MetaTask.Run(() =>
            {
                query.Callback(query.Result);
            }, MetaTask.UnityMainScheduler);
        }
    }
}

#endif
