// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Network;

namespace Metaplay.Unity
{
    class UnitySessionGuidService : ISessionDeviceGuidService
    {
        protected readonly IMetaplayConnectionSDKHook _sdk;

        public UnitySessionGuidService(IMetaplayConnectionSDKHook sdk)
        {
            _sdk = sdk;
        }

        string ISessionDeviceGuidService.TryGetDeviceGuid() => _sdk.GetDeviceGuid();
        void ISessionDeviceGuidService.StoreDeviceGuid(string deviceGuid) => _sdk.SetDeviceGuid(deviceGuid);
    }
}
