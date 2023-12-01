// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Represents an error during serialization or deserialization.
    /// </summary>
    public class MetaSerializationException : Exception
    {
        public MetaSerializationException() { }
        public MetaSerializationException(string message) : base(message) { }
        public MetaSerializationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// A deserialization error caused by an unknown derived type code.
    /// </summary>
    public class MetaUnknownDerivedTypeDeserializationException : MetaSerializationException
    {
        public readonly Type            AttemptedType;
        public readonly int             EncounteredTypeCode;

        public MetaUnknownDerivedTypeDeserializationException(string message, Type attemptedType, int encounteredTypeCode)
            : base(message)
        {
            AttemptedType = attemptedType;
            EncounteredTypeCode = encounteredTypeCode;
        }
    }

    /// <summary>
    /// A deserialization error caused by mismatching wire data types.
    /// </summary>
    public class MetaWireDataTypeMismatchDeserializationException : MetaSerializationException
    {
        public readonly string       MemberName;
        public readonly Type         AttemptedType;
        public readonly WireDataType ExpectedWireDataType;
        public readonly WireDataType EncounteredWireDataType;

        public MetaWireDataTypeMismatchDeserializationException(string message, string memberName, Type attemptedType, WireDataType expectedWireDataType, WireDataType encounteredWireDataType)
            : base(message)
        {
            MemberName              = memberName;
            AttemptedType           = attemptedType;
            ExpectedWireDataType    = expectedWireDataType;
            EncounteredWireDataType = encounteredWireDataType;
        }
    }

    /// <summary>
    /// Flags to specify how serialization operations should behave. These essentially exclude members
    /// based on <see cref="MetaMemberFlags"/>.
    /// </summary>
    [MetaSerializable]
    public enum MetaSerializationFlags
    {
        IncludeAll      = 0,                            // Include all fields (local cloning etc.)
        SendOverNetwork = MetaMemberFlags.Hidden,       // All but hidden fields are transmitted over network
        ComputeChecksum = MetaMemberFlags.NoChecksum,   // Checksum computation ignores NoChecksum members
        Persisted       = MetaMemberFlags.Transient,    // Include all persisted members
    }

    public ref struct MetaSerializationMemberContext
    {
        public string MemberName        { get; private set; }
        public int    MaxCollectionSize { get; private set; }

        public void UpdateMemberName(string memberName)
        {
            MemberName = memberName;
        }

        public void UpdateCollectionSize(int maxCollectionSize)
        {
            MaxCollectionSize = maxCollectionSize;
        }

        public void Update(string memberName, int maxCollectionSize)
        {
            UpdateMemberName(memberName);
            UpdateCollectionSize(maxCollectionSize);
        }
    }

    /// <summary>
    /// Represents state passed into serialization functions.
    /// </summary>
    public ref struct MetaSerializationContext
    {
        public readonly MetaMemberFlags                 ExcludeFlags;   // Mask of member flags to exclude when serializing.
        public readonly IGameConfigDataResolver         Resolver;       // Resolver for GameConfigData
        public readonly int?                            LogicVersion;   // LogicVersion of data to serialize (null means ignore any version attributes)
        public readonly StringBuilder                   DebugStream;    // Output stream for all debug data

        public MetaSerializationMetaRefTraversalParams MetaRefTraversal;

#if NETCOREAPP // cloud
        public readonly Akka.Actor.ExtendedActorSystem  ActorSystem;    // Akka.net actor system (for deserializing IActorRefs)
#endif

        // \todo [petri] make configurable?
        public int MaxStringSize        => 64 * 1024 * 1024;    // Maximum size of encoded string (in bytes)
        public int MaxByteArraySize     => 64 * 1024 * 1024;    // Maximum size of byte array

        public const int DefaultMaxCollectionSize = 16384;

        public MetaSerializationMemberContext MemberContext;

        public MetaSerializationContext(
            MetaSerializationFlags flags,
            IGameConfigDataResolver resolver,
            int? logicVersion,
            StringBuilder debugStream,
            MetaSerializationMetaRefTraversalParams metaRefTraversal
#if NETCOREAPP // cloud
            , Akka.Actor.ExtendedActorSystem actorSystem
#endif
            )
        {
            // \note MetaSerializationFlags is already an exclude mask, so we just cast here
            ExcludeFlags    = (MetaMemberFlags)flags;
            Resolver        = resolver;
            LogicVersion    = logicVersion;
            DebugStream     = debugStream;

            MetaRefTraversal = metaRefTraversal;

#if NETCOREAPP // cloud
            ActorSystem     = actorSystem;
#endif
            MemberContext = new MetaSerializationMemberContext();
            MemberContext.UpdateCollectionSize(DefaultMaxCollectionSize);
        }

        public MetaRef<TItem> VisitMetaRef<TItem>(MetaRef<TItem> metaRef)
            where TItem : class, IGameConfigData
        {
            if (MetaRefTraversal.VisitMetaRef == null)
                return metaRef;

            IMetaRef newMetaRef = metaRef;
            MetaRefTraversal.VisitMetaRef.Invoke(ref this, ref newMetaRef);
            if (!ReferenceEquals(newMetaRef, metaRef))
            {
                if (!MetaRefTraversal.IsMutatingOperation)
                    throw new InvalidOperationException($"Attempted to modify MetaRef during traversal even though {nameof(MetaRefTraversal.IsMutatingOperation)} was false");

                if (!(newMetaRef is MetaRef<TItem>))
                    throw new InvalidOperationException("Attempted to change concrete type of MetaRef during traversal");
            }

            return (MetaRef<TItem>)newMetaRef;
        }
    }

    public struct MetaSerializationMetaRefTraversalParams
    {
        public delegate void VisitTableTopLevelConfigItemDelegate(ref MetaSerializationContext context, IGameConfigData item);
        public delegate void VisitMetaRefDelegate(ref MetaSerializationContext context, ref IMetaRef metaRef);

        public readonly VisitTableTopLevelConfigItemDelegate VisitTableTopLevelConfigItem;
        public readonly VisitMetaRefDelegate VisitMetaRef;
        public readonly bool IsMutatingOperation;

        DefaultTraversalState _defaultState;

        /// <summary>
        /// State storage for DefaultVisitTableTopLevelConfigItem and DefaultVisitMetaRef.
        /// Stored here (instead of capturing in a lambda) in order to avoid allocations in
        /// the default case.
        /// This puts the default case in a bit of a privileged position of being able to
        /// work without allocation. Is there a way to do that with custom MetaSerializationMetaRefTraversalParams?
        /// </summary>
        struct DefaultTraversalState
        {
            public IGameConfigData CurrentConfigItem;
        }

        public MetaSerializationMetaRefTraversalParams(VisitTableTopLevelConfigItemDelegate visitTableTopLevelConfigItem, VisitMetaRefDelegate visitMetaRef, bool isMutatingOperation)
        {
            VisitTableTopLevelConfigItem = visitTableTopLevelConfigItem;
            VisitMetaRef = visitMetaRef;
            IsMutatingOperation = isMutatingOperation;
            _defaultState = default;
        }

        public static MetaSerializationMetaRefTraversalParams CreateDefault()
        {
            return new MetaSerializationMetaRefTraversalParams(DefaultVisitTableTopLevelConfigItem, DefaultVisitMetaRef, isMutatingOperation: true);
        }

        // \note This is a static delegate instead of a method in order to avoid allocating in CreateDefault.
        static readonly VisitTableTopLevelConfigItemDelegate DefaultVisitTableTopLevelConfigItem = (ref MetaSerializationContext context, IGameConfigData item) =>
        {
            context.MetaRefTraversal._defaultState.CurrentConfigItem = item;
        };

        // \note This is a static delegate instead of a method in order to avoid allocating in CreateDefault.
        static readonly VisitMetaRefDelegate DefaultVisitMetaRef = (ref MetaSerializationContext context, ref IMetaRef metaRef) =>
        {
            try
            {
                metaRef = metaRef.CreateResolved(context.Resolver);
            }
            catch (Exception ex)
            {
                throw new MetaRefResolveError(context.MetaRefTraversal._defaultState.CurrentConfigItem, ex);
            }
        };
    }

    /// <summary>
    /// A <see cref="MetaRef{TItem}"/> failed to resolve. This can happen either during game config builds,
    /// in which case the <see cref="GameConfigEntryName"/> gets filled in to help narrow down where the
    /// error happened, or it can happen during runtime validation, in which case we don't have the game
    /// config entry name.
    /// </summary>
    public class MetaRefResolveError : Exception
    {
        // \todo Better handling of where resolve errors happen
        public IGameConfigData  ConfigItem          { get; }
        public string           GameConfigEntryName { get; set; } // Filled after-the-fact, used limit source mapping to matching GameConfigSourceMapping only

        public MetaRefResolveError(IGameConfigData configItem, Exception innerException) : base(CreateMessage(configItem), innerException)
        {
            ConfigItem = configItem;
        }

        static string CreateMessage(IGameConfigData configItem)
        {
            object configKey = configItem == null ? null : GameConfigItemHelper.GetItemConfigKey(configItem);
            if (configKey != null)
                return Invariant($"Failed to resolve MetaRef in game config item '{configKey}'");
            return Invariant($"Failed to resolve MetaRef");
        }
    }

    /// <summary>
    /// Describes a failure that occurred when deserializing a member of a struct/class.
    /// This is passed to a handler registered with the <see cref="MetaOnMemberDeserializationFailureAttribute"/>.
    /// </summary>
    public struct MetaMemberDeserializationFailureParams
    {
        /// <summary>
        /// The serialized payload of the member.
        /// </summary>
        /// <remarks>
        /// This is _not_ prefixed with the wire data type tag for the member,
        /// nor with the member tag id. Those are known statically from the
        /// member's static type and the MetaMember tag id, respectively.
        /// </remarks>
        public readonly byte[]                      MemberPayload;
        /// <summary>
        /// The exception that caused the failure.
        /// </summary>
        public readonly Exception                   Exception;

        // \todo [nuutti] Include MetaSerializationContext. It's a ref struct, so cannot include as is.
        //                MetaMemberDeserializationFailureParams itself cannot be ref struct at the moment,
        //                since MemberAccessGenerator boxes it.

        public MetaMemberDeserializationFailureParams(byte[] memberPayload, Exception exception)
        {
            MemberPayload = memberPayload;
            Exception = exception;
        }
    }

    /// <summary>
    /// Parameters (optionally) passed to a custom on-deserialized handler registered
    /// with the <see cref="MetaOnDeserializedAttribute"/>.
    /// </summary>
    public struct MetaOnDeserializedParams
    {
        public readonly IGameConfigDataResolver Resolver;
        public readonly int?                    LogicVersion;

        public MetaOnDeserializedParams(ref MetaSerializationContext context)
        {
            Resolver = context.Resolver;
            LogicVersion = context.LogicVersion;
        }
    }

    /// <summary>
    /// High-level logic object serialization API.
    ///
    /// Provides functions for both compact (over-the-wire) and tagged (database persisting) serialization.
    /// </summary>
    public static class MetaSerialization
    {
        /// <summary>
        /// Simple wrapper to allow the use of 'using' keyword for borrowing a thread-local buffer from
        /// _recycleBuffers with added safety mechanism in Clear() to avoid an IOBuffer in broken state
        /// to fail all future serializations on the thread.
        /// </summary>
        internal struct BorrowedIOBuffer : IDisposable
        {
            public IOBuffer Buffer => _recycleBuffers.Value;

            public void Dispose()
            {
                // Extra-safe buffer releases: in case the buffer Clear() fails, we re-create the buffer
                // as otherwise all future serialization on this thread may end up failing.
                try
                {
                    _recycleBuffers.Value.Clear();
                }
                catch (Exception ex)
                {
                    DebugLog.Error("Failed to release serializer recycled buffer, reallocating buffer: {Error}", ex);
                    _recycleBuffers.Value = new SegmentedIOBuffer();
                }
            }
        }

#if NETCOREAPP // cloud
        static Akka.Actor.ExtendedActorSystem s_actorSystem = null;
#endif

        static TaggedSerializerRoslyn s_taggedSerializerRoslyn = null;

        // Recycled SegmentedIOBuffers for each thread to enable per-segment memory re-use
        static ThreadLocal<IOBuffer> _recycleBuffers = new ThreadLocal<IOBuffer>(() => new SegmentedIOBuffer());

        public static bool IsInitialized => s_taggedSerializerRoslyn != null;

        public static void Initialize(
            Type roslynGeneratedSerializer
#if NETCOREAPP // cloud
            , Akka.Actor.ExtendedActorSystem actorSystem
#endif
            )
        {
#if NETCOREAPP // cloud
            s_actorSystem = actorSystem;
#endif
            s_taggedSerializerRoslyn = new TaggedSerializerRoslyn(roslynGeneratedSerializer);
            if (RuntimeTypeInfoProvider.TryCreateFromGeneratedCode(roslynGeneratedSerializer, out RuntimeTypeInfoProvider typeInfoProvider))
                MetaSerializerTypeRegistry.RegisterRuntimeTypeInfoProvider(typeInfoProvider);

#if !NETCOREAPP
            WarmUpSerializerForUnity();
#endif
        }

#if !NETCOREAPP
        static void WarmUpSerializerForUnity()
        {
            // It seems that:
            //
            // Unity on Android uses stop-the-world Garbage collection that forcibly interrupts all threads, including background threads,
            // regardless of what they are processing at the time. If GC is triggered while a background thread is initializing lazily initialized IL2CPP
            // object metadata (i.e. accessing a type for the first time in the process instance), it still holds the the internal lock IL2CPP metadata
            // lock while it's parked for GC. Now, if UnityMain thread decides to access non-initialized lazily initialized metadata record for the GC sweep,
            // it first tries to take the lock, notices it's not available, and chooses to wait for it. And this wait never completes.
            //
            // Avoid this by initializing lazily-init static class initializers immediately with a dummy serialization operation.
            //
            // \todo [jarkko] make a reliable repro and report to unity.

            MetaSerializationContext context = CreateContext(MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null, debugStream: null);
            using (FlatIOBuffer buffer = new FlatIOBuffer())
            using (IOWriter writer = new IOWriter(buffer))
                s_taggedSerializerRoslyn.Serialize<MetaMessage>(ref context, writer, null);
        }
#endif

        // TAGGED SERIALIZATION

        public static void CheckInitialized()
        {
            if (s_taggedSerializerRoslyn == null)
                throw new InvalidOperationException($"Serialization must be initialized by calling MetaSerialization.Initialize()");

            if (!MetaplayCore.IsInitialized)
                throw new InvalidOperationException($"MetaplayCore.Initialize() must be called before (de)serializing any data");
        }

        static MetaSerializationContext CreateContext(MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream,
            MetaSerializationMetaRefTraversalParams? metaRefTraversalParams = null)
        {
            return new MetaSerializationContext(flags, resolver, logicVersion, debugStream, metaRefTraversalParams ?? MetaSerializationMetaRefTraversalParams.CreateDefault()
#if NETCOREAPP // cloud
                , s_actorSystem
#endif
                );
        }

        static BorrowedIOBuffer BorrowIOBuffer()
        {
            return new BorrowedIOBuffer();
        }

        public static byte[] SerializeTagged<T>(T value, MetaSerializationFlags flags, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver: null, logicVersion, debugStream);
            using (BorrowedIOBuffer buffer = BorrowIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer.Buffer))
                    s_taggedSerializerRoslyn.Serialize<T>(ref context, writer, value);
                return buffer.Buffer.ToArray();
            }
        }

        public static void SerializeTagged<T>(IOWriter writer, T value, MetaSerializationFlags flags, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver: null, logicVersion, debugStream);
            s_taggedSerializerRoslyn.Serialize<T>(ref context, writer, value);
        }

        public static byte[] SerializeTagged(Type type, object obj, MetaSerializationFlags flags, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver: null, logicVersion, debugStream);
            using (BorrowedIOBuffer buffer = BorrowIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer.Buffer))
                    s_taggedSerializerRoslyn.Serialize(ref context, writer, type, obj);
                return buffer.Buffer.ToArray();
            }
        }

        public static void SerializeTagged(IOWriter writer, Type type, object obj, MetaSerializationFlags flags, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver: null, logicVersion, debugStream);
            s_taggedSerializerRoslyn.Serialize(ref context, writer, type, obj);
        }

        public static MetaSerialized<T> ToMetaSerialized<T>(T value, MetaSerializationFlags flags, int? logicVersion, StringBuilder debugStream = null)
        {
            byte[] bytes = SerializeTagged(value, flags, logicVersion, debugStream);
            return new MetaSerialized<T>(bytes, flags);
        }

        public static byte[] SerializeTableTagged<T>(IReadOnlyList<T> items, MetaSerializationFlags flags, int? logicVersion, StringBuilder debugStream = null, int maxCollectionSizeOverride = MetaSerializationContext.DefaultMaxCollectionSize)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver: null, logicVersion, debugStream);
            using (BorrowedIOBuffer buffer = BorrowIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer.Buffer))
                    s_taggedSerializerRoslyn.SerializeTable<T>(ref context, writer, items, maxCollectionSizeOverride);
                return buffer.Buffer.ToArray();
            }
        }

        public static void SerializeTableTagged<T>(IOWriter writer, IReadOnlyList<T> items, MetaSerializationFlags flags, int? logicVersion, StringBuilder debugStream = null, int maxCollectionSizeOverride = MetaSerializationContext.DefaultMaxCollectionSize)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver: null, logicVersion, debugStream);
            s_taggedSerializerRoslyn.SerializeTable<T>(ref context, writer, items, maxCollectionSizeOverride);
        }

        public static T DeserializeTagged<T>(byte[] serialized, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver, logicVersion, debugStream);
            using (IOReader reader = new IOReader(serialized))
            {
                T result = s_taggedSerializerRoslyn.Deserialize<T>(ref context, reader);
                return result;
            }
        }

        public static object DeserializeTagged(byte[] serialized, Type type, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver, logicVersion, debugStream);
            using (IOReader reader = new IOReader(serialized))
            {
                object result = s_taggedSerializerRoslyn.Deserialize(ref context, reader, type);
                return result;
            }
        }

        public static T DeserializeTagged<T>(IOReader reader, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver, logicVersion, debugStream);
            T result = s_taggedSerializerRoslyn.Deserialize<T>(ref context, reader);
            return result;
        }

        public static object DeserializeTagged(IOReader reader, Type type, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream = null)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver, logicVersion, debugStream);
            object result  = s_taggedSerializerRoslyn.Deserialize(ref context, reader, type);
            return result;
        }

        public static IReadOnlyList<T> DeserializeTableTagged<T>(byte[] serialized, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream = null, int maxCollectionSizeOverride = MetaSerializationContext.DefaultMaxCollectionSize)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver, logicVersion, debugStream);
            using (IOReader reader = new IOReader(serialized))
            {
                return s_taggedSerializerRoslyn.DeserializeTable<T>(ref context, reader, maxCollectionSizeOverride, typeof(T));
            }
        }

        public static IReadOnlyList<T> DeserializeTableTagged<T>(IOReader reader, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream = null, int maxCollectionSizeOverride = MetaSerializationContext.DefaultMaxCollectionSize)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver, logicVersion, debugStream);
            return s_taggedSerializerRoslyn.DeserializeTable<T>(ref context, reader, maxCollectionSizeOverride, typeof(T));
        }

        public static IReadOnlyList<T> DeserializeTableTagged<T>(IOReader reader, Type type, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int? logicVersion, StringBuilder debugStream = null, int maxCollectionSizeOverride = MetaSerializationContext.DefaultMaxCollectionSize)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(flags, resolver, logicVersion, debugStream);
            return s_taggedSerializerRoslyn.DeserializeTable<T>(ref context, reader, maxCollectionSizeOverride, type);
        }

        public static T CloneTagged<T>(T value, MetaSerializationFlags flags, int? logicVersion, IGameConfigDataResolver resolver)
        {
            CheckInitialized();

            using (BorrowedIOBuffer buffer = BorrowIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer.Buffer))
                    SerializeTagged(writer, value, flags, logicVersion);
                using (IOReader reader = new IOReader(buffer.Buffer))
                    return DeserializeTagged<T>(reader, flags, resolver, logicVersion);
            }
        }

        public static object CloneTagged(Type type, object obj, MetaSerializationFlags flags, int? logicVersion, IGameConfigDataResolver resolver)
        {
            CheckInitialized();

            using (BorrowedIOBuffer buffer = BorrowIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer.Buffer))
                    SerializeTagged(writer, type, obj, flags, logicVersion);
                using (IOReader reader = new IOReader(buffer.Buffer))
                    return DeserializeTagged(reader, type, flags, resolver, logicVersion);
            }
        }

        public static IReadOnlyList<T> CloneTableTagged<T>(List<T> items, MetaSerializationFlags flags, int? logicVersion, IGameConfigDataResolver resolver, int maxCollectionSizeOverride = MetaSerializationContext.DefaultMaxCollectionSize)
        {
            CheckInitialized();

            using (BorrowedIOBuffer buffer = BorrowIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer.Buffer))
                    SerializeTableTagged(writer, items, flags, logicVersion, maxCollectionSizeOverride: maxCollectionSizeOverride);
                using (IOReader reader = new IOReader(buffer.Buffer))
                    return DeserializeTableTagged<T>(reader, flags, resolver, logicVersion, maxCollectionSizeOverride: maxCollectionSizeOverride);
            }
        }

        /// <summary>
        /// Resolve MetaRefs that are contained in the object tree rooted at <paramref name="obj"/>,
        /// and possibly adjust <paramref name="obj"/> to refer the object to be used as the new root.
        /// Whether the <paramref name="obj"/> reference is modified depends on its concrete type;
        /// see remarks.
        /// </summary>
        /// <remarks>
        /// The operation is generally destructive, i.e. it does not create a full modified clone
        /// of the object tree, but rather attempts to work in-place. Shallow copies may be made
        /// for parts of the tree as needed: for example, certain kinds of collections are not
        /// easy to mutate in-place, in which case the top-level of the collection is newly created.
        /// Even if a shallow copy is made for a part of the tree, further subtrees are still
        /// subject to being destructively mutated and being shared between the resulting tree and
        /// the original tree.
        ///
        /// The specifics of when in-place vs copying behavior occurs should be considered an
        /// implementation detail, except that by-members reference-typed "plain objects" are
        /// guaranteed to not be copied. This also implies that if <paramref name="obj"/> is of
        /// such a type, the <paramref name="obj"/> reference will not be changed.
        /// </remarks>
        public static void ResolveMetaRefs(Type type, ref object obj, IGameConfigDataResolver resolver)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(MetaSerializationFlags.IncludeAll, resolver, logicVersion: null, debugStream: null);
            s_taggedSerializerRoslyn.TraverseMetaRefs(ref context, type, ref obj);
        }

        /// <summary>
        /// Static typing helper for <see cref="ResolveMetaRefs(Type, ref object, IGameConfigDataResolver)"/>.
        /// </summary>
        public static void ResolveMetaRefs<T>(ref T value, IGameConfigDataResolver resolver)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(MetaSerializationFlags.IncludeAll, resolver, logicVersion: null, debugStream: null);
            s_taggedSerializerRoslyn.TraverseMetaRefs<T>(ref context, ref value);
        }

        /// <summary>
        /// Like <see cref="ResolveMetaRefs(Type, ref object, IGameConfigDataResolver)"/> but operates on
        /// a list of config data items that will be traversed into (instead of the default behavior of treating
        /// config data items as config data references).
        /// </summary>
        public static void ResolveMetaRefsInTable<T>(List<T> items, IGameConfigDataResolver resolver)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(MetaSerializationFlags.IncludeAll, resolver, logicVersion: null, debugStream: null);
            s_taggedSerializerRoslyn.TraverseMetaRefsInTable(ref context, items);
        }

        public static void TraverseMetaRefsInTable<T>(List<T> items, IGameConfigDataResolver resolver, MetaSerializationMetaRefTraversalParams metaRefTraversal)
        {
            CheckInitialized();

            MetaSerializationContext context = CreateContext(MetaSerializationFlags.IncludeAll, resolver, logicVersion: null, debugStream: null, metaRefTraversal);
            s_taggedSerializerRoslyn.TraverseMetaRefsInTable(ref context, items);
        }
    }
}
