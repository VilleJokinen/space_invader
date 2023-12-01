// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Network
{
    public struct DownloadStatus
    {
        /// <summary>
        /// Initializing -> Downloading -> ( Error | Timeout | Cancelled | Completed )
        /// </summary>
        public enum StatusCode
        {
            Initializing,
            Downloading,
            Error,
            Timeout,
            Cancelled,
            Completed
        }

        public StatusCode Code;
        public Exception Error; // set if Code == Error

        public DownloadStatus(StatusCode code, Exception error = null)
        {
            Code = code;
            Error = error;
        }
    }

    /// <summary>
    /// An interface representing a download in Metaplay. A download might contain multiple files, or be fulfilled
    /// completely from the cache.
    ///
    /// <para>
    /// IDownload can be periodically polled with <see cref="Status"/> and the returned status exposes the following
    /// state machine: <c>Initializing -> Downloading -> ( Error | Timeout | Cancelled | Completed )</c>.
    /// </para>
    ///
    /// <para>
    /// IDownload can be disposed at any time.
    /// </para>
    /// </summary>
    public interface IDownload : IDisposable
    {
        /// <summary>
        /// The current status of the download progress.
        ///
        /// <para>
        /// See <see cref="DownloadStatus.StatusCode"/>
        /// </para>
        ///
        /// <inheritdoc cref="DownloadStatus.StatusCode"/>
        /// </summary>
        DownloadStatus Status { get; }
    }
}
