// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using Metaplay.Core;

namespace Metaplay.Server.Matchmaking
{
    /// <summary>
    /// A pool of matchmaker buckets. The pool handles finding the right bucket for each player
    /// and querying nearby buckets. Each bucket is identified by its labels,
    /// which come from the registered bucketing strategies, 1 per each strategy.
    /// </summary>
    public class AsyncMatchmakerBucketPool<TMmPlayerModel, TMatchmakerQuery>
        where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
    {
        public int NumQueryParams { get; private set; }

        readonly Func<IBucketLabel[], AsyncMatchmakerBucket<TMmPlayerModel>> _createBucketFunc;
        readonly IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery>[] _bucketingStrategies;
        readonly IBucketLabel[] _queryParameters;
        readonly int[] _queryLabelIndices;
        readonly int _mmrStrategyIndex;

        int _queryHash;
        bool _queryOnGoing;

        List<IBucketLabel>[] _allLabels;

        readonly Dictionary<int, AsyncMatchmakerBucket<TMmPlayerModel>[]> _buckets = new Dictionary<int, AsyncMatchmakerBucket<TMmPlayerModel>[]>();

        public IEnumerable<AsyncMatchmakerBucket<TMmPlayerModel>> AllBuckets =>
            _buckets.Values.SelectMany(x => x);

        public int  NumBuckets => _buckets.Values.Sum(x => x.Length);
        public bool IsEmpty    => _buckets.Count == 0;

        public AsyncMatchmakerBucketPool(
            IEnumerable<IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery>> bucketingStrategies,
            Func<IBucketLabel[], AsyncMatchmakerBucket<TMmPlayerModel>> createBucketFunc)
        {
            _createBucketFunc = createBucketFunc;
            _bucketingStrategies = bucketingStrategies.ToArray();
            NumQueryParams = _bucketingStrategies.Length;
            _queryParameters = new IBucketLabel[NumQueryParams];
            _allLabels = new List<IBucketLabel>[NumQueryParams];
            _queryLabelIndices = new int[NumQueryParams];

            for (int i = 0; i < _allLabels.Length; i++)
                _allLabels[i] = new List<IBucketLabel>();

            (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery> strategy, int index) mmrStrategy =
                _bucketingStrategies.ZipWithIndex().FirstOrDefault(x => x.Item1.LabelType == typeof(MmrBucketingStrategyLabel));

            _mmrStrategyIndex = mmrStrategy.strategy != null ? mmrStrategy.index : -1;
        }

        public AsyncMatchmakerBucket<TMmPlayerModel> TryGetBucketByHash(int hash)
        {
            if (_buckets.TryGetValue(hash, out AsyncMatchmakerBucket<TMmPlayerModel>[] buckets))
                return buckets[0];
            return null;
        }

        public int GetLabelHashForPlayer(TMmPlayerModel player)
        {
            LabelHashBuilder hashBuilder = default;

            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMatchmakerQuery> matchmakerBucketingStrategy in _bucketingStrategies)
            {
                IBucketLabel label = matchmakerBucketingStrategy.GetBucketLabel(player);
                hashBuilder.Add(label);
            }

            return hashBuilder.Build();
        }

        /// <summary>
        /// Build a query that matches the given player.
        /// Sets _queryParameters and _queryHash.
        /// Also calls <see cref="BeginQuery"/> implicitly.
        /// </summary>
        void BuildQuery(TMmPlayerModel player)
        {
            BeginQuery();
            LabelHashBuilder hashBuilder = default;
            for (int i = 0; i < _bucketingStrategies.Length; i++)
            {
                _queryParameters[i] = _bucketingStrategies[i].GetBucketLabel(player);
                hashBuilder.Add(_queryParameters[i]);
            }
            _queryHash = hashBuilder.Build();
        }

        /// <summary>
        /// Build a bucket query that matches the given matchmaker query.
        /// Sets _queryParameters and _queryHash.
        /// Also calls <see cref="BeginQuery"/> implicitly.
        /// </summary>
        void BuildQuery(TMatchmakerQuery query)
        {
            BeginQuery();
            LabelHashBuilder hashBuilder = default;
            for (int i = 0; i < _bucketingStrategies.Length; i++)
            {
                _queryParameters[i] =  _bucketingStrategies[i].GetBucketLabel(query);
                hashBuilder.Add(_queryParameters[i]);
            }
            _queryHash = hashBuilder.Build();
        }

