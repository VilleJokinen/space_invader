// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.AdminApiActor;

namespace Metaplay.Server
{
    [MetaSerializable]
    public interface IBackgroundTaskOutput { }

    [MetaSerializable]
    public abstract class BackgroundTask
    {
        public abstract Task<IBackgroundTaskOutput> Run(BackgroundTaskContext Context);
    }

    [MetaSerializable]
    public struct BackgroundTaskStatus
    {
        [MetaMember(1)] public MetaGuid Id { get; set; }
        [MetaMember(2)] public BackgroundTask Task { get; set; }
        [MetaMember(3)] public bool Completed { get; set; }
        [MetaMember(4)] public string Failure { get; set; }
        [MetaMember(5)] public IBackgroundTaskOutput Output { get; set; }
    }

    [MetaMessage(MessageCodesCore.StartBackgroundTaskRequest, MessageDirection.ServerInternal)]
    public class StartBackgroundTaskRequest : MetaMessage
    {
        public MetaGuid Id { get; private set; }
        public BackgroundTask Task { get; private set; }

        public StartBackgroundTaskRequest() { }
        public StartBackgroundTaskRequest(MetaGuid id, BackgroundTask task)
        {
            Id = id;
            Task = task;
        }
    }

    [MetaMessage(MessageCodesCore.StartBackgroundTaskResponse, MessageDirection.ServerInternal)]
    public class StartBackgroundTaskResponse : MetaMessage
    {
        public MetaGuid Id { get; private set; }

        public StartBackgroundTaskResponse() { }
        public StartBackgroundTaskResponse(MetaGuid id)
        {
            Id = id;
        }
    }

    [MetaMessage(MessageCodesCore.ForgetBackgroundTaskRequest, MessageDirection.ServerInternal)]
    public class ForgetBackgroundTaskRequest : MetaMessage
    {
        public MetaGuid Id { get; private set; }

        public ForgetBackgroundTaskRequest() { }
        public ForgetBackgroundTaskRequest(MetaGuid id)
        {
            Id = id;
        }
    }

    [MetaMessage(MessageCodesCore.BackgroundTaskStatusRequest, MessageDirection.ServerInternal)]
    public class BackgroundTaskStatusRequest : MetaMessage
    {
        public string ClassName { get; private set; }

        public BackgroundTaskStatusRequest() { }
        public BackgroundTaskStatusRequest(string className)
        {
            ClassName = className;
        }
    }

    [MetaMessage(MessageCodesCore.BackgroundTaskStatusResponse, MessageDirection.ServerInternal)]
    public class BackgroundTaskStatusResponse : MetaMessage
    {
        public List<BackgroundTaskStatus> Tasks;

        public BackgroundTaskStatusResponse() { }

        public BackgroundTaskStatusResponse(List<BackgroundTaskStatus> tasks)
        {
            Tasks = tasks;
        }
    }

    [MetaMessage(MessageCodesCore.BackgroundTaskProgressUpdate, MessageDirection.ServerInternal)]
    public class BackgroundTaskProgressUpdate : MetaMessage
    {
        public MetaGuid Id { get; private set; }
        public IBackgroundTaskOutput Progress { get; private set; }

        public BackgroundTaskProgressUpdate() { }

        public BackgroundTaskProgressUpdate(MetaGuid id, IBackgroundTaskOutput progress)
        {
            Id = id;
            Progress = progress;
        }
    }

    public struct BackgroundTaskContext
    {
        IActorRef _actor;
        MetaGuid _id;

        public BackgroundTaskContext(IActorRef actor, MetaGuid id)
        {
            _actor = actor;
            _id = id;
        }

        public void UpdateTaskOutput(IBackgroundTaskOutput output)
        {
            _actor.Tell(new BackgroundTaskProgressUpdate(_id, output));
        }

        public Task<TResult> AskEntityAsync<TResult>(EntityId entityId, MetaMessage message) where TResult : MetaMessage
        {
            return ForwardAskToEntity.ExecuteAsync<TResult>(_actor, entityId, message);
        }
    }

