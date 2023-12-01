// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Tasks
{
    /// <summary>
    /// Executes Tasks in order on the desired TaskScheduler. A Task must be completed before
    /// any later tasks are executed. This means, that unlike with a task scheduler, enqueuing
    /// <c>Task.Delay</c> will block the whole queue until delay completion, after which the next task
    /// can proceed.
    ///
    /// <para>
    /// This is useful for throttling and for keeping the execution of multiple fire-and-forget tasks in order.
    /// </para>
    /// </summary>
    public sealed class TaskQueueExecutor
    {
        struct EnqueuedOp
        {
            public object                           Arg1;
            public object                           Arg2;
            public object                           Arg3;
            public Action<object, object, object>   Action;
            public Func<Task>                       AsyncAction;
        }

        public readonly TaskScheduler Scheduler;

        readonly object         _lock;
        Queue<EnqueuedOp>       _queue;
        Task                    _worker;
        WorkerWaitNextWaitable  _waitable;

        public TaskQueueExecutor(TaskScheduler scheduler)
        {
            Scheduler = scheduler;
            _lock = new object();
            _queue = new Queue<EnqueuedOp>();
            _waitable = new WorkerWaitNextWaitable(this);
        }

        /// <summary>
        /// Enqueues action to be run on the scheduler. The supplied arguments are passed into the Action. This enables
        /// the use of static, non-allocating actions where the creation of the delegate does not capture anything and is
        /// cached.
        /// </summary>
        public void EnqueueAsync(Action action)
        {
            EnqueueAsync(action, null, null, (object arg1, object arg2, object arg3) => ((Action)arg1)());
        }

        /// <inheritdoc cref="EnqueueAsync(Action)"/>
        public void EnqueueAsync(object arg1, Action<object> action)
        {
            EnqueueAsync(arg1, action, null, (object arg1, object arg2, object arg3) => ((Action<object>)arg2)(arg1));
        }

        /// <inheritdoc cref="EnqueueAsync(Action)"/>
        public void EnqueueAsync(object arg1, object arg2, Action<object, object> action)
        {
            EnqueueAsync(arg1, arg2, action, (object arg1, object arg2, object arg3) => ((Action<object, object>)arg3)(arg1, arg2));
        }

        /// <inheritdoc cref="EnqueueAsync(Action)"/>
        public void EnqueueAsync(object arg1, object arg2, object arg3, Action<object, object, object> action)
        {
            lock (_lock)
            {
                _queue.Enqueue(new EnqueuedOp()
                {
                    Arg1 = arg1,
                    Arg2 = arg2,
                    Arg3 = arg3,
                    Action = action,
                });
                EnsureWorker();
            }
        }

        /// <summary>
        /// Enqueues async function to be executed on the scheduler.
        /// </summary>
        public void EnqueueAsync(Func<Task> asyncAction)
        {
            lock (_lock)
            {
                _queue.Enqueue(new EnqueuedOp()
                {
                    AsyncAction = asyncAction,
                });
                EnsureWorker();
            }
        }

        void EnsureWorker()
        {
            if (_worker == null)
                _worker = MetaTask.Run(Worker, Scheduler);
            _waitable.NotifyIfAwaitingLocked();
        }

        class WorkerWaitNextWaitable
            : INotifyCompletion
            #if NETCOREAPP3_0_OR_GREATER
            , IThreadPoolWorkItem
            #endif
        {
            TaskQueueExecutor       _executor;
            EnqueuedOp              _result;
            Action                  _continuation;
            Action                  _continuationForThreadPool;
            SynchronizationContext  _continuationSynchronizationContext;

            public WorkerWaitNextWaitable(TaskQueueExecutor executor)
            {
                _executor = executor;
            }

            public WorkerWaitNextWaitable GetAwaiter()
            {
                // Await next. Complete immediately if has next available.
                lock (_executor._lock)
                {
                    if (_executor._queue.TryDequeue(out _result))
                        IsCompleted = true;
                    else
                        IsCompleted = false;
                }

                return this;
            }

            public void OnCompleted(Action continuation)
            {
                SynchronizationContext synchronizationContext = SynchronizationContext.Current;

                // Was not ready. Last check and if still not available, suspend and wait for wakeup.
                lock (_executor._lock)
                {
                    if (!_executor._queue.TryDequeue(out _result))
                    {
                        _continuation = continuation;
                        _continuationSynchronizationContext = synchronizationContext;
                        return;
                    }
                }

                // Completed while being enqueued
                continuation();
            }

            // Caller must have Lock
            public void NotifyIfAwaitingLocked()
            {
                if (_continuation != null)
                {
                    _result = _executor._queue.Dequeue();
                    if (_continuationSynchronizationContext != null)
                    {
                        // Enqueue on Synchronization context
    #pragma warning disable VSTHRD001 // Avoid legacy thread switching APIs
                        _continuationSynchronizationContext.Post((object action) =>
                        {
                            ((Action)action).Invoke();
                        }, _continuation);
    #pragma warning restore VSTHRD001 // Avoid legacy thread switching APIs
                    }
                    else
                    {
                        // Enqueue on thread pool
                        _continuationForThreadPool = _continuation;
                        #if NETCOREAPP3_0_OR_GREATER
                        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
                        #else
                        ThreadPool.UnsafeQueueUserWorkItem((self) =>
                        {
                            ((WorkerWaitNextWaitable)self).ExecuteOnThreadPool();
                        }, this);
                        #endif
                    }

                    _continuationSynchronizationContext = null;
                    _continuation = null;
                }
            }

            void ExecuteOnThreadPool()
            {
                Action continuation = _continuationForThreadPool;
                _continuationForThreadPool = null;
                continuation();
            }

            public bool IsCompleted { get; set; }
            public EnqueuedOp GetResult() => _result;

            #if NETCOREAPP3_0_OR_GREATER
            void IThreadPoolWorkItem.Execute() => ExecuteOnThreadPool();
            #endif
        }

        async Task Worker()
        {
            for (;;)
            {
                EnqueuedOp op = await _waitable;
                try
                {
                    if (op.Action != null)
                    {
                        op.Action(op.Arg1, op.Arg2, op.Arg3);
                    }
                    if (op.AsyncAction != null)
                    {
                        await op.AsyncAction();
                    }
                }
                catch
                {
                }
            }
        }
    }
}
