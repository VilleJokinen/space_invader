// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Network
{
    public interface ISessionDeviceGuidService
    {
        /// <summary>
        /// Returns the current DeviceGuid of this device or null if no DeviceGuid has been set.
        /// </summary>
        /// <remarks>May be called on any thread.</remarks>
        string TryGetDeviceGuid();

        /// <summary>
        /// Stores the Device Guid.
        /// </summary>
        /// <remarks>May be called on any thread.</remarks>
        void StoreDeviceGuid(string deviceGuid);
    }
}
