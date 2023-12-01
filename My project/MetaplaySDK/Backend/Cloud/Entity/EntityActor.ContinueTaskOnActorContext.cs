// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Entity
{
    /// <summary>
    /// Message for informing an actor that a background task of its has been completed (either successfully or with error).
    ///
    /// See: <see cref="EntityActor.ContinueTaskOnActorContext{TResult}(Task{TResult}, Func{TResult, Task}, Action{Exception})"/>.
    /// </summary>
    class BackgroundTaskCompleted
    {
        public bool         IsSuccess       { get; private set; }
        public Func<Task>   HandleFuncAsync { get; private set; }

        private BackgroundTaskCompleted(bool isSuccess, Func<Task> handleFunc) { IsSuccess = isSuccess; HandleFuncAsync = handleFunc; }

        public static BackgroundTaskCompleted Success(Func<Task> handleFunc)
        {
            return new BackgroundTaskCompleted(isSuccess: true, handleFunc);
        }

        public static BackgroundTaskCompleted Failure(Action handleFunc)
        {
            return new BackgroundTaskCompleted(isSuccess: false, () => { handleFunc(); return Task.CompletedTask; });
        }
    }

    public abstract partial class EntityActor
    {
        /// <summary>
        /// Execute a task success or failure completion handler on Actor's own execution context when task completes. This is useful
        /// for when a long-running async operation has been deferred to a thread pool and the result needs to be routed
        /// back, or when a long-running async operation needs to be deferred such that it doesn't block mailbox processing.
        /// <para>
        /// Note that <c>ContinueTaskOnActorContext(task, ...)</c> is essentially the same as <c>task.ContinueWith(task => ExecuteOnActorContext(...))</c>.
        /// </para>
        /// <code>
        /// <![CDATA[
        /// // Run on thread pool and route back
        /// int MyComputeHeavyTask(int param)
        /// {
        ///    return FindNextPrime(param);
        /// }
        /// ContinueTaskOnActorContext(Task.Run(() => MyComputeHeavyTask(param)), result => {}, failure => {});
        ///
        /// // Run deferred on background on the actor context (does not block the mailbox).
        /// async Task<int> MyLongRunningActorTaskAsync(int param)
        /// {
        ///   await Task.Delay(1000);
        ///   await EntityAskAsync<>(param);
        ///   return 1;
        /// }
        /// ContinueTaskOnActorContext(MyLongRunningActorTaskAsync(param)), result => {}, failure => {});
        /// ]]>
        /// </code>
        /// <para>
        /// See also <see cref="ExecuteOnActorContextAsync"/>
        /// </para>
        /// </summary>
        /// <typeparam name="TResult">Type of the result value from task</typeparam>
        /// <param name="asyncTask">Async operation</param>
        /// <param name="handleSuccessAsync">Handler callback for successful operation</param>
        /// <param name="handleFailure">Handler callback for failure (an exception thrown from function)</param>
        protected void ContinueTaskOnActorContext<TResult>(Task<TResult> asyncTask, Func<TResult, Task> handleSuccessAsync, Action<Exception> handleFailure)
        {
            _ = asyncTask
                .ContinueWith((Task<TResult> task) =>
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        Tell(_self, BackgroundTaskCompleted.Success(async () => await handleSuccessAsync(task.GetCompletedResult()).ConfigureAwait(false)));
                    }
                    else
                    {
                        Tell(_self, BackgroundTaskCompleted.Failure(() =>
                        {
                            // Unwrap AggregateException if it's a trivial exception
                            Exception unwrappedEx;
                            if (task.Exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
                                unwrappedEx = aggregate.InnerExceptions[0];
                            else
                                unwrappedEx = task.Exception;

                            handleFailure(unwrappedEx);
                        }));
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <inheritdoc cref="ContinueTaskOnActorContext{TResult}(Task{TResult}, Func{TResult, Task}, Action{Exception})"/>
        protected void ContinueTaskOnActorContext<TResult>(Task<TResult> asyncTask, Action<TResult> handleSuccess, Action<Exception> handleFailure)
        {
            ContinueTaskOnActorContext(asyncTask, result => { handleSuccess(result); return Task.CompletedTask; }, handleFailure);
        }

        void InitializeContinueTaskOnActorContext()
        {
            ReceiveAsync<BackgroundTaskCompleted>(ReceiveBackgroundTaskCompleted);
        }

        async Task ReceiveBackgroundTaskCompleted(BackgroundTaskCompleted completed)
        {
            await completed.HandleFuncAsync();
        }
    }
}
