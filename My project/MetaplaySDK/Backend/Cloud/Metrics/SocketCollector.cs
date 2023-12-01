// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Runtime.InteropServices;
using System.Threading;

namespace Metaplay.Cloud.Metrics
{
    public abstract partial class SocketCollector
    {
        object _collectLock = new object();

        public static SocketCollector CreateForCurrentOS()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return new SocketCollector.Linux();
            return new SocketCollector.Dummy();
        }

        public void Collect()
        {
            // Skip overlapping collects
            if (Monitor.TryEnter(_collectLock))
            {
                try
                {
                    CollectImpl();
                }
                finally
                {
                    Monitor.Exit(_collectLock);
                }
            }
        }

        public void Dispose()
        {
            lock (_collectLock)
                DisposeImpl();
        }

        protected abstract void CollectImpl();
        protected abstract void DisposeImpl();
    }
}
