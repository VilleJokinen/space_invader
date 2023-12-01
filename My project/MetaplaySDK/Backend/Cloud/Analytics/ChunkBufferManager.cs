// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Analytics
{
    /// <summary>
    /// Manages a bounded set of buffers (MemoryStreams) for writing analytics events into, and then flushing in the
    /// background to some external storage.
    /// </summary>
    public class ChunkBufferManager : IAsyncDisposable
    {
        ConcurrentStack<MemoryStream>       _memoryStreams  = new ConcurrentStack<MemoryStream>();
        ConcurrentDictionary<Task, bool>    _flushTasks     = new ConcurrentDictionary<Task, bool>();

        public ChunkBufferManager(int numChunkBuffers)
        {
            if (numChunkBuffers < 2)
                throw new ArgumentException($"Must use at least 2 chunks for buffering", nameof(numChunkBuffers));

            for (int ndx = 0; ndx < numChunkBuffers; ndx++)
                _memoryStreams.Push(new MemoryStream(capacity: 65536));
        }

        public bool TryAllocate(out MemoryStream memoryStream)
        {
            return _memoryStreams.TryPop(out memoryStream);
        }

        public void ReleaseAndFlush(MemoryStream memoryStream, Func<byte[], Task> flushAction)
        {
            // Execute flush in background
            Task flushTask = Task.Run(async () =>
            {
                try
                {
                    // Let the user handle the flush
                    // \todo [petri] recycle memory instead of ToArray()
                    await flushAction(memoryStream.ToArray()).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // \todo [petri] log exception? should be logged inside flushAction() already, though
                }
                finally
                {
                    // Reset memoryStream
                    memoryStream.Position = 0;
                    memoryStream.SetLength(0);

                    // Release memory stream for re-use
                    _memoryStreams.Push(memoryStream);
                }
            });

            // Register as flush task
            _flushTasks.TryAdd(flushTask, true);

            // When done, remove self from flush tasks
            _ = flushTask.ContinueWith(task =>
            {
                bool isSuccess = _flushTasks.TryRemove(flushTask, out bool value);
                _ = isSuccess;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public async ValueTask DisposeAsync()
        {
            // Wait for all pending flush tasks to complete
            await Task.WhenAll(_flushTasks.Keys.ToArray()).ConfigureAwait(false);
        }
    }
}
