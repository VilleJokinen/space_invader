// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Utility;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using static System.FormattableString;

namespace Cloud.Tests
{
    [NonParallelizable]
    class WeakReferencingCacheTests
    {
        [Retry(3)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void TestTimedRetention(int numThreads)
        {
            const int numKeys = 1000;
            const int retentionMillis = 500;

            RandomPCG random = RandomPCG.CreateFromSeed(1);

            Stopwatch stopwatch = Stopwatch.StartNew();
            void Log(string message)
            {
                // Disabled to avoid spam - enable for debugging
                //Console.WriteLine("[t = {0}ms] {1}", stopwatch.ElapsedMilliseconds, message);
                _ = stopwatch;
            }

            WeakReferencingCacheWithTimedRetention<string, object> cache = new(minimumRetentionTime: TimeSpan.FromMilliseconds(retentionMillis));

            List<string> keys = Enumerable.Range(0, numKeys)
                                .Select(k => k.ToString(CultureInfo.InvariantCulture))
                                .ToList();

            List<string> keyAdditions = Enumerable.Repeat(keys, numThreads+1)
                                        .Flatten()
                                        .Shuffle(random)
                                        .ToList();

            Log(Invariant($"Retain {keys.Count} distinct keys, with {keyAdditions.Count} key additions (including duplicates) spread among {numThreads} threads"));
            RunThreads(numThreads, threadNdx =>
            {
                int                 keyNdxStart     = threadNdx*keyAdditions.Count/numThreads;
                int                 keyNdxEnd       = (threadNdx+1)*keyAdditions.Count/numThreads;
                IEnumerable<string> keysForThread   = keyAdditions.Skip(keyNdxStart).Take(keyNdxEnd-keyNdxStart);

                Log(Invariant($"  Thread {threadNdx}: Retaining {keysForThread.Count()} keys"));
                foreach (string key in keysForThread)
                {
                    Log(Invariant($"    Thread {threadNdx}: Retain key {key}"));
                    cache.GetCachedOrCreateNew(key, _ => new object());
                }
            });

            Log("Right afterwards, check all keys are retained");
            foreach (string key in keys)
                Assert.True(cache.IsRetainedDebug(key));

            Log(Invariant($"Sleep {retentionMillis/2}ms..."));
            Thread.Sleep(retentionMillis/2);

            Log("Check all keys are still retained");
            foreach (string key in keys)
                Assert.True(cache.IsRetainedDebug(key));

            OrderedSet<string> reRetainedKeys = keys.Shuffle(random)
                                                .Take(keys.Count/2)
                                                .ToOrderedSet();

            List<string> keyReRetentions = Enumerable.Repeat(reRetainedKeys, numThreads+1)
                                           .Flatten()
                                           .Shuffle(random)
                                           .ToList();

            Log(Invariant($"Re-retain {reRetainedKeys.Count} of the keys, with {keyReRetentions.Count} retentions (including duplicates) spread among {numThreads} threads; while leaving the rest of the keys on their original expiration"));
            RunThreads(numThreads, threadNdx =>
            {
                int                 keyNdxStart     = threadNdx*reRetainedKeys.Count/numThreads;
                int                 keyNdxEnd       = (threadNdx+1)*reRetainedKeys.Count/numThreads;
                IEnumerable<string> keysForThread   = reRetainedKeys.Skip(keyNdxStart).Take(keyNdxEnd-keyNdxStart);

                Log(Invariant($"  Thread {threadNdx}: Re-retaining {keysForThread.Count()}"));
                foreach (string key in keysForThread)
                {
                    Log(Invariant($"    Thread {threadNdx}: Re-retain key {key}"));
                    cache.GetCachedOrCreateNew(key, _ => new object());
                }
            });

            Log(Invariant($"Sleep {retentionMillis*3/5}ms..."));
            Thread.Sleep(retentionMillis*3/5);

            Log("Check each key's retention status according to whether it should've expired");
            foreach (string key in keys)
            {
                if (reRetainedKeys.Contains(key))
                {
                    Log($"  Check re-retained key {key} is still retained");
                    Assert.True(cache.IsRetainedDebug(key));
                }
                else
                {
                    Log($"  Check key {key} is no longer retained");
                    Assert.False(cache.IsRetainedDebug(key));
                }
            }

            Log(Invariant($"Sleep {retentionMillis/2}ms..."));
            Thread.Sleep(retentionMillis/2);

            Log("Check no key is retained anymore");
            foreach (string key in keys)
                Assert.False(cache.IsRetainedDebug(key));
        }

        void RunThreads(int numThreads, Action<int> func)
        {
            List<Thread> threads = new();

            for (int threadNdxIter = 0; threadNdxIter < numThreads; threadNdxIter++)
            {
                int threadNdx = threadNdxIter; // \note Below Lambda captures this, so copy into a non-mutated variable.
                threads.Add(new Thread(() => func(threadNdx)));
            }

            foreach (Thread thread in threads)
                thread.Start();

            foreach (Thread thread in threads)
                thread.Join();
        }
    }
}
