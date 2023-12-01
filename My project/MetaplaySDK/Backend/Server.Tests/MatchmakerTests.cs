// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.Matchmaking;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Server.Tests
{
    [TestFixture]
    class MatchmakerTests
    {
        public class TestMatchmakerOptions : AsyncMatchmakerOptionsBase
        {
            public TestMatchmakerOptions()
            {
                MmrBucketCount    = 2;
                BucketInitialSize = 100;
                InitialMinMmr     = 0;
                InitialMaxMmr     = 10;
            }
        }

        [MetaSerializable]
        public class TestMatchHardRequirement : StringId<TestMatchHardRequirement>
        {
        }

        [MetaSerializableDerived(1000)]
        public class TestBucketingStrategyLabel : IDistinctBucketLabel<TestBucketingStrategyLabel>
        {
            [MetaMember(1)] public TestMatchHardRequirement Requirement { get; set; }

            public string DashboardLabel => "";

            public TestBucketingStrategyLabel() { }

            public TestBucketingStrategyLabel(TestMatchHardRequirement requirement)
            {
                Requirement = requirement;
            }

            public bool Equals(TestBucketingStrategyLabel other)
            {
                if (ReferenceEquals(null, other))
                    return false;
                if (ReferenceEquals(this, other))
                    return true;

                return Equals(Requirement, other.Requirement);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                if (ReferenceEquals(this, obj))
                    return true;
                if (obj.GetType() != typeof(TestBucketingStrategyLabel))
                    return false;

                return Equals((TestBucketingStrategyLabel)obj);
            }

            public override int GetHashCode()
            {
                return (Requirement != null ? Requirement.GetHashCode() : 0);
            }
        }

        [MetaSerializable]
        public struct TestAsyncMatchmakerPlayerModel : IAsyncMatchmakerPlayerModel
        {
            [MetaMember(1)] public EntityId                 PlayerId    { get; set; }
            [MetaMember(2)] public int                      DefenseMmr  { get; set; }
            [MetaMember(3)] public TestMatchHardRequirement Requirement { get; set; }
        }

        [MetaSerializableDerived(1000)]
        public class TestAsyncMatchmakerQuery : AsyncMatchmakerQueryBase
        {
            [MetaMember(1)] public TestMatchHardRequirement Requirement { get; set; }
        }


        public class TestHardRequirementBucketingStrategy : AsyncMatchmakerBucketingStrategyBase<
            TestBucketingStrategyLabel,
            TestAsyncMatchmakerPlayerModel,
            TestAsyncMatchmakerQuery>
        {
            /// <inheritdoc />
            public override bool IsHardRequirement => true;

            /// <inheritdoc />
            public override TestBucketingStrategyLabel GetBucketLabel(TestAsyncMatchmakerPlayerModel model)
            {
                return new TestBucketingStrategyLabel(model.Requirement);
            }

            /// <inheritdoc />
            public override TestBucketingStrategyLabel GetBucketLabel(TestAsyncMatchmakerQuery query)
            {
                return new TestBucketingStrategyLabel(query.Requirement);
            }
        }

        static readonly TestMatchHardRequirement _requirement1 = TestMatchHardRequirement.FromString("One");
        static readonly TestMatchHardRequirement _requirement2 = TestMatchHardRequirement.FromString("Two");
        static readonly TestMatchHardRequirement _requirement3 = TestMatchHardRequirement.FromString("Three");

        static readonly TestAsyncMatchmakerPlayerModel[] _testPlayers = new[]
        {
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId = EntityId.Create(EntityKindCore.Player, 0),
                DefenseMmr = 0,
                Requirement = _requirement1,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 1),
                DefenseMmr  = 1,
                Requirement = _requirement2,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 2),
                DefenseMmr  = 2,
                Requirement = _requirement3,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 3),
                DefenseMmr  = 3,
                Requirement = _requirement1,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 4),
                DefenseMmr  = 4,
                Requirement = _requirement2,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 5),
                DefenseMmr  = 5,
                Requirement = _requirement3,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 6),
                DefenseMmr  = 6,
                Requirement = _requirement1,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 7),
                DefenseMmr  = 7,
                Requirement = _requirement2,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 8),
                DefenseMmr  = 8,
                Requirement = _requirement3,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 9),
                DefenseMmr  = 9,
                Requirement = _requirement1,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 10),
                DefenseMmr  = 10,
                Requirement = _requirement2,
            },
            new TestAsyncMatchmakerPlayerModel
            {
                PlayerId    = EntityId.Create(EntityKindCore.Player, 11),
                DefenseMmr  = 11,
                Requirement = _requirement3,
            },
        };

        static readonly TestAsyncMatchmakerQuery[] _testQueries = new[]
        {
            new TestAsyncMatchmakerQuery()
            {
                AttackerId = EntityId.Create(EntityKindCore.Player, 99999),
                AttackMmr = 1,
                Requirement = _requirement1,
            },
            new TestAsyncMatchmakerQuery()
            {
                AttackerId  = EntityId.Create(EntityKindCore.Player, 99999),
                AttackMmr   = 2,
                Requirement = _requirement2,
            },
            new TestAsyncMatchmakerQuery()
            {
                AttackerId  = EntityId.Create(EntityKindCore.Player, 99999),
                AttackMmr   = 3,
                Requirement = _requirement3,
            },
            new TestAsyncMatchmakerQuery()
            {
                AttackerId  = EntityId.Create(EntityKindCore.Player, 99999),
                AttackMmr   = 9,
                Requirement = _requirement1,
            },
            new TestAsyncMatchmakerQuery()
            {
                AttackerId  = EntityId.Create(EntityKindCore.Player, 99999),
                AttackMmr   = 7,
                Requirement = _requirement2,
            },
            new TestAsyncMatchmakerQuery()
            {
                AttackerId  = EntityId.Create(EntityKindCore.Player, 99999),
                AttackMmr   = 8,
                Requirement = _requirement3,
            },
        };

        AsyncMatchmakerBucketPool<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery> _bucketPool;

        List<int> _bucketPoolCreateCallList;

        TestMatchmakerOptions _options;

        IAsyncMatchmakerBucketingStrategy<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery>[] _bucketingStrategies;

        [SetUp]
        public void Init()
        {
            _bucketingStrategies = new IAsyncMatchmakerBucketingStrategy<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery>[]
            {
                new MmrBucketingStrategy<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery>(),
                new TestHardRequirementBucketingStrategy(),
            };
            _bucketPool               = new AsyncMatchmakerBucketPool<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery>(_bucketingStrategies, CreateNewBucketFunc);
            _bucketPoolCreateCallList = new List<int>();

            _options = new TestMatchmakerOptions();

            foreach (IAsyncMatchmakerBucketingStrategy<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery> bucketingStrategy in _bucketingStrategies)
            {
                bucketingStrategy.State = bucketingStrategy.InitializeNew(_options);
                bucketingStrategy.PostLoad(_options);
            }
        }

        public AsyncMatchmakerBucket<TestAsyncMatchmakerPlayerModel> CreateNewBucketFunc(IBucketLabel[] labels)
        {
            _bucketPoolCreateCallList.Add(labels.HashLabels());
            return new AsyncMatchmakerBucket<TestAsyncMatchmakerPlayerModel>(100, labels);
        }

        public void AddAllTestPlayers()
        {
            foreach (TestAsyncMatchmakerPlayerModel playerModel in _testPlayers)
            {
                AsyncMatchmakerBucket<TestAsyncMatchmakerPlayerModel> bucket = _bucketPool.GetBucketForPlayer(playerModel);

                // Set HashSeed to not fail randomly
                bucket.HashSeed = 1234567;

                Assert.AreEqual(null, bucket.InsertOrReplace(playerModel));
            }

            AssertBucketCreateList(6);
        }

        public void AssertBucketCreateList(int expectedBuckets)
        {
            Assert.That(_bucketPoolCreateCallList, Is.Unique);
            Assert.AreEqual(expectedBuckets, _bucketPoolCreateCallList.Count);
        }

        [Test]
        public void TestAddPlayers()
        {
            AddAllTestPlayers();
            Assert.AreEqual(12, _bucketPool.AllBuckets.Sum(bucket => bucket.Count));
            Assert.AreEqual(6, _bucketPool.AllBuckets.Count());

            Assert.That(_bucketPool.AllBuckets.SelectMany(x => x.Values), Is.EquivalentTo(_testPlayers));
        }

        [TestCase(0, 0, "One")]
        [TestCase(1, 1, "Two")]
        [TestCase(2, 2, "Three")]
        [TestCase(3, 6, "One")]
        [TestCase(4, 7, "Two")]
        [TestCase(5, 8, "Three")]
        public void TestQueryFromPool(int queryIdx, int expectedPlayerIdx, string expectedRequirement)
        {
            AddAllTestPlayers();
            TestAsyncMatchmakerQuery        query            = _testQueries[queryIdx];
            TestAsyncMatchmakerPlayerModel  expectedPlayer   = _testPlayers[expectedPlayerIdx];
            TestMatchHardRequirement   requirement      = TestMatchHardRequirement.FromString(expectedRequirement);
            TestBucketingStrategyLabel requirementLabel = new TestBucketingStrategyLabel(requirement);

            AsyncMatchmakerBucket<TestAsyncMatchmakerPlayerModel>[] buckets = _bucketPool.QueryBuckets(query).ToArray();
            _bucketPool.EndQuery();

            AsyncMatchmakerBucket<TestAsyncMatchmakerPlayerModel>[] expectedBuckets = _bucketPool.AllBuckets
                .Where(x => x.Labels.Contains(requirementLabel)).ToArray();

            Assert.That(buckets[0], Has.Member(expectedPlayer));

            Assert.That(buckets.SelectMany(b => b.Values), Is.EquivalentTo(
                _testPlayers.Where(player => player.Requirement.Equals(requirement))));

            Assert.That(buckets, Is.EquivalentTo(expectedBuckets));


            Assert.That(buckets, Is.Unique);
            Assert.That(buckets.SelectMany(b => b.Values), Is.Unique);
        }

        [TestCase(0, "One")]
        [TestCase(1, "Two")]
        [TestCase(2, "Three")]
        [TestCase(3, "One")]
        [TestCase(4, "Two")]
        [TestCase(5, "Three")]
        public void TestQueryWithBucketWalker(int queryIdx, string expectedRequirement)
        {
            AddAllTestPlayers();
            TestAsyncMatchmakerQuery        query            = _testQueries[queryIdx];
            TestMatchHardRequirement   requirement      = TestMatchHardRequirement.FromString(expectedRequirement);

            AsyncMatchmakerBucketWalker<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery> walker = new AsyncMatchmakerBucketWalker<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery>(_bucketPool, query);

            TestAsyncMatchmakerPlayerModel[] players = walker.ToArray();

            Assert.That(players, Is.EquivalentTo(
                _testPlayers.Where(player => player.Requirement.Equals(requirement))));

            Assert.That(players, Is.Unique);
        }

        [TestCase("One")]
        [TestCase("Two")]
        [TestCase("Three")]
        [TestCase(null)]
        public void TestQueryOutsideRange(string expectedRequirement)
        {
            TestMatchHardRequirement   requirement      = TestMatchHardRequirement.FromString(expectedRequirement);

            AddAllTestPlayers();
            TestAsyncMatchmakerQuery        query            = new TestAsyncMatchmakerQuery()
            {
                AttackerId = EntityId.None,
                AttackMmr = 99999,
                Requirement = requirement,
            };

            AsyncMatchmakerBucketWalker<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery> walker = new AsyncMatchmakerBucketWalker<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery>(_bucketPool, query);

            TestAsyncMatchmakerPlayerModel[] players = walker.ToArray();

            Assert.That(players, Is.EquivalentTo(
                _testPlayers.Where(player => player.Requirement.Equals(requirement))));

            Assert.That(players, Is.Unique);
        }

        [Test]
        public void TestMmrRangeRebalance()
        {
            TestAsyncMatchmakerPlayerModel highMmrPlayer = new TestAsyncMatchmakerPlayerModel()
            {
                PlayerId    = EntityId.CreateRandom(EntityKindCore.Player),
                DefenseMmr  = 1000,
                Requirement = _requirement1,
            };
            TestAsyncMatchmakerPlayerModel lowMmrPlayer = new TestAsyncMatchmakerPlayerModel()
            {
                PlayerId    = EntityId.CreateRandom(EntityKindCore.Player),
                DefenseMmr  = 400,
                Requirement = _requirement1,
            };

            MmrBucketingStrategy<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery> mmrStrategy =
                _bucketingStrategies.OfType<MmrBucketingStrategy<TestAsyncMatchmakerPlayerModel, TestAsyncMatchmakerQuery>>().FirstOrDefault();

            Assert.AreEqual(mmrStrategy.State.MmrLow, _options.InitialMinMmr);
            Assert.AreEqual(mmrStrategy.State.MmrHigh, _options.InitialMaxMmr);

            mmrStrategy.CollectSample(highMmrPlayer);
            mmrStrategy.CollectSample(lowMmrPlayer);

            Assert.AreEqual(mmrStrategy.State.MmrLow, _options.InitialMinMmr);
            Assert.AreEqual(mmrStrategy.State.MmrHigh, _options.InitialMaxMmr);

            mmrStrategy.OnRebalance(_options);

            Assert.AreEqual(mmrStrategy.State.MmrLow, lowMmrPlayer.DefenseMmr);
            Assert.AreEqual(mmrStrategy.State.MmrHigh, highMmrPlayer.DefenseMmr);
        }
    }
}
