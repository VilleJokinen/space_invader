// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Network;

namespace Metaplay.BotClient
{
    class BotClientDeviceGuidService : ISessionDeviceGuidService
    {
        string _deviceGuid = MetaGuid.None.ToString();

        void ISessionDeviceGuidService.StoreDeviceGuid(string deviceGuid)
        {
            _deviceGuid = deviceGuid;
        }
        public string TryGetDeviceGuid()
        {
            return _deviceGuid;
        }
    }
}
