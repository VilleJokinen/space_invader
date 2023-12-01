// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity.Synchronize;
using Metaplay.Core;
using Metaplay.Core.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Cloud.Entity
{
    /// <summary>
    /// Mark the method as a handler for an Akka.NET command (i.e., a message sent with Akka's <see cref="Akka.Actor.ICanTell.Tell(object, Akka.Actor.IActorRef)"/>).
    /// </summary>
    /// <remarks>
    /// Handler must take exactly one parameter: the message. It must return void or a <see cref="Task"/> (ie, be an async method).
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class CommandHandlerAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark a method as a handler for a <see cref="MetaMessage"/> sent with <see cref="EntityActor.CastMessage(EntityId, MetaMessage)"/>,
    /// <see cref="EntityActor.SendMessage(EntitySubscriber, MetaMessage)"/>, or <see cref="EntityActor.PublishMessage(EntityTopic, MetaMessage)"/>.
    /// </summary>
    /// <remarks>
    /// Handler must take exactly one or two parameters: an optional <see cref="EntityId"/> for the source and the message (derived from <see cref="MetaMessage"/>.
    /// It must return void or a <see cref="Task"/> (ie, be an async method).
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class MessageHandlerAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark a method as a handler for an incoming <see cref="EntityAsk"/> message.
    /// </summary>
    /// <remarks>
    /// Returning the result for the EntityAsk may be handled implicitly or explicitly:<br/>
    /// In implicit mode, the Handler takes the EntityId of the sender entity and the message (derived from <see cref="MetaMessage"/>) and return the result for the
    /// EntityAsk. The sender Entity Id is optional. Return type must be MetaMessage (or a type derived from it) or a <see cref="Task{TResult}"/> for
    /// <c>TResult : MetaMessage</c> (or a type derived from it).<br/>
    /// In explict mode, the Handler must take exactly two parameters: an <see cref="EntityAsk"/> object and the message (derived from <see cref="MetaMessage"/>.
    /// It must return void or a <see cref="Task"/> (ie, be an async method). To return an value, Handler must call <see cref="EntityActor.ReplyToAsk(EntityAsk, MetaMessage)"/>
    /// exactly once with the provided ask object.
    /// <para>
    /// Legal signatures: Braces <c>[]</c> mark the optional parts.
    /// </para>
    /// <list type="table">
    /// <item><description><c>ResponseMessageType HandleMessage([EntityId senderEntity,] RequestMessageType request) { return message; }</c> // Implicit, Synchronous</description></item>
    /// <item><description><c>async Task&lt;ResponseMessageType&gt; HandleMessage([EntityId senderEntity,] RequestMessageType request) { return message; }</c> // Implicit, Asynchronous</description></item>
    /// <item><description><c>void HandleMessage(EntityAsk ask, RequestMessageType request) { ReplyToAsk(ask, message); }</c> // Explicit, Synchronous</description></item>
    /// <item><description><c>Task HandleMessage(EntityAsk ask, RequestMessageType request) { ReplyToAsk(ask, message); }</c> // Explicit, Asynchronous</description></item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class EntityAskHandlerAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark a method as a handler for an incoming <see cref="EntitySynchronize"/> message.
    /// </summary>
    /// <remarks>
    /// Handler must take exactly two parameters: an <see cref="EntitySynchronize"/> object and the message (derived from <see cref="MetaMessage"/>.
    /// It must return void or a <see cref="Task"/> (ie, be an async method).
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class EntitySynchronizeHandlerAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark a method as a handler for an incoming pubsub message.
    /// </summary>
    /// <remarks>
    /// Handler must take exactly two parameters: an <see cref="EntitySubscriber"/> or an <see cref="EntitySubscription"/>, and the message (derived from <see cref="MetaMessage"/>.
    /// It must return void or a <see cref="Task"/> (ie, be an async method).
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class PubSubMessageHandlerAttribute : Attribute
    {
    }

    public abstract class EntityDispatcherBase<TReceiverType> where TReceiverType : IEntityReceiver
    {
        public delegate Task DispatchMessage(TReceiverType actor, EntityId fromEntityId, MetaMessage message);
        public delegate Task DispatchCommand(TReceiverType actor, object command);
        public delegate Task DispatchEntityAsk(TReceiverType actor, EntityAsk ask, MetaMessage message);
        public delegate Task DispatchEntitySynchronize(TReceiverType actor, EntitySynchronize sync, MetaMessage message);
        public delegate Task DispatchSubscriptionMessage(TReceiverType actor, EntitySubscription subscription, MetaMessage message);
        public delegate Task DispatchSubscriberMessage(TReceiverType actor, EntitySubscriber subscriber, MetaMessage message);

        public Dictionary<Type, DispatchMessage> _messageDispatchers;
        public Dictionary<Type, DispatchCommand> _commandDispatchers;
        public Dictionary<Type, DispatchEntityAsk> _entityAskDispatchers;
        public Dictionary<Type, DispatchEntitySynchronize> _entitySyncDispatchers;
        public Dictionary<Type, DispatchSubscriptionMessage> _pubsubSubscriptionDispatchers;
        public Dictionary<Type, DispatchSubscriberMessage> _pubsubSubscriberDispatchers;

        public EntityDispatcherBase()
        {
            _messageDispatchers = new Dictionary<Type, DispatchMessage>();
            _commandDispatchers = new Dictionary<Type, DispatchCommand>();
            _entityAskDispatchers = new Dictionary<Type, DispatchEntityAsk>();
            _entitySyncDispatchers = new Dictionary<Type, DispatchEntitySynchronize>();
            _pubsubSubscriptionDispatchers = new Dictionary<Type, DispatchSubscriptionMessage>();
            _pubsubSubscriberDispatchers = new Dictionary<Type, DispatchSubscriberMessage>();
        }

        public bool TryGetMessageDispatchFunc(Type msgType, out DispatchMessage dispatchFunc) => _messageDispatchers.TryGetValue(msgType, out dispatchFunc);
        public bool TryGetCommandDispatchFunc(Type cmdType, out DispatchCommand dispatchFunc) => _commandDispatchers.TryGetValue(cmdType, out dispatchFunc);
        public bool TryGetEntityAskDispatchFunc(Type msgType, out DispatchEntityAsk dispatchFunc) => _entityAskDispatchers.TryGetValue(msgType, out dispatchFunc);
        public bool TryGetEntitySynchronizeDispatchFunc(Type msgType, out DispatchEntitySynchronize dispatchFunc) => _entitySyncDispatchers.TryGetValue(msgType, out dispatchFunc);
        public bool TryGetPubSubSubscriptionDispatchFunc(Type msgType, out DispatchSubscriptionMessage dispatchFunc) => _pubsubSubscriptionDispatchers.TryGetValue(msgType, out dispatchFunc);
        public bool TryGetPubSubSubscriberDispatchFunc(Type msgType, out DispatchSubscriberMessage dispatchFunc) => _pubsubSubscriberDispatchers.TryGetValue(msgType, out dispatchFunc);
    }

    public class EntityDispatcher : EntityDispatcherBase<EntityActor> { }

    public class EntityComponentDispatcher : EntityDispatcherBase<EntityComponent> { }


    public static class EntityDispatcherBuilder<TReceiverType> where TReceiverType : IEntityReceiver
    {
        static MethodInfo _getCompletedTask = typeof(Task).GetProperty("CompletedTask").GetGetMethod();

        public static void Build(EntityDispatcherBase<TReceiverType> dispatcher, Type receiverType)
        {
            MetaDebug.Assert(receiverType.IsSubclassOf(typeof(TReceiverType)), "Type {0} must be subclass of {1} to be used with EntityDispatcher", receiverType, typeof(TReceiverType));

            for (Type type = receiverType; type != null; type = type.BaseType)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    // Make sure that the old naming style isn't accidentally used.
                    if (method.Name == "HandleMessage")
                        throw new InvalidOperationException($"Legacy naming convention used in {type.Name}.{method.Name}(): rename method and use [MessageHandler] attribute instead!");
                    else if (method.Name == "HandleCommand")
                        throw new InvalidOperationException($"Legacy naming convention used in {type.Name}.{method.Name}(): rename method and use [CommandHandler] attribute instead!");
                    else if (method.Name == "HandleEntityAsk")
                        throw new InvalidOperationException($"Legacy naming convention used in {type.Name}.{method.Name}(): rename method and use [EntityAskHandler] attribute instead!");
                    else if (method.Name == "HandleEntitySynchronize")
                        throw new InvalidOperationException($"Legacy naming convention used in {type.Name}.{method.Name}(): rename method and use [EntitySynchronizeHandler] attribute instead!");
                    else if (method.Name == "HandlePubSubMessage") // \note: intentionally use a greppable name
                        throw new InvalidOperationException($"Legacy naming convention used in {type.Name}.{method.Name}(): rename method and use [PubSubMessageHandler] attribute instead!");

                    // Grab all known handler attributes
                    MessageHandlerAttribute             messageHandleAttrib     = method.GetCustomAttribute<MessageHandlerAttribute>();
                    CommandHandlerAttribute             commandHandleAttrib     = method.GetCustomAttribute<CommandHandlerAttribute>();
                    EntityAskHandlerAttribute           askHandlerAttrib        = method.GetCustomAttribute<EntityAskHandlerAttribute>();
                    EntitySynchronizeHandlerAttribute   synchronizeHandleAttrib = method.GetCustomAttribute<EntitySynchronizeHandlerAttribute>();
                    PubSubMessageHandlerAttribute       pubSubHandlerAttrib     = method.GetCustomAttribute<PubSubMessageHandlerAttribute>();

                    // Check that multiple attributes are not used
                    int attribCount = (messageHandleAttrib != null ? 1 : 0)
                                    + (commandHandleAttrib != null ? 1 : 0)
                                    + (askHandlerAttrib != null ? 1 : 0)
                                    + (synchronizeHandleAttrib != null ? 1 : 0)
                                    + (pubSubHandlerAttrib != null ? 1 : 0);
                    if (attribCount > 1)
                        throw new InvalidOperationException($"Method {type.Name}.{method.Name}() declared multiple EntityDispatcher attributes -- only one is allowed!");

                    // Register dispatcher based on attribute
                    if (messageHandleAttrib != null)
                        RegisterDispatchMessageFunc(dispatcher, type, method);
                    else if (commandHandleAttrib != null)
                        RegisterDispatchCommandFunc(dispatcher, type, method);
                    else if (askHandlerAttrib != null)
                        RegisterDispatchAskFunc(dispatcher, receiverType, method);
                    else if (synchronizeHandleAttrib != null)
                        RegisterDispatchSynchronizeFunc(dispatcher, receiverType, method);
                    else if (pubSubHandlerAttrib != null)
                        RegisterDispatchPubSubFunc(dispatcher, type, method);
                }
            }
        }

        static void RegisterDispatchMessageFunc(EntityDispatcherBase<TReceiverType> dispatcher, Type receiverType, MethodInfo method)
        {
            ParameterInfo[] args = method.GetParameters();
            bool    hasEntityIdArg  = args.Length == 2;
            int     msgArgNdx       = args.Length - 1;
            if (args.Length == 2)
                MetaDebug.Assert(args[0].ParameterType == typeof(EntityId), "[MessageHandler] method {0}.{1}() first parameter must be EntityId, got {2}", receiverType.Name, method.Name, args[0].ParameterType.Name);
            else if (args.Length != 1)
                MetaDebug.AssertFail("[MessageHandler] method {0}.{1}({2}) must take exactly one or two parameters, got {3}", receiverType.Name, method.Name, string.Join(',', args.Select(paramInfo => paramInfo.ParameterType.Name)), args.Length);
            MetaDebug.Assert(args[msgArgNdx].ParameterType.IsSubclassOf(typeof(MetaMessage)), "[MessageHandler] method {0}.{1}() last parameter must be derived from MetaMessage, got {2}", receiverType.Name, method.Name, args[msgArgNdx].ParameterType.Name);

            Type retType = method.ReturnType;
            bool isAsync;
            if (retType == typeof(Task))
                isAsync = true;
            else if (retType == typeof(void))
                isAsync = false;
            else
                throw new InvalidOperationException($"[MessageHandler] method {receiverType.Name}.{method.Name}() return type must be Task or void, got {retType.ToGenericTypeString()}");

            Type msgType = args[msgArgNdx].ParameterType;
            //DebugLog.Info("Found {2} {0}.HandleMessage<{1}>()", actorType.Name, msgType.Name, isAsync ? "async" : "sync");

            if (!msgType.IsMetaFeatureEnabled())
                return;

            MetaMessageSpec msgTypeSpec = MetaMessageRepository.Instance.GetFromType(msgType);
            DynamicMethod   wrapper     = new DynamicMethod($"{receiverType.Name}.DispatchMessage<{msgTypeSpec.Name}>", typeof(Task), new Type[] { typeof(TReceiverType), typeof(EntityId), typeof(MetaMessage) }, receiverType);
            wrapper.DefineParameter(1, ParameterAttributes.None, "receiver");
            wrapper.DefineParameter(2, ParameterAttributes.None, "fromEntityId");
            wrapper.DefineParameter(3, ParameterAttributes.None, "message");

            ILGenerator il = wrapper.GetILGenerator();

            // Call receiver.HandleMessage([fromEntityId], (MessageType)message);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, receiverType);
            if (hasEntityIdArg)
                il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Castclass, msgType);
            il.Emit(OpCodes.Call, method);

            // If not async, load Task.CompletedTask as return value
            if (!isAsync)
                il.Emit(OpCodes.Call, _getCompletedTask);

            // Return Task
            il.Emit(OpCodes.Ret);

            // Register dispatch function
            if (dispatcher._messageDispatchers.ContainsKey(msgType))
                throw new InvalidOperationException($"Duplicate method with [MessageHandler] for type {msgType.ToGenericTypeString()} in {receiverType.ToGenericTypeString()}");
            dispatcher._messageDispatchers.Add(msgType, (EntityDispatcherBase<TReceiverType>.DispatchMessage)wrapper.CreateDelegate(typeof(EntityDispatcherBase<TReceiverType>.DispatchMessage)));
        }

        static void RegisterDispatchCommandFunc(EntityDispatcherBase<TReceiverType> dispatcher, Type actorType, MethodInfo method)
        {
            ParameterInfo[] args = method.GetParameters();
            MetaDebug.Assert(args.Length == 1, "[CommandHandler] method {0}.{1}({2}) must take exactly one parameter", actorType.Name, method.Name, string.Join(',', args.Select(paramInfo => paramInfo.ParameterType.Name)));

            Type retType = method.ReturnType;
            bool isAsync;
            if (retType == typeof(Task))
                isAsync = true;
            else if (retType == typeof(void))
                isAsync = false;
            else
                throw new InvalidOperationException($"[CommandHandler] method {actorType.Name}.{method.Name}() return type must be Task or void, got {retType.ToGenericTypeString()}");

            Type cmdType = args[0].ParameterType;
            //DebugLog.Info("Found {0}: {1} {2}.{3}({4})", actorType.ToGenericTypeString(), isAsync ? "async" : "sync", type.Name, method.Name, cmdType.ToGenericTypeString());

            if (!cmdType.IsMetaFeatureEnabled())
                return;

            // Generate the dispatch function using ILGenerator
            DynamicMethod wrapper = new DynamicMethod($"{actorType.Name}.DispatchCommand<{cmdType.ToGenericTypeString()}>", typeof(Task), new Type[] { typeof(TReceiverType), typeof(object) }, actorType);
            wrapper.DefineParameter(1, ParameterAttributes.None, "actor");
            wrapper.DefineParameter(2, ParameterAttributes.None, "command");

            ILGenerator il = wrapper.GetILGenerator();

            // Call actor.HandleCommand((CommandType)command);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, actorType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, cmdType);
            il.Emit(OpCodes.Call, method);

            // If not async, load Task.CompletedTask as return value
            if (!isAsync)
                il.Emit(OpCodes.Call, _getCompletedTask);

            // Return Task
            il.Emit(OpCodes.Ret);

            // Register dispatch function
            if (dispatcher._commandDispatchers.ContainsKey(cmdType))
                throw new InvalidOperationException($"Duplicate method with [CommandHandler] for type {cmdType.ToGenericTypeString()} in {actorType.ToGenericTypeString()}");
            dispatcher._commandDispatchers.Add(cmdType, (EntityDispatcherBase<TReceiverType>.DispatchCommand)wrapper.CreateDelegate(typeof(EntityDispatcherBase<TReceiverType>.DispatchCommand)));
        }

        /// <summary>
        /// Pseudo-lambda class for invoking ReplyToAsk() with the answer from a [EntityAskHandler] for async methods returning a value.
        /// </summary>
        class AsyncReplyWrapper
        {
            IEntityReceiver _receiver;
            EntityAsk _ask;

            public AsyncReplyWrapper(IEntityReceiver receiver, EntityAsk ask)
            {
                _receiver = receiver;
                _ask = ask;
            }

            // \note Must be called by passing method into ContinueWith()
            public void Execute(Task<MetaMessage> task)
            {
                // Unwrap AggregateException to get original exception. This gives us consistent exception types for both sync and async handlers.
                // Use ExceptionDispatchInfo to avoid rewriting the exception stack trace.
                if (task.Status == TaskStatus.Faulted)
                {
                    Exception rethrownEx;
                    if (task.Exception.InnerExceptions.Count == 1)
                        rethrownEx = task.Exception.InnerException;
                    else
                        rethrownEx = task.Exception;

                    ExceptionDispatchInfo.Throw(rethrownEx);
                    // unreachable
                }
                else
                    _receiver.ReplyToAsk(_ask, task.GetCompletedResult());
            }
        }

        static void RegisterDispatchAskFunc(EntityDispatcherBase<TReceiverType> dispatcher, Type receiverType, MethodInfo method)
        {
            Type retType = method.ReturnType;
            bool isRetGenericTask = retType.IsGenericTypeOf(typeof(Task<>));
            bool isAsync = (retType == typeof(Task)) || isRetGenericTask;
            bool hasReturnValue = (retType != typeof(void)) && (retType != typeof(Task));
            Type returnValueType = hasReturnValue ? (isAsync ? retType.GetGenericArguments()[0] : retType) : null;
            ParameterInfo[] args = method.GetParameters();

            if (hasReturnValue)
                MetaDebug.Assert(returnValueType.IsDerivedFrom<MetaMessage>(), "[EntityAskHandler] method {0}.{1}({2}) must return a MetaMessage or a descendant.", receiverType.Name, method.Name, string.Join(',', args.Select(paramInfo => paramInfo.ParameterType.Name)));

            // Detect overload
            Type msgType;
            bool hasAskArg;
            bool hasSourceEntityIdArg;
            if (hasReturnValue && args.Length == 1)
            {
                // ResponseType HandleMessage(RequestType message)
                msgType = args[0].ParameterType;
                hasAskArg = false;
                hasSourceEntityIdArg = false;
            }
            else if (hasReturnValue && args.Length == 2)
            {
                // ResponseType HandleMessage(EntityId sourceId, RequestType message)
                MetaDebug.Assert(args[0].ParameterType == typeof(EntityId), "[EntityAskHandler] method {0}.{1}(): first parameter must be EntityId. See EntityAskHandler doc-comment for legal signatures.", receiverType.Name, method.Name);
                msgType = args[1].ParameterType;
                hasAskArg = false;
                hasSourceEntityIdArg = true;
            }
            else if (!hasReturnValue && args.Length == 2)
            {
                // void HandleMessage(EntityAsk ask, RequestType message)
                MetaDebug.Assert(args[0].ParameterType == typeof(EntityAsk), "[EntityAskHandler] method {0}.{1}(): first parameter must be EntityAsk. See EntityAskHandler doc-comment for legal signatures.", receiverType.Name, method.Name);
                msgType = args[1].ParameterType;
                hasAskArg = true;
                hasSourceEntityIdArg = false;
            }
            else
            {
                MetaDebug.AssertFail("[EntityAskHandler] method {0}.{1}({2}) has unrecognized signature. See EntityAskHandler doc-comment for legal signatures.", receiverType.Name, method.Name, string.Join(',', args.Select(paramInfo => paramInfo.ParameterType.Name)));
                return; // unreachable
            }

            MetaDebug.Assert(msgType.IsSubclassOf(typeof(MetaMessage)), "[EntityAskHandler] method {0}.{1}(): last parameter must be derived from MetaMessage. See EntityAskHandler doc-comment for legal signatures.", receiverType.Name, method.Name);

            if (!msgType.IsMetaFeatureEnabled())
                return;

            //DebugLog.Info("Found [EntityAskHandler] {AsyncPrefix} {ReturnValue} {ActorType}.{MethodName}({MessageType})", isAsync ? "async" : "sync", retType.ToGenericTypeString(), actorType.Name, method.Name, msgType.Name);

            // Generate the dispatch function using ILGenerator
            DynamicMethod wrapper = new DynamicMethod($"{receiverType.Name}.DispatchEntityAsk<{msgType.ToGenericTypeString()}>", typeof(Task), new Type[] { typeof(TReceiverType), typeof(EntityAsk), typeof(MetaMessage) }, receiverType);
            wrapper.DefineParameter(1, ParameterAttributes.None, "receiver");
            wrapper.DefineParameter(2, ParameterAttributes.None, "ask");
            wrapper.DefineParameter(3, ParameterAttributes.None, "message");

            ILGenerator il = wrapper.GetILGenerator();

            // Call receiver.HandleMessage(ask, (MessageType)message);  -- \note 'ask' is optional!
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, receiverType);
            if (hasAskArg)
                il.Emit(OpCodes.Ldarg_1);
            if (hasSourceEntityIdArg)
            {
                MethodInfo sourceIdGetter = typeof(EntityAsk).GetProperty("FromEntityId").GetGetMethod();
                MetaDebug.Assert(sourceIdGetter != null, $"EntityAsk must have {nameof(EntityAsk.FromEntityId)} getter");
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, sourceIdGetter);
            }
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Castclass, msgType);
            il.Emit(OpCodes.Call, method);

            // If returns the value, invoke ReplyToAsk(ask, <retValue>)
            if (hasReturnValue)
            {
                if (isAsync)
                {
                    // Emit: wrapper = new AsyncReplyWrapper(IEntityReceiver actor, EntityAsk ask)
                    Type replyWrapper = typeof(AsyncReplyWrapper);
                    il.Emit(OpCodes.Ldarg_0); // receiver
                    il.Emit(OpCodes.Ldarg_1); // ask
                    il.Emit(OpCodes.Newobj, replyWrapper.GetConstructor(new Type[] { typeof(IEntityReceiver), typeof(EntityAsk) }));

                    // Emit: action = new Action<Task<TReply>>(wrapper, wrapper.Execute)
                    Type actionType = typeof(Action<>).MakeGenericType(retType); // Action<Task<TReply>>
                    il.Emit(OpCodes.Ldftn, replyWrapper.GetMethod("Execute"));
                    il.Emit(OpCodes.Newobj, actionType.GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));

                    // Emit: retTask.ContinueWith(action)
                    MethodInfo taskContinueWith = retType.GetMethod("ContinueWith", new Type[] { typeof(Action<>).MakeGenericType(retType) });
                    MetaDebug.Assert(taskContinueWith != null, $"Unable to find {retType.ToGenericTypeString()}.ContinueWith(Action<{retType.ToGenericTypeString()}>)"); // Task<TReply>.ContinueWith(Action<Task<TReply>>)
                    il.Emit(OpCodes.Call, taskContinueWith);
                }
                else // sync: just call ReplyToAsk() with return value
                {
                    LocalBuilder localRetValue = il.DeclareLocal(retType); // TReply or Task<TReply>
                    il.Emit(OpCodes.Stloc, localRetValue.LocalIndex); // store return value

                    MethodInfo replyToAsk = receiverType.GetMethod("ReplyToAsk", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    MetaDebug.Assert(replyToAsk != null, $"Unable to find {receiverType.ToGenericTypeString()}.ReplyToAsk()");

                    il.Emit(OpCodes.Ldarg_0); // load this
                    il.Emit(OpCodes.Ldarg_1); // load ask
                    il.Emit(OpCodes.Ldloc, localRetValue.LocalIndex); // load return value
                    il.Emit(OpCodes.Call, replyToAsk);

                    il.Emit(OpCodes.Call, _getCompletedTask);
                }
            }
            else // no return value, just pass on Task (for async handlers) or Task.CompletedTask (for void returning handlers)
            {
                // If not async, load Task.CompletedTask as return value
                if (!isAsync)
                    il.Emit(OpCodes.Call, _getCompletedTask);
            }

            // Return Task
            il.Emit(OpCodes.Ret);

            // Register dispatch function
            if (dispatcher._entityAskDispatchers.ContainsKey(msgType))
                throw new InvalidOperationException($"Duplicate [EntityAskHandler] method for type {msgType.ToGenericTypeString()} in {receiverType.ToGenericTypeString()}");
            dispatcher._entityAskDispatchers.Add(msgType, (EntityDispatcherBase<TReceiverType>.DispatchEntityAsk)wrapper.CreateDelegate(typeof(EntityDispatcherBase<TReceiverType>.DispatchEntityAsk)));
        }

        static void RegisterDispatchSynchronizeFunc(EntityDispatcherBase<TReceiverType> dispatcher, Type receiverType, MethodInfo method)
        {
            ParameterInfo[] args = method.GetParameters();
            MetaDebug.Assert(args.Length == 2, "[EntitySynchronizeHandler] method {0}.{1}() must take exactly two parameters", receiverType.Name, method.Name);
            MetaDebug.Assert(args[0].ParameterType == typeof(EntitySynchronize), "[EntitySynchronizeHandler] method {0}.{1}(): first parameter must be EntitySynchronize", receiverType.Name, method.Name);
            MetaDebug.Assert(args[1].ParameterType.IsSubclassOf(typeof(MetaMessage)), "[EntitySynchronizeHandler] method {0}.{1}: second parameter must be derived from MetaMessage", receiverType.Name, method.Name);

            Type retType = method.ReturnType;
            bool isAsync;
            if (retType == typeof(Task))
                isAsync = true;
            else if (retType == typeof(void))
                isAsync = false;
            else
                throw new InvalidOperationException($"[EntitySynchronizeHandler] method {receiverType.Name}.{method.Name}() return type must be Task or void, got {retType.ToGenericTypeString()}");

            Type msgType = args[1].ParameterType;

            // Generate the dispatch function using ILGenerator
            DynamicMethod wrapper = new DynamicMethod($"{receiverType.Name}.DispatchEntitySynchronize<{msgType.ToGenericTypeString()}>", typeof(Task), new Type[] { typeof(TReceiverType), typeof(EntitySynchronize), typeof(MetaMessage) }, receiverType);
            wrapper.DefineParameter(1, ParameterAttributes.None, "receiver");
            wrapper.DefineParameter(2, ParameterAttributes.None, "sync");
            wrapper.DefineParameter(3, ParameterAttributes.None, "message");

            ILGenerator il = wrapper.GetILGenerator();

            // Call actor.HandleEntitySynchronize(sync, (MessageType)message);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, receiverType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Castclass, msgType);
            il.Emit(OpCodes.Call, method);

            // If not async, load Task.CompletedTask as return value
            if (!isAsync)
                il.Emit(OpCodes.Call, _getCompletedTask);

            // Return Task
            il.Emit(OpCodes.Ret);

            // Register dispatch function
            if (dispatcher._entitySyncDispatchers.ContainsKey(msgType))
                throw new InvalidOperationException($"Duplicate [EntitySynchronizeHandler] method for type {msgType.ToGenericTypeString()} in class {receiverType.ToGenericTypeString()}");
            dispatcher._entitySyncDispatchers.Add(msgType, (EntityDispatcherBase<TReceiverType>.DispatchEntitySynchronize)wrapper.CreateDelegate(typeof(EntityDispatcherBase<TReceiverType>.DispatchEntitySynchronize)));
        }

        static void RegisterDispatchPubSubFunc(EntityDispatcherBase<TReceiverType> dispatcher, Type receiverType, MethodInfo method)
        {
            ParameterInfo[] args = method.GetParameters();

            if (args.Length != 2)
                throw new InvalidOperationException($"[PubSubMessageHandler] method {receiverType.Name}.{method.Name}() must take exactly two parameters, got {args.Length}");

            bool isSubscriber;
            if (args[0].ParameterType == typeof(EntitySubscriber))
                isSubscriber = true;
            else if (args[0].ParameterType == typeof(EntitySubscription))
                isSubscriber = false;
            else
                throw new InvalidOperationException($"[PubSubMessageHandler] method {receiverType.Name}.{method.Name}() parameter 1 must be EntitySubscriber or EntitySubscription, got {args[0].ParameterType.Name}");

            Type msgType = args[1].ParameterType;
            if (!msgType.IsSubclassOf(typeof(MetaMessage)) && msgType != typeof(MetaMessage))
                throw new InvalidOperationException($"[PubSubMessageHandler] method {receiverType.Name}.{method.Name}() parameter 2 must be derived from MetaMessage, got {msgType.Name}");

            string msgTypeName;
            if (msgType != typeof(MetaMessage))
                msgTypeName = MetaMessageRepository.Instance.GetFromType(msgType).Name;
            else
                msgTypeName = "MetaMessage";

            if (isSubscriber && dispatcher._pubsubSubscriberDispatchers.ContainsKey(msgType))
                throw new InvalidOperationException($"Duplicate [PubSubMessageHandler] method for type {msgType.Name} in {receiverType.Name}.{method.Name}(EntitySubscriber, {msgTypeName})");
            else if (!isSubscriber && dispatcher._pubsubSubscriptionDispatchers.ContainsKey(msgType))
                throw new InvalidOperationException($"Duplicate [PubSubMessageHandler] method for type {msgType.Name} in {receiverType.Name}.{method.Name}(EntitySubscription, {msgTypeName})");

            Type retType = method.ReturnType;
            bool isAsync;
            if (retType == typeof(Task))
                isAsync = true;
            else if (retType == typeof(void))
                isAsync = false;
            else
                throw new InvalidOperationException($"[PubSubMessageHandler] method {receiverType.Name}.{method.Name}() return type must be Task or void, got {retType.ToGenericTypeString()}");

            DynamicMethod wrapper;
            if (isSubscriber)
            {
                wrapper = new DynamicMethod($"{receiverType.Name}.DispatchSubscriberPubSubMessage<{msgTypeName}>", typeof(Task), new Type[] { typeof(TReceiverType), typeof(EntitySubscriber), typeof(MetaMessage) }, receiverType);
                wrapper.DefineParameter(1, ParameterAttributes.None, "actor");
                wrapper.DefineParameter(2, ParameterAttributes.None, "subscriber");
                wrapper.DefineParameter(3, ParameterAttributes.None, "message");
            }
            else
            {
                wrapper = new DynamicMethod($"{receiverType.Name}.DispatchSubscriptionPubSubMessage<{msgTypeName}>", typeof(Task), new Type[] { typeof(TReceiverType), typeof(EntitySubscription), typeof(MetaMessage) }, receiverType);
                wrapper.DefineParameter(1, ParameterAttributes.None, "actor");
                wrapper.DefineParameter(2, ParameterAttributes.None, "subscription");
                wrapper.DefineParameter(3, ParameterAttributes.None, "message");
            }

            ILGenerator il = wrapper.GetILGenerator();

            // Call actor.HandlePubSubMessage(subscriber/subscription, (MessageType)message);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, receiverType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Castclass, msgType);
            il.Emit(OpCodes.Call, method);

            // If not async, load Task.CompletedTask as return value
            if (!isAsync)
                il.Emit(OpCodes.Call, _getCompletedTask);

            // Return Task
            il.Emit(OpCodes.Ret);

            if (isSubscriber)
                dispatcher._pubsubSubscriberDispatchers.Add(msgType, (EntityDispatcherBase<TReceiverType>.DispatchSubscriberMessage)wrapper.CreateDelegate(typeof(EntityDispatcherBase<TReceiverType>.DispatchSubscriberMessage)));
            else
                dispatcher._pubsubSubscriptionDispatchers.Add(msgType, (EntityDispatcherBase<TReceiverType>.DispatchSubscriptionMessage)wrapper.CreateDelegate(typeof(EntityDispatcherBase<TReceiverType>.DispatchSubscriptionMessage)));
        }
    }

    public class EntityDispatcherCombiner
    {
        EntityDispatcher _dispatcher;
        Type _actorType;
        // Additional information for component handler functions already added to the
        // dispatcher. This info is used solely for the purpose of being able to provide
        // more accurate information when conflicting handlers are detected. The dictionary
        // is keyed by (dispatcher type (message, cmd, ask, ..), message type).
        Dictionary<(Type, Type), Type> _debugInfo = new Dictionary<(Type, Type), Type>();

        public EntityDispatcherCombiner(EntityDispatcher actorDispatcher, Type actorType)
        {
            _dispatcher = actorDispatcher;
            _actorType = actorType;
        }

        static EntityDispatcher.DispatchMessage MakeComponentDispatcher(EntityComponentDispatcher.DispatchMessage componentDispatcher, int componentIndex)
        {
            return (actor, from, msg) => componentDispatcher(actor.GetComponentByIndex(componentIndex), from, msg);
        }

        static EntityDispatcher.DispatchCommand MakeComponentDispatcher(EntityComponentDispatcher.DispatchCommand componentDispatcher, int componentIndex)
        {
            return (actor, cmd) => componentDispatcher(actor.GetComponentByIndex(componentIndex), cmd);
        }

        static EntityDispatcher.DispatchEntityAsk MakeComponentDispatcher(EntityComponentDispatcher.DispatchEntityAsk componentDispatcher, int componentIndex)
        {
            return (actor, ask, msg) => componentDispatcher(actor.GetComponentByIndex(componentIndex), ask, msg);
        }

        static EntityDispatcher.DispatchEntitySynchronize MakeComponentDispatcher(EntityComponentDispatcher.DispatchEntitySynchronize componentDispatcher, int componentIndex)
        {
            return (actor, sync, msg) => componentDispatcher(actor.GetComponentByIndex(componentIndex), sync, msg);
        }

        static EntityDispatcher.DispatchSubscriptionMessage MakeComponentDispatcher(EntityComponentDispatcher.DispatchSubscriptionMessage componentDispatcher, int componentIndex)
        {
            return (actor, s, msg) => componentDispatcher(actor.GetComponentByIndex(componentIndex), s, msg);
        }

        static EntityDispatcher.DispatchSubscriberMessage MakeComponentDispatcher(EntityComponentDispatcher.DispatchSubscriberMessage componentDispatcher, int componentIndex)
        {
            return (actor, s, msg) => componentDispatcher(actor.GetComponentByIndex(componentIndex), s, msg);
        }


        void Combine<TActorDispatcher, TComponentDispatcher>(
            Type componentType,
            int componentIndex,
            Dictionary<Type, TActorDispatcher> dst,
            Dictionary<Type, TComponentDispatcher> src,
            Func<TComponentDispatcher, int, TActorDispatcher> wrapper)
        {
            foreach ((Type messageType, TComponentDispatcher dispatcher) in src)
            {
                if (!dst.TryAdd(messageType, wrapper(dispatcher, componentIndex)))
                {
                    // In case of a conflict, the existing handler can either be in the base actor or in a previously combined component.
                    // In the case of the latter, _debugInfo will contain the previously added component receiver type.
                    if (!_debugInfo.TryGetValue((typeof(TComponentDispatcher), messageType), out Type existingReceiver))
                        existingReceiver = _actorType;
                    throw new Exception($"Conflicting handlers for {messageType}: declared both in {existingReceiver} and {componentType}");
                }
                _debugInfo.Add((typeof(TComponentDispatcher), messageType), componentType);
            }
        }

        public void Combine(EntityComponentDispatcher componentDispatcher, Type componentType, int componentIndex)
        {
            Combine(componentType, componentIndex, _dispatcher._messageDispatchers, componentDispatcher._messageDispatchers, MakeComponentDispatcher);
            Combine(componentType, componentIndex, _dispatcher._commandDispatchers, componentDispatcher._commandDispatchers, MakeComponentDispatcher);
            Combine(componentType, componentIndex, _dispatcher._entityAskDispatchers, componentDispatcher._entityAskDispatchers, MakeComponentDispatcher);
            Combine(componentType, componentIndex, _dispatcher._entitySyncDispatchers, componentDispatcher._entitySyncDispatchers, MakeComponentDispatcher);
            Combine(componentType, componentIndex, _dispatcher._pubsubSubscriptionDispatchers, componentDispatcher._pubsubSubscriptionDispatchers, MakeComponentDispatcher);
            Combine(componentType, componentIndex, _dispatcher._pubsubSubscriberDispatchers, componentDispatcher._pubsubSubscriberDispatchers, MakeComponentDispatcher);
        }
    }

    public static class EntityDispatcherRegistry
    {
        static Dictionary<EntityKind, EntityDispatcher> _dispatchers = new Dictionary<EntityKind, EntityDispatcher>();

        public static void RegisterAll()
        {
            foreach ((_, EntityConfigBase entityConfig) in EntityConfigRegistry.Instance.TypeToEntityConfig)
            {
                EntityDispatcher dispatcher = new EntityDispatcher();

                EntityDispatcherBuilder<EntityActor>.Build(dispatcher, entityConfig.EntityActorType);

                if (EntityActor.TryGetComponentTypes(entityConfig.EntityKind, out List<Type> components))
                {
                    EntityDispatcherCombiner combiner = new EntityDispatcherCombiner(dispatcher, entityConfig.EntityActorType);

                    foreach ((Type componentType, int index) in components.ZipWithIndex())
                    {
                        EntityComponentDispatcher componentDispatcher = new EntityComponentDispatcher();
                        EntityDispatcherBuilder<EntityComponent>.Build(componentDispatcher, componentType);
                        combiner.Combine(componentDispatcher, componentType, index);
                    }
                }

                _dispatchers.Add(entityConfig.EntityKind, dispatcher);
            }
        }

        public static EntityDispatcher Get(EntityKind kind) => _dispatchers[kind];
    }
}
