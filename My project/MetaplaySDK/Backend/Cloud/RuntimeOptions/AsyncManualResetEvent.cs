// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Utility
{
    // https://blogs.msdn.microsoft.com/pfxteam/2012/02/11/building-async-coordination-primitives-part-1-asyncmanualresetevent/
    internal class AsyncManualResetEvent
    {
        private volatile TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

        public Task WaitAsync() => _tcs.Task;

        public void Set()
        {
            // https://stackoverflow.com/questions/12693046/configuring-the-continuation-behaviour-of-a-taskcompletionsources-task
            _ = Task.Run(() => _tcs.TrySetResult(true));
        }

        public void Reset()
        {
            while (true)
            {
                TaskCompletionSource<bool> tcs = _tcs;
                if (!tcs.Task.IsCompleted || Interlocked.CompareExchange(ref _tcs, new TaskCompletionSource<bool>(), tcs) == tcs)
                    return;
            }
        }
    }
}
