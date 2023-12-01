// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !UNITY_WEBGL || UNITY_EDITOR
#define HAS_FILESYSTEM
#endif

#if HAS_FILESYSTEM
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is non-WebGL.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    /// <summary>
    /// Lock for a certain path in the filesystem.  The lock is opt-in and can prevent concurrent modifications only if all participants
    /// use it. This is used by atomic filesystem utilities operations to avoid clobbering their temporary paths.
    /// </summary>
    public struct FileAccessLock : IDisposable
    {
        struct OngoingOp
        {
            public SemaphoreSlim Semaphore;
            public int NumOngoing;

            public OngoingOp(SemaphoreSlim semaphore, int numOngoing)
            {
                Semaphore = semaphore;
                NumOngoing = numOngoing;
            }
        }

        readonly static object s_lock = new object();
        static Dictionary<string, OngoingOp> s_ops = null;

        string _canonicalPath;
        SemaphoreSlim _semaphore;

        FileAccessLock(string canonicalPath, SemaphoreSlim semaphore)
        {
            _canonicalPath = canonicalPath;
            _semaphore = semaphore;
        }
        void IDisposable.Dispose()
        {
            SemaphoreSlim s = _semaphore;
            _semaphore = null;
            if (s != null)
            {
                FreeLock(_canonicalPath, s);
            }
        }

        public static async Task<FileAccessLock> AcquireAsync(string path)
        {
            (string canonicalPath, SemaphoreSlim semaphore) = AllocateLock(path);
            await semaphore.WaitAsync().ConfigureAwait(false);
            return new FileAccessLock(canonicalPath, semaphore);
        }

        public static FileAccessLock AcquireSync(string path)
        {
            (string canonicalPath, SemaphoreSlim semaphore) = AllocateLock(path);
            semaphore.Wait();
            return new FileAccessLock(canonicalPath, semaphore);
        }

        static (string, SemaphoreSlim) AllocateLock(string path)
        {
            string canonicalPath = FileUtil.NormalizePath(path);
            SemaphoreSlim semaphore;
            lock (s_lock)
            {
                if (s_ops == null)
                    s_ops = new Dictionary<string, OngoingOp>();

                if (!s_ops.TryGetValue(canonicalPath, out OngoingOp ongoingOp))
                {
                    semaphore = new SemaphoreSlim(initialCount: 1);
                    s_ops.Add(canonicalPath, new OngoingOp(semaphore, 1));
                }
                else
                {
                    semaphore = ongoingOp.Semaphore;
                    s_ops[canonicalPath] = new OngoingOp(semaphore, ongoingOp.NumOngoing + 1);
                }
            }

            return (canonicalPath, semaphore);
        }

        static void FreeLock(string canonicalPath, SemaphoreSlim semaphore)
        {
            semaphore.Release();

            lock (s_lock)
            {
                if (s_ops == null)
                    return;
                if (!s_ops.TryGetValue(canonicalPath, out OngoingOp ongoingOp))
                    return;
                if (semaphore != ongoingOp.Semaphore)
                    return;

                if (ongoingOp.NumOngoing > 1)
                {
                    s_ops[canonicalPath] = new OngoingOp(semaphore, ongoingOp.NumOngoing - 1);
                    return;
                }

                s_ops.Remove(canonicalPath);
                if (s_ops.Count == 0)
                    s_ops = null;
            }
        }
    }
}

#endif