        /// <summary>
        /// Creates a new bucket for the ongoing query.
        /// Only called if an existing bucket is not found.
        /// </summary>
        AsyncMatchmakerBucket<TMmPlayerModel> CreateNewBucketForQuery()
        {
            if (!_queryOnGoing)
                throw new InvalidOperationException("Begin a query first!");

            IBucketLabel[] labels = new IBucketLabel[NumQueryParams];
            _queryParameters.CopyTo(labels.AsSpan());

            AsyncMatchmakerBucket<TMmPlayerModel> newBucket = _createBucketFunc(labels);

            if (_buckets.TryGetValue(_queryHash, out AsyncMatchmakerBucket<TMmPlayerModel>[] oldBuckets)) // Append
                _buckets[_queryHash] = oldBuckets.Append(newBucket).ToArray();
            else // Add new
                _buckets.Add(_queryHash, new[] { newBucket });

            AddQueryLabels();

            return newBucket;
        }

        /// <summary>
        /// Try to get a bucket that exactly matches the ongoing query.
        /// Uses _queryHash and _queryParameters to verify.
        /// </summary>
        AsyncMatchmakerBucket<TMmPlayerModel> TryGetQueryMatch()
        {
            if (!_queryOnGoing)
                throw new InvalidOperationException("Begin a query first!");

            if (_buckets.TryGetValue(_queryHash, out AsyncMatchmakerBucket<TMmPlayerModel>[] possibleBuckets))
            {
                foreach (AsyncMatchmakerBucket<TMmPlayerModel> possibleBucket in possibleBuckets)
                {
                    if (possibleBucket.LabelsEquals(_queryParameters))
                        return possibleBucket;
                }
            }
            return null;
        }

        /// <summary>
        /// Change _queryHash and _queryParameters to match updated _queryLabelIndices.
        /// </summary>
        void UpdateQueryToIndex()
        {
            if (!_queryOnGoing)
                throw new InvalidOperationException("Begin a query first!");


            LabelHashBuilder hashBuilder = default;
            for (int i = 0; i < NumQueryParams; i++)
            {
                if (_queryLabelIndices[i] < 0 || _queryLabelIndices[i] >= _allLabels[i].Count)
                    _queryParameters[i] = null;
                else
                    _queryParameters[i] = _allLabels[i][_queryLabelIndices[i]];

                hashBuilder.Add(_queryParameters[i]);
            }
            _queryHash = hashBuilder.Build();
        }

        /// <summary>
        /// Get a bucket for a player. If an exact match is not found, a new bucket is created.
        /// </summary>
        public AsyncMatchmakerBucket<TMmPlayerModel> GetBucketForPlayer(TMmPlayerModel player)
        {
            try
            {
                BuildQuery(player);
                return TryGetQueryMatch() ?? CreateNewBucketForQuery();
            }
            finally
            {
                EndQuery();
            }
        }

