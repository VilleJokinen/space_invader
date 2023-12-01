// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Metaplay.Core.Network
{
    public interface IHasMinimalErrorDescription
    {
        /// <summary>
        /// Returns a minimal human-readable description of the error. This value overrides the default
        /// value of <see cref="NetworkErrorLoggingUtil.GetMinimalDescription(Exception)"/>.
        /// If this method returns null, the default error string is returned insted.
        /// </summary>
        string GetMinimalDescription();
    }

    public static class NetworkErrorLoggingUtil
    {
        /// <summary>
        /// Generates the minimal human-readable description of the error. Essentially, this
        /// summarizes all well-known errors into a very short error messages and keeps unknown
        /// errors as is. Default behavior may be overriden by implementing <see cref="IHasMinimalErrorDescription"/>
        /// in the supplied error.
        /// </summary>
        public static string GetMinimalDescription(Exception ex)
        {
            if (ex == null)
                return "null";

            Exception baseEx;
            if (ex is AggregateException agex)
                baseEx = agex.GetBaseException();
            else
                baseEx = ex;

            return TrySummarizeException(baseEx) ?? ex.ToString();
        }

        static string TrySummarizeException(Exception ex)
        {
            if (ex is SocketException soex)
            {
                if (soex.SocketErrorCode == SocketError.ConnectionRefused)
                    return "connection refused";
                else if (soex.SocketErrorCode == SocketError.ConnectionAborted)
                    return "connection aborted";
                else if (soex.SocketErrorCode == SocketError.ConnectionReset)
                    return "connection reset by peer";
                else if (soex.SocketErrorCode == SocketError.NotConnected)
                    return "not connected";
            }

            if (ex is IOException ioex)
            {
                string ioCauseMaybe = TrySummarizeException(ioex.InnerException);
                if (ioCauseMaybe != null)
                    return $"io exception: {ioCauseMaybe}";
            }

            if (ex is WebException webex)
            {
                if (webex.Status == WebExceptionStatus.ConnectFailure)
                    return "could not connect";
            }

            if (ex is IHasMinimalErrorDescription medex)
            {
                string description = medex.GetMinimalDescription();
                if (description != null)
                    return description;
            }

            return null;
        }
    }
}
