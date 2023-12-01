// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

#if UNITY_WEBGL_BUILD
# define METATASK_USE_MAIN_THREAD_ONLY
#endif

#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL" regarding Task usage. MetaTask is the assumed-safe wrapper.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Tasks
{
    public static class MetaTask
    {
        #if !NETCOREAPP
        /// <summary>
        /// Task Scheduler for Unity's main thread.
        /// </summary>
        public static TaskScheduler UnityMainScheduler;
        #endif

        /// <summary>
        /// Task Scheduler for background tasks.
        /// <list type="bullet">
        /// <item>On WebGL, this is the main thread as there are no threads.</item>
        /// <item>On other platforms, this is the default thread pool.</item>
        /// </list>
        ///
        /// On WebGL in Unity Editor we use the Main thread too to keep behavior between Editor and Builds.
        /// </summary>
        public static TaskScheduler BackgroundScheduler;

        static TaskScheduler _taskScheduler;

        public static void Initialize()
        {
            #if !NETCOREAPP
            UnityMainScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            #endif

            #if UNITY_WEBGL
            BackgroundScheduler = UnityMainScheduler;
            #else
            BackgroundScheduler = TaskScheduler.Default;
            #endif

            #if METATASK_USE_MAIN_THREAD_ONLY
            // Use TaskScheduler that uses Unity's synchronization context.
            _taskScheduler = UnityMainScheduler;
            #else
            _taskScheduler = TaskScheduler.Default;
            #endif
        }

        static void EnsureTaskScheduler()
        {
            if (_taskScheduler == null)
                throw new InvalidOperationException("MetaTask not initialized yet!");
        }

        #region Run methods

        #if METATASK_USE_MAIN_THREAD_ONLY
        public static Task Run(Action action)
        {
            return Run(action, default(CancellationToken));
        }

        public static Task Run(Action action, CancellationToken cancellationToken)
        {
            EnsureTaskScheduler();
            return Task.Factory.StartNew(
                action,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                _taskScheduler);
        }

        public static Task<TResult> Run<TResult>(Func<TResult> function)
        {
            return Run(function, default(CancellationToken));
        }

        public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)
        {
            EnsureTaskScheduler();
            return Task<TResult>.Factory.StartNew(
                function,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                _taskScheduler);
        }

        public static Task Run(Func<Task> function)
        {
            return Run(function, default(CancellationToken));
        }

        public static Task Run(Func<Task> function, CancellationToken cancellationToken)
        {
            if (function == null)
                throw new ArgumentNullException(nameof(function));

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            EnsureTaskScheduler();

            Task<Task> task = Task<Task>.Factory.StartNew(
                function,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                _taskScheduler);

            return task.Unwrap();
        }


        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function)
        {
            return Run(function, default(CancellationToken));
        }

        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken)
        {
            if (function == null)
                throw new ArgumentNullException(nameof(function));

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<TResult>(cancellationToken);

            EnsureTaskScheduler();

            Task<Task<TResult>> task = Task<Task<TResult>>.Factory.StartNew(
                function,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                _taskScheduler);

            return task.Unwrap();
        }

        #else

        public static Task Run(Action action)
        {
            return Task.Run(action);
        }

        public static Task Run(Action action, CancellationToken cancellationToken)
        {
            return Task.Run(action, cancellationToken);
        }

        public static Task<TResult> Run<TResult>(Func<TResult> function)
        {
            return Task.Run(function);
        }

        public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)

        {
            return Task.Run(function, cancellationToken);
        }

        public static Task Run(Func<Task> function)
        {
            return Task.Run(function);
        }

        public static Task Run(Func<Task> function, CancellationToken cancellationToken)
        {
            return Task.Run(function, cancellationToken);
        }

        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function)
        {
            return Task.Run(function);
        }

        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken)
        {
            return Task.Run(function, cancellationToken);
        }

        #endif

        #endregion

        #region Delay methods

        #if METATASK_USE_MAIN_THREAD_ONLY
        public static Task Delay(TimeSpan delay) => MetaDelay.Delay(delay);

        public static Task Delay(TimeSpan delay, CancellationToken cancellationToken) => MetaDelay.Delay(delay, cancellationToken);

        public static Task Delay(int millisecondsDelay) => MetaDelay.Delay(millisecondsDelay);

        public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken) => MetaDelay.Delay(millisecondsDelay, cancellationToken);
        #else
        public static Task Delay(TimeSpan delay)
        {
            return Task.Delay(delay);
        }

        public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }

        public static Task Delay(int millisecondsDelay)
        {
            return Task.Delay(millisecondsDelay);
        }

        public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
        {
            return Task.Delay(millisecondsDelay, cancellationToken);
        }

        #endif

        #endregion

        #region ContinueWithCtx

        public static Task ContinueWithCtx(this Task task, Action<Task> continuation, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, token, TaskContinuationOptions.None);

        public static Task ContinueWithCtx(this Task task, Action<Task> continuation, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, default(CancellationToken), options);

        public static Task ContinueWithCtx(this Task task, Action<Task> continuation, CancellationToken token, TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, token, options, _taskScheduler);
        }

        public static Task<TResult> ContinueWithCtx<TResult>(this Task task, Func<Task, TResult> continuation, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, token, TaskContinuationOptions.None);

        public static Task<TResult> ContinueWithCtx<TResult>(this Task task, Func<Task, TResult> continuation, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, default(CancellationToken), options);

        public static Task<TResult> ContinueWithCtx<TResult>(this Task task, Func<Task, TResult> continuation, CancellationToken token, TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, token, options, _taskScheduler);
        }

        public static Task ContinueWithCtx<TTaskResult>(this Task<TTaskResult> task, Action<Task<TTaskResult>> continuation, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, token, TaskContinuationOptions.None);

        public static Task ContinueWithCtx<TTaskResult>(this Task<TTaskResult> task, Action<Task<TTaskResult>> continuation, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, default(CancellationToken), options);

        public static Task ContinueWithCtx<TTaskResult>(this Task<TTaskResult> task, Action<Task<TTaskResult>> continuation, CancellationToken token, TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, token, options, _taskScheduler);
        }

        public static Task<TResult> ContinueWithCtx<TTaskResult, TResult>(this Task<TTaskResult> task, Func<Task<TTaskResult>, TResult> continuation, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, token, TaskContinuationOptions.None);

        public static Task<TResult> ContinueWithCtx<TTaskResult, TResult>(this Task<TTaskResult> task, Func<Task<TTaskResult>, TResult> continuation, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, default(CancellationToken), options);

        public static Task<TResult> ContinueWithCtx<TTaskResult, TResult>(this Task<TTaskResult> task, Func<Task<TTaskResult>, TResult> continuation, CancellationToken token, TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, token, options, _taskScheduler);
        }


        public static Task ContinueWithCtx(this Task task, Action<Task, object> continuation, object state, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, state, token, TaskContinuationOptions.None);

        public static Task ContinueWithCtx(this Task task, Action<Task, object> continuation, object state, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, state, default(CancellationToken), options);

        public static Task ContinueWithCtx(
            this Task task,
            Action<Task, object> continuation,
            object state,
            CancellationToken token,
            TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, state, token, options, _taskScheduler);
        }

        public static Task<TResult> ContinueWithCtx<TResult>(this Task task, Func<Task, object, TResult> continuation, object state, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, state, token, TaskContinuationOptions.None);

        public static Task<TResult> ContinueWithCtx<TResult>(this Task task, Func<Task, object, TResult> continuation, object state, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, state, default(CancellationToken), options);

        public static Task<TResult> ContinueWithCtx<TResult>(
            this Task task,
            Func<Task, object, TResult> continuation,
            object state,
            CancellationToken token,
            TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, state, token, options, _taskScheduler);
        }

        public static Task ContinueWithCtx<TTaskResult>(this Task<TTaskResult> task, Action<Task<TTaskResult>, object> continuation, object state, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, state, token, TaskContinuationOptions.None);

        public static Task ContinueWithCtx<TTaskResult>(this Task<TTaskResult> task, Action<Task<TTaskResult>, object> continuation, object state, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, state, default(CancellationToken), options);

        public static Task ContinueWithCtx<TTaskResult>(
            this Task<TTaskResult> task,
            Action<Task<TTaskResult>, object> continuation,
            object state,
            CancellationToken token,
            TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, state, token, options, _taskScheduler);
        }

        public static Task<TResult> ContinueWithCtx<TTaskResult, TResult>(this Task<TTaskResult> task, Func<Task<TTaskResult>, object, TResult> continuation, object state, CancellationToken token = default)
            => ContinueWithCtx(task, continuation, state, token, TaskContinuationOptions.None);

        public static Task<TResult> ContinueWithCtx<TTaskResult, TResult>(this Task<TTaskResult> task, Func<Task<TTaskResult>, object, TResult> continuation, object state, TaskContinuationOptions options)
            => ContinueWithCtx(task, continuation, state, default(CancellationToken), options);

        public static Task<TResult> ContinueWithCtx<TTaskResult, TResult>(
            this Task<TTaskResult> task,
            Func<Task<TTaskResult>, object, TResult> continuation,
            object state,
            CancellationToken token,
            TaskContinuationOptions options)
        {
            EnsureTaskScheduler();
            return task.ContinueWith(continuation, state, token, options, _taskScheduler);
        }

        #endregion


        #if METATASK_USE_MAIN_THREAD_ONLY
        /// <summary>
        /// Use instead of <see cref="Task.ConfigureAwait"/>
        /// in client code to avoid breaking WebGL.
        /// </summary>
        public static Task ConfigureAwaitFalse(this Task task)
            => task;

        /// <summary>
        /// Use instead of <see cref="Task{TResult}.ConfigureAwait"/>
        /// in client code to avoid breaking WebGL.
        /// </summary>
        public static Task<TResult> ConfigureAwaitFalse<TResult>(this Task<TResult> task)
            => task;
        #else
        public static ConfiguredTaskAwaitable ConfigureAwaitFalse(this Task task)
            =>  task.ConfigureAwait(false);

        public static ConfiguredTaskAwaitable<TResult> ConfigureAwaitFalse<TResult>(this Task<TResult> task)
            =>  task.ConfigureAwait(false);
        #endif

        /// <summary>
        /// Enqueues the execution of sync function on the <paramref name="scheduler"/>.
        /// <code>
        /// int result = await MetaTask.Run(() => { return 1; }, MetaTask.UnityMainScheduler);
        /// // result == 1
        /// </code>
        /// </summary>
        public static Task<TResult> Run<TResult>(Func<TResult> op, TaskScheduler scheduler)
        {
            return Task.Factory.StartNew(op, CancellationToken.None, TaskCreationOptions.None, scheduler);
        }

        /// <summary>
        /// Enqueues the execution of async function on the <paramref name="scheduler"/>.
        /// <code>
        /// int result = await MetaTask.Run(async () => { await Task.Delay(500); return 1; }, MetaTask.UnityMainScheduler);
        /// // result == 1
        /// </code>
        /// </summary>
        public static Task<TResult> Run<TResult>(Func<Task<TResult>> op, TaskScheduler scheduler)
        {
            return Task.Factory.StartNew(op, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
        }

        /// <summary>
        /// Enqueues the execution of sync operation on the <paramref name="scheduler"/>.
        /// <code>
        /// await MetaTask.Run(() => { /* something on unity thread */ }, MetaTask.UnityMainScheduler);
        /// </code>
        /// </summary>
        public static Task Run(Action op, TaskScheduler scheduler)
        {
            return Task.Factory.StartNew(op, CancellationToken.None, TaskCreationOptions.None, scheduler);
        }

        /// <summary>
        /// Enqueues the execution of async operation on the <paramref name="scheduler"/>.
        /// <code>
        /// await MetaTask.Run(async () => { await Task.Delay(500); }, MetaTask.UnityMainScheduler);
        /// </code>
        /// </summary>
        public static Task Run(Func<Task> op, TaskScheduler scheduler)
        {
            return Task.Factory.StartNew(op, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
        }

#if UNITY_EDITOR
        // Make sure MetaTask is usable on Editor too.
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeForEditor()
        {
            Initialize();
        }
#endif
    }
}

#pragma warning restore MP_WGL_00
