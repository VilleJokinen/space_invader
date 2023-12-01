// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Network
{
    public sealed class SessionNonceService
    {
        readonly uint _appLaunchId;
        uint _sessionConnectionIndex;
        uint _sessionNonce;

        /// <param name="appLaunchId">The unique GUID for this client application launch</param>
        public SessionNonceService(Guid appLaunchId)
        {
            _appLaunchId = BitConverter.ToUInt32(appLaunchId.ToByteArray(), startIndex: 0);
        }

        /// <summary>
        /// Called when before new session is about to be started. This is also called if target server changes due to a redirect.
        /// </summary>
        public void NewSession()
        {
            _sessionNonce = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), startIndex: 0);
            _sessionConnectionIndex = 0;
        }

        /// <summary>
        /// Returns the session nonce generated in <see cref="NewSession"/>.
        /// </summary>
        public uint GetSessionNonce() => _sessionNonce;

        /// <summary>
        /// Called when new underlying connection has been created. Increments the connection index.
        /// </summary>
        public void NewConnection()
        {
            _sessionConnectionIndex++;
        }

        /// <summary>
        /// Returns the connection index, which is cleared in <see cref="NewSession"/> and incremented in <see cref="NewConnection"/>.
        /// </summary>
        public uint GetSessionConnectionIndex() => _sessionConnectionIndex;

        /// <summary>
        /// Returns the app-launch specfic ID for message transport.
        /// </summary>
        public uint GetTransportAppLaunchId() => _appLaunchId;
    }
}
