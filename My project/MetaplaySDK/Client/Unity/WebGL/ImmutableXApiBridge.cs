// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR

using Metaplay.Core;
using Metaplay.Core.Json;
using System.Threading.Tasks;

// \todo [petri] all here is placeholder

// \todo [petri] namespace
namespace Metaplay.Unity
{
    [MetaWebApiBridge]
    public static class ImmutableXApiBridge
    {
        public struct SetApiUrlAsyncRequest
        {
            public string ApiUrl;
        }

        [MetaImportBrowserMethod]
        // \todo [petri] codegen json wrapper
        public static void SetApiUrl(SetApiUrlAsyncRequest request)
        {
            // \todo [petri] make static
            int methodId = WebApiBridge.GetBrowserMethodId(nameof(ImmutableXApiBridge), nameof(SetApiUrl));

            string requestJson = JsonSerialization.SerializeToString(request);
            WebApiBridge.JsonCallSync(methodId, requestJson);
        }

        public struct GetWalletAddressRequest
        {
            public bool ForceResetup;
        }
        public struct GetWalletAddressResponse
        {
            public string EthAddress;
            public string ImxAddress;
        }

        [MetaImportBrowserMethod]
        public static async Task<GetWalletAddressResponse> GetWalletAddressAsync(GetWalletAddressRequest request)
        {
            int methodId = WebApiBridge.GetBrowserMethodId(nameof(ImmutableXApiBridge), nameof(GetWalletAddressAsync));

            string requestJson = JsonSerialization.SerializeToString(request);
            string responseJson = await WebApiBridge.JsonCallAsync(methodId, requestJson);
            DebugLog.Debug("Got responseJson: {0}", responseJson);
            return JsonSerialization.Deserialize<GetWalletAddressResponse>(responseJson);
        }

        public struct SignLoginChallengeRequest
        {
            public string Message;
            public string Description;
        }
        public struct SignLoginChallengeResponse
        {
            public string Signature;
        }

        [MetaImportBrowserMethod]
        public static async Task<SignLoginChallengeResponse> SignLoginChallengeAsync(SignLoginChallengeRequest request)
        {
            int methodId = WebApiBridge.GetBrowserMethodId(nameof(ImmutableXApiBridge), nameof(SignLoginChallengeAsync));

            string requestJson = JsonSerialization.SerializeToString(request);
            string responseJson = await WebApiBridge.JsonCallAsync(methodId, requestJson);
            return JsonSerialization.Deserialize<SignLoginChallengeResponse>(responseJson);
        }
    }
}

#endif // Unity WebGL build
