// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// We enable helpers only when ValueTask is in the core language. You could use older
// language and import polyfill packages, but we cannot autodetect that and won't be
// providing helpers.
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
    // dotnet path for .NET Standard 2.1
    #define HAVE_VALUETASK
#elif !NETCOREAPP && NETSTANDARD
    // unity path for .NET Standard 2.1
    #define HAVE_VALUETASK
#endif

using Metaplay.Core.Tasks;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    /// <summary>
    /// Extend <see cref="Task"/> with some methods.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Wrap a <see cref="Task"/> with a <see cref="CancellationToken"/>. Useful when the original Task
        /// doesn't accept a CancellationToken.
        /// </summary>
        /// <param name="task"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static Task WithCancelAsync(this Task task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return task;
            else
                return DoWithCancelAsync(task, cancellationToken);
        }

        static async Task DoWithCancelAsync(this Task task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwaitFalse())
                    throw new OperationCanceledException(cancellationToken);

                await task; // already completed; propagate any exception
            }
        }

        /// <summary>
        /// Wrap a <see cref="Task{TResult}"/> with a <see cref="CancellationToken"/>. Useful when the original Task
        /// doesn't accept a CancellationToken.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="task"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static Task<TResult> WithCancelAsync<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return task;
            else
                return DoWithCancelAsync<TResult>(task, cancellationToken);
        }

        static async Task<TResult> DoWithCancelAsync<TResult>(Task<TResult> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwaitFalse())
                {
                    // suppress any potential unobserved errors by observing the error whenever it completes
                    _ = task.ContinueWithCtx(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new OperationCanceledException(cancellationToken);
                }

                return await task; // already completed; propagate any exception
            }
        }

        /// <summary>
        /// Continues a successful <see cref="Task{TResult}"/> with a Dispose() for the TResult. For faulting task, the
        /// Exception is observed and ignored.
        ///
        /// <para>
        /// If <paramref name="task"/> is already Completed and <paramref name="allowSynchronousExecution"/> is set,
        /// the Dispose() is run synchronously by the calling thread.
        /// </para>
        /// </summary>
        public static void ContinueWithDispose<TResult>(this Task<TResult> task, bool allowSynchronousExecution = true) where TResult : IDisposable
        {
            if (allowSynchronousExecution)
            {
                switch (task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        #pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                        task.Result.Dispose();
                        #pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
                        return;

                    case TaskStatus.Faulted:
                        // Task failed, but we have already intended the failing component to
                        // be disposed. Just silence the exception.
                        _ = task.Exception;
                        return;
                }
            }

            _ = task.ContinueWithCtx((Task<TResult> t) =>
            {
                switch (t.Status)
                {
                    case TaskStatus.RanToCompletion:
                        #pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                        t.Result.Dispose();
                        #pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
                        break;

                    case TaskStatus.Faulted:
                        // As above
                        _ = t.Exception;
                        break;
                }
            });
        }

        /// <summary>
        /// Returns the <see cref="Task{TResult}.Result"/> of the <paramref name="task"/> if it is available. If the
        /// task has failed or cancelled, throws an <see cref="AggregateException"/>. Otherwise, i.e., if the task
        /// is not complete, throws an <see cref="InvalidOperationException"/>.
        /// <para>
        /// Use of this helper prevents accidentally blocking the calling thread if <c>Result</c> is not available.
        /// </para>
        /// </summary>
        public static TResult GetCompletedResult<TResult>(this Task<TResult> task)
        {
            TaskStatus status = task.Status;
            switch (status)
            {
                case TaskStatus.RanToCompletion:
                case TaskStatus.Faulted:
                case TaskStatus.Canceled:
                    #pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                    return task.Result;
                    #pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

                default:
                    throw new InvalidOperationException("Cannot get result, Task is not complete");
            }
        }

        #if HAVE_VALUETASK
        /// <summary>
        /// Returns the <see cref="ValueTask{TResult}.Result"/> of the <paramref name="task"/> if it is available. If the
        /// task has failed throws the exception that failed the operation. If the task is cancelled, throws <see cref="TaskCanceledException"/>.
        /// Otherwise, i.e., if the task is not complete, throws an <see cref="InvalidOperationException"/>.
        /// <para>
        /// Use of this helper prevents accidentally blocking the calling thread if <c>Result</c> is not available.
        /// </para>
        /// </summary>
        public static TResult GetCompletedResult<TResult>(this ValueTask<TResult> task)
        {
            if (task.IsCompleted)
            {
                #pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                return task.Result;
                #pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
            }

            throw new InvalidOperationException("Cannot get result, ValueTask is not complete");
        }
        #endif
    }
}
