// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Threading.Tasks;

namespace Metaplay.Server.Database
{
    public interface IDatabaseThrottle
    {
        public Task<IDisposable> LockAsync(DatabaseReplica replica, int shardNdx);
    }

    public class DatabaseThrottleNop : IDatabaseThrottle
    {
        public static DatabaseThrottleNop Instance { get; } = new DatabaseThrottleNop();
        static readonly Task<IDisposable> Completed = Task.FromResult<IDisposable>(null);

        public Task<IDisposable> LockAsync(DatabaseReplica replica, int shardNdx)
        {
            return Completed;
        }
    }

    /// <summary>
    /// Helper for managing per-shard data in <see cref="IDatabaseThrottle"/>.
    /// </summary>
    public class DatabaseThrottlePerShardState<TState>
    {
        readonly TState[] _states;
        readonly int _numShards;

        public DatabaseThrottlePerShardState(int numShards, Func<DatabaseReplica, int, TState> stateInitializer)
        {
            _numShards = numShards;
            _states = new TState[2 * numShards];
            for (int ndx = 0; ndx < numShards; ndx++)
                _states[ndx] = stateInitializer(DatabaseReplica.ReadWrite, ndx);
            for (int ndx = 0; ndx < numShards; ndx++)
                _states[ndx+numShards] = stateInitializer(DatabaseReplica.ReadOnly, ndx);
        }

        public TState GetState(DatabaseReplica replica, int shardNdx)
        {
            if (replica == DatabaseReplica.ReadWrite)
                return _states[shardNdx];
            else
                return _states[shardNdx + _numShards];
        }
    }
}
