// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Application
{
    /// <summary>
    /// Custom lifetime class that doesn't capture Ctrl-C presses.
    /// </summary>
    public class SimpleHostLifetime : IHostLifetime
    {
        public Task StopAsync(CancellationToken cancellationToken)
        {
            // nothing
            return Task.CompletedTask;
        }

        public Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            // no waiting for anything
            return Task.CompletedTask;
        }
    }
}
