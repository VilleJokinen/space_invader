// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;

namespace Metaplay.Cloud.Entity
{
    /// <summary>
    /// Pending task that should be executed on actor context after certain point in time.
    ///
    /// See: <see cref="EntityActor.ScheduleExecuteOnActorContext"/>.
    /// </summary>
    struct PendingScheduledActorTask
    {
        /// <summary>
        /// Ordering primarily by time, but if the time is same use the order
        /// the requests were issued.
        /// </summary>
        public struct ExecuteTime : IComparable<ExecuteTime>
        {
            public long ExecuteOnTick   { get; }
            public int  RunningId       { get; }

            public ExecuteTime(long executeOnTick, int runningId)
            {
                ExecuteOnTick = executeOnTick;
                RunningId = runningId;
            }

            int IComparable<ExecuteTime>.CompareTo(ExecuteTime other)
            {
                if (ExecuteOnTick < other.ExecuteOnTick)
                    return -1;
                if (ExecuteOnTick > other.ExecuteOnTick)
                    return +1;
                if (RunningId < other.RunningId)
                    return -1;
                if (RunningId > other.RunningId)
                    return +1;
                return 0;
            }
        }

        public Func<Task>           ExecuteOpAsync      { get; }
        public CancellationToken    Ct                  { get; }

        public PendingScheduledActorTask(Func<Task> executeOpAsync, CancellationToken ct)
        {
            ExecuteOpAsync = executeOpAsync;
            Ct = ct;
        }
    }

    /// <summary>
    /// Command for informing an actor that it should execute scheduled actions on actor context.
    /// See: <see cref="EntityActor.ScheduleExecuteOnActorContext"/>.
    /// </summary>
    sealed class ExecuteScheduledTasks
    {
        public long ScheduledForTick { get; }

        public ExecuteScheduledTasks(long scheduledForTick)
        {
            ScheduledForTick = scheduledForTick;
        }
    }

    public partial class EntityActor
    {
        PriorityQueue<PendingScheduledActorTask, PendingScheduledActorTask.ExecuteTime> _scheduledTasks                         = null; // if null, then actor is shut down
        object                                                                          _scheduledTasksLock                     = null;
        int                                                                             _nextScheduledTaskId                    = 0;
        HashSet<long>                                                                   _scheduledTasksScheduledCommandsOnTick  = new HashSet<long>(); // ticks for which a message has been scheduled via Akka Scheduler.

        void InitializeScheduleExecuteOnActorContext()
        {

            _scheduledTasks = new PriorityQueue<PendingScheduledActorTask, PendingScheduledActorTask.ExecuteTime>();
            _scheduledTasksLock = new object();
        }

        void CancelAllScheduledExecuteOnActorContextTasksForEntityShutdown()
        {
            lock (_scheduledTasksLock)
            {
                _scheduledTasks = null;
            }
        }

        /// <summary>
        /// Schedules the execution for <paramref name="foregroundOp"/> on actor's execute context after <paramref name="delay"/> has passed.
        /// The action is not executed if <paramref name="ct"/> is triggered before the execution starts. If execution has already started,
        /// cancellation has no effect. Similarly, if the entity shuts down before execution has started, the operation is not executed.
        /// </summary>
        protected void ScheduleExecuteOnActorContext(TimeSpan delay, Action foregroundOp, CancellationToken ct = default)
        {
            DoScheduleExecuteOnActorContext(DateTime.UtcNow + delay, () =>
                {
                    foregroundOp();
                    return Task.CompletedTask;
                }, ct);
        }

        /// <inheritdoc cref="ScheduleExecuteOnActorContext(TimeSpan, Action, CancellationToken)"/>
        protected void ScheduleExecuteOnActorContext(TimeSpan delay, Func<Task> foregroundOp, CancellationToken ct = default)
        {
            DoScheduleExecuteOnActorContext(DateTime.UtcNow + delay, foregroundOp, ct);
        }

        /// <summary>
        /// Schedules the execution for <paramref name="foregroundOp"/> on actor's execute context after <paramref name="time"/>.
        /// The action is not executed if <paramref name="ct"/> is triggered before the execution starts. If execution has already started,
        /// cancellation has no effect. Similarly, if the entity shuts down before execucion has started, the operation is not executed.
        /// </summary>
        protected void ScheduleExecuteOnActorContext(DateTime utcTime, Action foregroundOp, CancellationToken ct = default)
        {
            if (utcTime.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"Time must be UTC time. Got {utcTime.Kind}.", nameof(utcTime));

            DoScheduleExecuteOnActorContext(utcTime, () =>
                {
                    foregroundOp();
                    return Task.CompletedTask;
                }, ct);
        }

