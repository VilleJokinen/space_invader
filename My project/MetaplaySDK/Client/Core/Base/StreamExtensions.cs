// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    public static class StreamExtensions
    {
        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes into the buffer. If cannot read <paramref name="count"/> bytes, throws <see cref="EndOfStreamException"/>.
        /// </summary>
        public static Task ReadAllAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            #if NET7_0_OR_GREATER
            return stream.ReadExactlyAsync(buffer, offset, count, cancellationToken).AsTask();
            #else
            return PolyfillReadAllAsync(stream, buffer, offset, count, cancellationToken);
            #endif
        }

        /// <summary>
        /// Polyfill implementation. Do not use directly.
        /// </summary>
        public static async Task PolyfillReadAllAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            for (;;)
            {
                int numRead = await stream.ReadAsync(buffer, offset, count, cancellationToken);
                if (numRead == 0 && count != 0)
                    throw new EndOfStreamException();
                offset += numRead;
                count -= numRead;
                if (count == 0)
                    return;
            }
        }
    }
}
