// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Metaplay.Cloud.Entity
{
    /// <summary>
    /// Pending task that should be executed on actor context.
    ///
    /// See: <see cref="EntityActor.ExecuteOnActorContextAsync"/>.
    /// </summary>
    struct PendingOnActorContextTask
    {
        public Func<Task>   ExecuteOpAsync { get; }
        public Action       CancelFunc { get; }

        public PendingOnActorContextTask(Func<Task> executeOpAsync, Action cancelFunc)
        {
            ExecuteOpAsync = executeOpAsync;
            CancelFunc = cancelFunc;
        }
    }

    /// <summary>
    /// Command for informing an actor that it should execute next pending action on its actor context.
    /// See: <see cref="EntityActor.ExecuteOnActorContextAsync"/>.
    /// </summary>
    sealed class ExecuteNextPendingOnActorContextTask
    {
        public static readonly ExecuteNextPendingOnActorContextTask Instance = new ExecuteNextPendingOnActorContextTask();
    }

    public partial class EntityActor
    {
        object                              _executeOnActorContextQueueLock = null;
        Queue<PendingOnActorContextTask>    _executeOnActorContextQueue     = null; // if null, then actor is shut down

        void InitializeExecuteOnActorContext()
        {
            _executeOnActorContextQueueLock = new object();
            _executeOnActorContextQueue = new Queue<PendingOnActorContextTask>();
        }

        void CancelAllPendingOnActorContextTasksForEntityShutdown()
        {
            Queue<PendingOnActorContextTask> queue;
            lock (_executeOnActorContextQueueLock)
            {
                if (_executeOnActorContextQueue == null)
                    return;
                queue = _executeOnActorContextQueue;
                _executeOnActorContextQueue = null;
            }

            foreach(PendingOnActorContextTask task in queue)
                task.CancelFunc();
        }

        /// <summary>
        /// Enqueue and execute <paramref name="foregroundOp"/> on actor's execute context. Completes after <paramref name="foregroundOp"/> has been run to completion.
        /// If entity shuts down before operation can be completed, the returned task is Cancelled. This method can be called from actor's main thread (to defer execution)
        /// or from background thread (to post work on actor thread).
        /// <para>
        /// See also <seealso cref="ContinueTaskOnActorContext"/>
        /// </para>
        /// </summary>
        protected Task ExecuteOnActorContextAsync(Action foregroundOp)
        {
            return DoExecuteOnActorContextAsync<int>(() =>
                {
                    foregroundOp();
                    return Task.FromResult(0);
                });
        }

        /// <summary>
        /// Enqueue and execute async <paramref name="foregroundOp"/> to completion on actor's execute context. Completes after <paramref name="foregroundOp"/> has been run to completion.
        /// If entity shuts down before operation can be completed, the returned task is Cancelled. This method can be called from actor's main thread (to defer execution)
        /// or from background thread (to post work on actor thread).
        /// <para>
        /// See also <seealso cref="ContinueTaskOnActorContext"/>
        /// </para>
        /// </summary>
        protected Task ExecuteOnActorContextAsync(Func<Task> foregroundOp)
        {
            return DoExecuteOnActorContextAsync<int>(async () =>
                {
                    await foregroundOp();
                    return 0;
                });
        }

        /// <summary>
        /// Enqueue and execute <paramref name="foregroundOp"/> on actor's execute context and returns the result.
        /// If entity shuts down before operation can be completed, the returned task is Cancelled. This method can be called from actor's main thread (to defer execution)
        /// or from background thread (to post work on actor thread).
        /// <para>
        /// See also <seealso cref="ContinueTaskOnActorContext"/>
        /// </para>
        /// </summary>
        protected Task<TResult> ExecuteOnActorContextAsync<TResult>(Func<TResult> foregroundOp)
        {
            return DoExecuteOnActorContextAsync(() =>
                {
                    return Task.FromResult(foregroundOp());
                });
        }

        /// <summary>
        /// Enqueue and execute async <paramref name="foregroundOp"/> to completion on actor's execute context and returns the result.
        /// If entity shuts down before operation can be completed, the returned task is Cancelled. This method can be called from actor's main thread (to defer execution)
        /// or from background thread (to post work on actor thread).
        /// <para>
        /// See also <seealso cref="ContinueTaskOnActorContext"/>
        /// </para>
        /// </summary>
        protected Task<TResult> ExecuteOnActorContextAsync<TResult>(Func<Task<TResult>> foregroundOp)
        {
            return DoExecuteOnActorContextAsync(foregroundOp);
        }

        Task<TResult> DoExecuteOnActorContextAsync<TResult>(Func<Task<TResult>> foregroundOp)
        {
            // Set RunContinuationsAsynchronously to avoid Actor context leaking to awaiter on caller side. Safer, but worse latency.
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task> executeOpAsync = async () =>
            {
                try
                {
                    tcs.TrySetResult(await foregroundOp());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };
            Action cancelFunc = () => tcs.TrySetCanceled();

            lock (_executeOnActorContextQueueLock)
            {
                if (_executeOnActorContextQueue == null)
                {
                    // actor has been shut down.
                    return Task.FromCanceled<TResult>(CancellationToken.None);
                }

                // actor is still running. Push to message queue
                _executeOnActorContextQueue.Enqueue(new PendingOnActorContextTask(executeOpAsync, cancelFunc));
                Tell(_self, ExecuteNextPendingOnActorContextTask.Instance);
            }

            return tcs.Task;
        }

        [CommandHandler]
        async Task HandleExecuteNextPendingOnActorContextTask(ExecuteNextPendingOnActorContextTask _)
        {
            PendingOnActorContextTask request;
            lock (_executeOnActorContextQueueLock)
            {
                if (_executeOnActorContextQueue == null)
                    return;
                if (!_executeOnActorContextQueue.TryDequeue(out request))
                    return;
            }

            await request.ExecuteOpAsync();
        }
    }
}