        /// <inheritdoc cref="ScheduleExecuteOnActorContext(DateTime, Action, CancellationToken)"/>
        protected void ScheduleExecuteOnActorContext(DateTime utcTime, Func<Task> foregroundOp, CancellationToken ct = default)
        {
            if (utcTime.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"Time must be UTC time. Got {utcTime.Kind}.", nameof(utcTime));

            DoScheduleExecuteOnActorContext(utcTime, foregroundOp, ct);
        }

        void DoScheduleExecuteOnActorContext(DateTime timeUtc, Func<Task> foregroundOp, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return;

            // Put task on the priority queue, ordered by desired time with insert order working as the tiebreaker.
            PendingScheduledActorTask scheduledExecution = new PendingScheduledActorTask(foregroundOp, ct);
            PendingScheduledActorTask.ExecuteTime atTime = new PendingScheduledActorTask.ExecuteTime(timeUtc.Ticks, _nextScheduledTaskId++);
            lock(_scheduledTasksLock)
            {
                _scheduledTasks.Enqueue(scheduledExecution, atTime);
            }

            ScheduleNextExecuteScheduledTasksCommand();
        }

        /// <summary>
        /// Schedules the ExecuteScheduledTasks command for the next (earliest) pending scheduled tasks. Does
        /// nothing if there are no pending tasks, or if the command is already scheduled.
        /// </summary>
        void ScheduleNextExecuteScheduledTasksCommand()
        {
            long nextTick;
            lock(_scheduledTasksLock)
            {
                // Actor is shut down, no need to do anything?
                if (_scheduledTasks == null)
                    return;

                // No scheduled tasks?
                if (!_scheduledTasks.TryPeek(out _, out PendingScheduledActorTask.ExecuteTime executeTime))
                    return;

                // We don't need to schedule a command if there is already message scheduled for that very same tick.
                // Otherwise, mark the command for the tick as scheduled.
                if (!_scheduledTasksScheduledCommandsOnTick.Add(executeTime.ExecuteOnTick))
                    return;

                nextTick = executeTime.ExecuteOnTick;
            }

            // Schedule the execution
            TimeSpan delay = new DateTime(ticks: nextTick, kind: DateTimeKind.Utc) - DateTime.UtcNow;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;
            Context.System.Scheduler.ScheduleTellOnce(delay, _self, new ExecuteScheduledTasks(nextTick), null, _cancelTimers);
        }

        [CommandHandler]
        async Task HandleExecuteScheduledTasks(ExecuteScheduledTasks command)
        {
            // Choose the work first to avoid starvation if more work is scheduled from handlers.
            // \note: We process the tasks up to the current time instead of command.ScheduledForTick. Using the current
            //        time avoids running scheduled tasks too early if Akka delivers the scheduled message too early
            //        which it will if the system is shutting down. This means that this command might execute more
            //        tasks than it was initially scheduled for but that is harmless.
            List<PendingScheduledActorTask> tasksToExecute = new List<PendingScheduledActorTask>();
            long processUpToTick = DateTime.UtcNow.Ticks;

            lock (_scheduledTasksLock)
            {
                // Mark this command as completed.
                _scheduledTasksScheduledCommandsOnTick.Remove(command.ScheduledForTick);

                // Choose and remove all ready-to-be-executed work from queue
                for (;;)
                {
                    if (_scheduledTasks == null)
                        break;
                    if (!_scheduledTasks.TryPeek(out PendingScheduledActorTask pendingScheduled, out PendingScheduledActorTask.ExecuteTime executeTime))
                        break;
                    if (executeTime.ExecuteOnTick > processUpToTick)
                        break;

                    // Found task to run. Move into work queue.
                    tasksToExecute.Add(pendingScheduled);
                    _ = _scheduledTasks.Dequeue();
                }
            }

            // Execute work
            foreach (PendingScheduledActorTask task in tasksToExecute)
            {
                if (task.Ct.IsCancellationRequested)
                    continue;

                await task.ExecuteOpAsync();
            }

            // Schedule command for the next scheduled task
            ScheduleNextExecuteScheduledTasksCommand();
        }
    }
}
