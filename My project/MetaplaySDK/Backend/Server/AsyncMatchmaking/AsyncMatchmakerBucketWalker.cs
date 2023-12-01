// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Utility;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Metaplay.Server.Matchmaking
{
    /// <summary>
    /// <para>
    /// <see cref="AsyncMatchmakerBucketWalker{TMmPlayerModel,TMmQuery}"/> is an enumerable type used for matchmaking queries that iterates all the buckets in an alternating fashion,
    /// starting from the bucket that corresponds to the player's AttackMmr.
    /// </para><para>
    ///
    /// First, two <see cref="PrimeRandomOrderEnumerator{T}"/>s are initialized based on the querying Player's AttackMmr.
    /// </para><para>
    ///
    /// While iterating, the walker iterates one step in the internal enumerators in an alternating fashion, so that two buckets are iterated "simultaneously".
    /// When the end of one enumerator is reached, the enumerator moves to the next bucket in the underlying bucket enumerator.
    /// </para><para>
    ///
    /// This way, we start from the most likely matches for the player (while also taking into account if a player is near a bucket border), but will still
    /// eventually iterate all the players in all buckets.
    /// </para>
    /// </summary>
    public readonly struct AsyncMatchmakerBucketWalker<TMmPlayerModel, TMmQuery> : IEnumerable<TMmPlayerModel>
        where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
        where TMmQuery : IAsyncMatchmakerQuery
    {
        readonly AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery> _pool;
        readonly TMmQuery                 _query;

        public AsyncMatchmakerBucketWalker(AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery> pool, TMmQuery query)
        {
            _pool = pool;
            _query = query;
        }

        public WalkerEnumerator GetEnumerator() => new WalkerEnumerator(_pool, _query);

        IEnumerator<TMmPlayerModel> IEnumerable<TMmPlayerModel>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct WalkerEnumerator : IEnumerator<TMmPlayerModel>
        {
            readonly AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery> _pool;

            IEnumerator<AsyncMatchmakerBucket<TMmPlayerModel>> _bucketEnumerator;
            IEnumerator<TMmPlayerModel>                   _currentHighEnumerator;
            IEnumerator<TMmPlayerModel>                   _currentLowEnumerator;
            int                                           _index;

            public WalkerEnumerator(AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery> pool, TMmQuery query)
            {
                _pool             = pool;
                _bucketEnumerator = pool.QueryBuckets(query).GetEnumerator();

                _currentHighEnumerator = null;
                _currentLowEnumerator  = null;
                _index                 = -1;
                Current                = default;

                NextHighBucket();
                NextLowBucket();
            }

            void NextHighBucket()
            {
                _currentHighEnumerator?.Dispose();
                if (_bucketEnumerator.MoveNext())
                    _currentHighEnumerator = _bucketEnumerator.Current.GetRandomOrderEnumerator();
                else
                    _currentHighEnumerator = null;
            }

            void NextLowBucket()
            {
                _currentLowEnumerator?.Dispose();
                if (_bucketEnumerator.MoveNext())
                    _currentLowEnumerator = _bucketEnumerator.Current.GetRandomOrderEnumerator();
                else
                    _currentLowEnumerator = null;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    _index = (_index == 0) ? 1 : 0; // Switch index from 0 to 1 and vice versa

                    if (_index == 0 && _currentLowEnumerator != null)
                    {
                        if (_currentLowEnumerator.MoveNext())
                        {
                            Current = _currentLowEnumerator.Current;
                            return true;
                        }

                        NextLowBucket();
                    }
                    else if (_index == 1 && _currentHighEnumerator != null)
                    {
                        if (_currentHighEnumerator.MoveNext())
                        {
                            Current = _currentHighEnumerator.Current;
                            return true;
                        }

                        NextHighBucket();
                    }

                    if (_currentLowEnumerator == null && _currentHighEnumerator == null)
                        return false;
                }
            }

            public void Reset()
            {
                throw new NotImplementedException();
            }

            public TMmPlayerModel Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                if (_bucketEnumerator != null)
                    _pool?.EndQuery();

                _currentHighEnumerator?.Dispose();
                _currentLowEnumerator?.Dispose();
                _bucketEnumerator?.Dispose();

                _currentLowEnumerator  = null;
                _currentHighEnumerator = null;
                _bucketEnumerator      = null;
            }
        }
    }
}
