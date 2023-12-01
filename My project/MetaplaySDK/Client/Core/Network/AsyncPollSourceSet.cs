// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Threading.Tasks;
using System;
using System.Threading;
using Metaplay.Core.Tasks;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// A set of items to asynchronously wait, akin to <c>fs_sets</c> in <c>select(2)</c>, or <c>poll(2)</c>.
    /// <para>Usage:</para>
    /// <code>
    /// AsyncPollSourceSet set = new AsyncPollSourceSet();
    /// set.Begin()
    /// set.Add*();
    /// await set.WaitAsync();
    /// </code>
    /// </summary>
    public class AsyncPollSourceSet
    {
        Task[]              _tasks      = new Task[4];
        int                 _count      = 0;
        DateTime?           _deadlineAt;
        CancellationToken   _ct;
        bool                _alreadyTriggered;

        Task                _cachedDeadlineTask;
        DateTime            _cachedDeadlineAt;
        CancellationToken   _cachedDeadlineCt;

        /// <summary>
        /// Begins the construction of a new set. The set is empty after this call.
        /// </summary>
        public void Begin()
        {
            for (int ndx = 0; ndx < _count; ++ndx)
                _tasks[ndx] = null;

            _count = 0;
            _deadlineAt = null;
            _ct = default;
            _alreadyTriggered = false;
        }

        /// <summary>
        /// Adds CancellationToken into the waited set.
        /// </summary>
        public void AddCancellation(CancellationToken ct)
        {
            if (_alreadyTriggered)
                return;
            if (_ct.IsCancellationRequested)
            {
                _alreadyTriggered = true;
                return;
            }

            if (!_ct.CanBeCanceled)
                _ct = ct;
            else
                _ct = CancellationTokenSource.CreateLinkedTokenSource(_ct, ct).Token;
        }

        /// <summary>
        /// Adds Task into the waited set.
        /// </summary>
        public void AddTask(Task t)
        {
            if (_alreadyTriggered)
                return;
            if (t.IsCompleted)
            {
                _alreadyTriggered = true;
                return;
            }

            AddInternalTask(t);
        }

        /// <summary>
        /// Adds a dedline into the waited set.
        /// </summary>
        public void AddTimeout(DateTime deadline)
        {
            if (_alreadyTriggered)
                return;
            if (DateTime.UtcNow >= deadline)
            {
                _alreadyTriggered = true;
                return;
            }

            if (_deadlineAt == null)
                _deadlineAt = deadline;
            else
                _deadlineAt = new DateTime(System.Math.Min(_deadlineAt.Value.Ticks, deadline.Ticks), DateTimeKind.Utc);
        }

        /// <summary>
        /// Completes when any added Task, CT or timeout trigger. Always completes successfully.
        /// </summary>
        public Task WaitAsync()
        {
            if (_alreadyTriggered)
                return Task.CompletedTask;
            if (_ct.IsCancellationRequested)
                return Task.CompletedTask;
            if (_deadlineAt != null && DateTime.UtcNow >= _deadlineAt)
                return Task.CompletedTask;

            Task deadlineAndOrCt = TryGetDeadlineTask();
            if (deadlineAndOrCt != null)
                AddInternalTask(deadlineAndOrCt);

            // Task.WhenAny will create a temporary array if given IEnumerable. Hence
            // we might as well right-size the buffer here. This save allocs if the next
            // wait has the same amount of Tasks.
            if (_count != _tasks.Length)
                ResizeTasks(_count);

            return Task.WhenAny(_tasks);
        }

        void AddInternalTask(Task t)
        {
            if (_count >= _tasks.Length)
                ResizeTasks(System.Math.Max(_count + 1, _tasks.Length * 2));

            _tasks[_count] = t;
            _count++;
        }

        void ResizeTasks(int count)
        {
            Task[] newTasks = new Task[count];
            for (int ndx = 0; ndx < System.Math.Min(count, _count); ++ndx)
                newTasks[ndx] = _tasks[ndx];
            _tasks = newTasks;
        }

        Task TryGetDeadlineTask()
        {
            if (_deadlineAt != null)
            {
                if (_cachedDeadlineAt != _deadlineAt.Value || _cachedDeadlineCt != _ct || _cachedDeadlineTask == null)
                {
                    TimeSpan delay = _deadlineAt.Value - DateTime.UtcNow;
                    if (delay.Ticks < 0)
                        delay = TimeSpan.Zero;

                    _cachedDeadlineTask = MetaTask.Delay(delay, _ct);
                    _cachedDeadlineAt = _deadlineAt.Value;
                    _cachedDeadlineCt = _ct;
                }
                return _cachedDeadlineTask;
            }
            else if (_ct.CanBeCanceled)
            {
                if (_cachedDeadlineAt != DateTime.MinValue || _cachedDeadlineCt != _ct || _cachedDeadlineTask == null)
                {
                    _cachedDeadlineTask = MetaTask.Delay(-1, _ct);
                    _cachedDeadlineAt = DateTime.MinValue;
                    _cachedDeadlineCt = _ct;
                }
                return _cachedDeadlineTask;
            }
#pragma warning disable VSTHRD114 // Avoid returning a null Task
            return null;
#pragma warning restore VSTHRD114 // Avoid returning a null Task
        }
    }
}
