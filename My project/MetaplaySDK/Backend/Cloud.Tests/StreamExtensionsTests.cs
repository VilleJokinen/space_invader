// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    class StreamExtensionsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task TestReadAllAsync(bool usePolyfill)
        {
            Func<Stream, byte[], int, int, CancellationToken, Task> method;
            if (usePolyfill)
                method = StreamExtensions.ReadAllAsync;
            else
                method = StreamExtensions.PolyfillReadAllAsync;

            {
                using MemoryStream ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
                {
                    byte[] dst = new byte[6];
                    await method(ms, dst, 1, 4, CancellationToken.None);
                    Assert.AreEqual(new byte[6] { 0, 1, 2, 3, 4, 0 }, dst);
                }
            }
            {
                using MemoryStream ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
                {
                    byte[] dst = new byte[6];
                    Assert.ThrowsAsync<EndOfStreamException>(async () =>
                    {
                        await method(ms, dst, 0, 6, CancellationToken.None);
                    });
                }
            }
        }
    }
}
