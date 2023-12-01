// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// API bridge to ImmutableX
const ImmutableXApiBridge = {
  _state: {
    apiUrl: null,
    bundleLoadPromise: null
  },

  async _EnsureLib() {
    if (ImmutableXApiBridge._state.bundleLoadPromise === null) {
      ImmutableXApiBridge._state.bundleLoadPromise = new Promise(function (resolve, reject) {
        let bundleScript = document.createElement('script');
        bundleScript.src = "metaplay-imx-link-sdk-plugin.min.js";
        bundleScript.onload = function() { resolve(); };
        bundleScript.onerror = function() { reject(new Error("failed to load metaplay-imx-link-sdk-plugin.min.js")); };
        document.body.append(bundleScript);
      });
    }
    await ImmutableXApiBridge._state.bundleLoadPromise;
    MetaplayImxLinkSdkPlugin.EnsureInitialized(ImmutableXApiBridge._state.apiUrl);
  },

  SetApiUrl(args) {
    ImmutableXApiBridge._state.apiUrl = args.apiUrl
    return {}
  },
  async GetWalletAddressAsync(args) {
    await ImmutableXApiBridge._EnsureLib();
    let wallet = await MetaplayImxLinkSdkPlugin.GetWalletAddressAsync(args.forceResetup);
    return {
      ethAddress: wallet.ethAddress,
      imxAddress: wallet.imxAddress,
    };
  },
  async SignLoginChallengeAsync(args) {
    await ImmutableXApiBridge._EnsureLib();
    let signature = await MetaplayImxLinkSdkPlugin.TrySignAsync(args.message, args.description);
    return { signature: signature };
  }
};

// Register module
Module['MetaplayApiBridge'] = Module['MetaplayApiBridge'] || {}
Module['MetaplayApiBridge']['ImmutableXApiBridge'] = ImmutableXApiBridge;