    [EntityConfig]
    internal sealed class BackgroundTaskConfig : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.BackgroundTask;
        public override Type                EntityActorType         => typeof(BackgroundTaskActor);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(numServiceShards: 1);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(30);
    }

    public class BackgroundTaskActor : EphemeralEntityActor
    {
        struct TaskState
        {
            public static TaskState New(BackgroundTask task, bool autoCleanUp = false)
            {
                return new TaskState() { Task = task, StartedAt = MetaTime.Now, AutoCleanup = autoCleanUp };
            }

            public static TaskState Failure(TaskState s, Exception e)
            {
                s.Completed = true;
                s.Exception = e;
                s.CompletedAt = MetaTime.Now;
                return s;
            }

            public static TaskState Success(TaskState s, IBackgroundTaskOutput result)
            {
                s.Completed = true;
                s.Output = result;
                s.CompletedAt = MetaTime.Now;
                return s;
            }

            public static TaskState WithOutput(TaskState s, IBackgroundTaskOutput output)
            {
                s.Output = output;
                return s;
            }

            public static TaskState WithAutoCleanup(TaskState s)
            {
                s.AutoCleanup = true;
                return s;
            }

            public BackgroundTask Task { get; private set; }
            public bool Completed { get; private set; }
            public Exception Exception { get; private set; }
            public IBackgroundTaskOutput Output { get; private set; }
            public MetaTime StartedAt { get; private set; }
            public MetaTime CompletedAt { get; private set; }
            public bool AutoCleanup { get; private set; }
            public string TaskClassName => Task.GetType().Name;
        }

        public static readonly EntityId EntityId = EntityId.Create(EntityKindCloudCore.BackgroundTask, 0);
        readonly RandomPCG _random = RandomPCG.CreateNew();
        Dictionary<MetaGuid, TaskState> _taskStates = new Dictionary<MetaGuid, TaskState>();

        public BackgroundTaskActor(EntityId entityId) : base(entityId)
        {
        }

        protected override Task Initialize()
        {
            StartPeriodicTimer(TimeSpan.FromSeconds(1), ActorTick.Instance);
            return Task.CompletedTask;
        }

        [CommandHandler]
        private void HandleActorTick(ActorTick _)
        {
            CleanupCompletedTasks();
        }

        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        protected override void RegisterHandlers()
        {
            Receive<ForwardAskToEntity>(ReceiveForwardAskToEntity);
            Receive<BackgroundTaskProgressUpdate>(ReceiveProgressUpdate);
            base.RegisterHandlers();
        }

        bool GetRunningTaskState(MetaGuid id, out TaskState oldState)
        {
            if (!_taskStates.TryGetValue(id, out oldState))
            {
                _log.Error($"Unknown task id {id} completed");
                return false;
            }
            if (oldState.Completed)
            {
                _log.Error($"Task id {id} already completed");
                return false;
            }
            return true;
        }

        void CompleteTask(MetaGuid id, TaskState state)
        {
            if (state.Exception != null)
            {
                _log.Error($"Background task id {id} failed: {state.Exception}");
            }
            else
            {
                _log.Debug($"Background task id {id} completed");
            }
            if (state.AutoCleanup)
                _taskStates.Remove(id);
            else
                _taskStates[id] = state;
        }

        void CleanupCompletedTasks()
        {
            // \TODO implement
        }

        [EntityAskHandler]
        public StartBackgroundTaskResponse HandleStartBackgroundTask(StartBackgroundTaskRequest taskProps)
        {
            string taskClass = taskProps.Task.GetType().Name;
            MetaGuid id = taskProps.Id.IsValid ? taskProps.Id : MetaGuid.New();

            _log.Debug($"Starting background task {taskClass} id {id}");

            BackgroundTaskContext taskContext = new BackgroundTaskContext(_self, id);
            _taskStates[id] = TaskState.New(taskProps.Task);
            try
            {
                ContinueTaskOnActorContext(
                    Task.Run(() => taskProps.Task.Run(taskContext)),
                    result =>
                    {
                        if (GetRunningTaskState(id, out TaskState state))
                            CompleteTask(id, TaskState.Success(state, result));
                    },
                    exception =>
                    {
                        if (GetRunningTaskState(id, out TaskState state))
                            CompleteTask(id, TaskState.Failure(state, exception));
                    }
                );
            }
            catch (Exception)
            {
                _taskStates.Remove(id);
                throw;
            }
            return new StartBackgroundTaskResponse(id);
        }

        [EntityAskHandler]
        public BackgroundTaskStatusResponse HandleBackgroundTaskStatusRequest(BackgroundTaskStatusRequest statusRequest)
        {
            return new BackgroundTaskStatusResponse(_taskStates
                .Where(x => x.Value.TaskClassName == statusRequest.ClassName)
                .Select(x => new BackgroundTaskStatus()
                {
                    Id = x.Key,
                    Task = x.Value.Task,
                    Completed = x.Value.Completed,
                    Failure = TryGetExceptionStringForTaskStatus(x.Value.Exception),
                    Output = x.Value.Output
                }).ToList());
        }

        static string TryGetExceptionStringForTaskStatus(Exception exception)
        {
            if (exception == null)
                return null;

            return exception.ToString();
        }

        [EntityAskHandler]
        // TODO: return value type is a bit silly here
        public StartBackgroundTaskResponse HandleForgetBackgroundTaskRequest(ForgetBackgroundTaskRequest req)
        {
            if (!_taskStates.TryGetValue(req.Id, out TaskState state))
            {
                throw new InvalidEntityAsk($"Unknown task id {req.Id}");
            }
            else
            {
                // TODO: implement cancel if desired
                if (state.Completed)
                {
                    _taskStates.Remove(req.Id);
                }
                else if (!state.AutoCleanup)
                {
                    _taskStates[req.Id] = TaskState.WithAutoCleanup(state);
                }
            }
            return new StartBackgroundTaskResponse(req.Id);
        }

        void ReceiveProgressUpdate(BackgroundTaskProgressUpdate request)
        {
            if (GetRunningTaskState(request.Id, out TaskState state))
            {
                _taskStates[request.Id] = TaskState.WithOutput(state, request.Progress);
            }
        }

        void ReceiveForwardAskToEntity(ForwardAskToEntity request)
        {
            request.HandleReceive(Sender, _log, this);
        }
    }
}
