// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Unity.IAP
{
    public static class PurchasingMisc
    {
        /// <summary>
        /// Store name used by Unity IAP when using its fake mode.
        /// Unity IAP library doesn't provide a constant for this one
        /// (like it provides GooglePlay.Name and AppleAppStore.Name)
        /// so let's define it ourselves.
        /// </summary>
        public const string UnityFakeStoreName = "fake";
    }
}