        /// <summary>
        /// Returns an IEnumerable that enumerates buckets starting from the
        /// exact match, continuing outward based on the distance from the original label.
        /// Remember to call <see cref="EndQuery"/> after done.
        /// </summary>
        public IEnumerable<AsyncMatchmakerBucket<TMmPlayerModel>> QueryBuckets(TMatchmakerQuery query)
        {
            int IndexDistanceToQuery(AsyncMatchmakerBucket<TMmPlayerModel> bucket)
            {
                int distance = 0;
                for (int i = 0; i < NumQueryParams; i++)
                {
                    bool isEquals = bucket.Labels[i].Equals(_queryParameters[i]);
                    // Check if missing hard requirement
                    if (_bucketingStrategies[i].IsHardRequirement && !isEquals)
                        return -1;

                    if (_bucketingStrategies[i].StrategyType == BucketingStrategyType.Range)
                    {
                        int index = _allLabels[i].IndexOf(bucket.Labels[i]);
                        distance += Math.Abs(index - _queryLabelIndices[i]);
                    }
                    else if(!isEquals)
                        distance += 2; // Distinct different labels have same distance
                }
                return distance;
            }

            BuildQuery(query);
            AsyncMatchmakerBucket<TMmPlayerModel> exactMatch = TryGetQueryMatch();

            if (exactMatch != null)
                yield return exactMatch;

            InitializeQueryIndices();

            AsyncMatchmakerBucket<TMmPlayerModel> lowBucket = null;
            AsyncMatchmakerBucket<TMmPlayerModel> highBucket = null;

            // If mmr strategy exists, set low and high (cheat) buckets.
            if (_mmrStrategyIndex != -1)
            {
                _queryLabelIndices[_mmrStrategyIndex] -= 1;
                UpdateQueryToIndex();
                lowBucket = TryGetQueryMatch();
                _queryLabelIndices[_mmrStrategyIndex] += 2;
                UpdateQueryToIndex();
                highBucket = TryGetQueryMatch();
                _queryLabelIndices[_mmrStrategyIndex] -= 1;
                UpdateQueryToIndex();
            }

            if (lowBucket != null)
                yield return lowBucket;
            if (highBucket != null)
                yield return highBucket;

            // Sort rest of the buckets by index distance to the query indices.
            IEnumerable<(AsyncMatchmakerBucket<TMmPlayerModel> bucket, int)> sorted = AllBuckets.Select(
                bucket => (bucket, distance: IndexDistanceToQuery(bucket) ))
                .Where(bld => bld.Item2 != -1 // Prune any that do not match hard requirement
                    && bld.bucket != exactMatch
                    && bld.bucket != lowBucket
                    && bld.bucket != highBucket)
                .OrderBy(bld => bld.distance);

            foreach ((AsyncMatchmakerBucket<TMmPlayerModel> bucket, _) in sorted)
                yield return bucket;
        }

        public void BeginQuery()
        {
            if (_queryOnGoing)
                throw new InvalidOperationException("Finish previous query first!");

            _queryOnGoing = true;
        }

        public void EndQuery()
        {
            if (!_queryOnGoing)
                throw new InvalidOperationException("Begin a query first!");

            _queryOnGoing = false;
        }

        /// <summary>
        /// Prunes any empty buckets.
        /// </summary>
        public void PruneEmptyBuckets()
        {
            int[] keys = _buckets.Keys.ToArray();
            foreach (int key in keys)
            {
                AsyncMatchmakerBucket<TMmPlayerModel>[] bucketArr = _buckets[key];
                if (bucketArr.Any(x => x.Count == 0))
                {
                    bucketArr = bucketArr.Where(x => x.Count > 0).ToArray();
                    if (bucketArr.Length == 0)
                        _buckets.Remove(key);
                    else
                        _buckets[key] = bucketArr;
                }
            }
        }

        /// <summary>
        /// Sets query label indices to match the ongoing query.
        /// These indices are an index into the <see cref="_allLabels"/>
        /// List.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        void InitializeQueryIndices()
        {
            if (!_queryOnGoing)
                throw new InvalidOperationException("Begin a query first!");

            for (int i = 0; i < NumQueryParams; i++)
            {
                IBucketLabel label = _queryParameters[i];
                _queryLabelIndices[i] = _allLabels[i].IndexOf(label);

                if (_queryLabelIndices[i] == -1 && !_bucketingStrategies[i].IsHardRequirement)
                    _queryLabelIndices[i] = 0;
            }
        }

        /// <summary>
        /// Add all new labels in the current query to the <see cref="_allLabels"/> list.
        /// Ranged bucketing strategy labels are sorted afterwards.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        void AddQueryLabels()
        {
            if (!_queryOnGoing)
                throw new InvalidOperationException("Begin a query first!");

            for (int i = 0; i < NumQueryParams; i++)
            {
                IBucketLabel label = _queryParameters[i];

                if (_allLabels[i].Contains(label))
                    continue;

                _allLabels[i].Add(label);

                if (_bucketingStrategies[i].StrategyType == BucketingStrategyType.Range)
                {
                    _allLabels[i].Sort((label1, label2) =>
                        ((IOrderedBucketLabel)label1).CompareTo(label2));
                }
            }
        }
    }
}
