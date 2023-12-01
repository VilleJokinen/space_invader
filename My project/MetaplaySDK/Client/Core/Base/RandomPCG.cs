// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core
{
    /// <summary>
    /// PCG Random number generator. Provides great statistical qualities with a tiny memory footprint (64 bits).
    ///
    /// See: http://www.pcg-random.org
    /// </summary>
    [MetaSerializable]
    public class RandomPCG : IEquatable<RandomPCG>
    {
        const ulong     Multiplier  = 6364136223846793005ul;
        const ulong     Increment   = 1442695040888963407ul;
        const float     ToFloat01   = 1.0f / 4294967296.0f;
        const double    ToDouble01  = 1.0 / 4294967296.0;

        [MetaMember(1)] ulong _state;

        /// <summary>
        /// Construct without initialization, intended to be only used when deserializing.
        /// </summary>
        private RandomPCG()
        {
            _state = 0;
        }

        /// <summary>
        /// Initialize random number generator with the given seed.
        /// </summary>
        /// <param name="seed">Seed to use for initialization</param>
        private RandomPCG(ulong seed)
        {
            _state = 0;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>
        /// Clone another random generator.
        /// </summary>
        public RandomPCG(RandomPCG other)
        {
            _state = other._state;
        }

        /// <summary>
        /// Create a new instance with randomized initial seed. Each call will result in a new RandomPCG initialized with a
        /// different random seed. This method may be called in quick succession safely. As the results are non-deterministic,
        /// calling this method on server and on client will result in different results, so it is not safe to use in shared
        /// game logic code.
        /// </summary>
        public static RandomPCG CreateNew()
        {
            return new RandomPCG(GetRandomSeed());
        }

        /// <summary>
        /// Create new instnace using the given unsigned 64-bit seed.
        /// </summary>
        /// <param name="seed">Seed to use for initialization</param>
        public static RandomPCG CreateFromSeed(ulong seed)
        {
            return new RandomPCG(seed);
        }

        static ulong GetRandomSeed()
        {
            ulong upper = (ulong)(Environment.TickCount ^ Guid.NewGuid().GetHashCode()) << 32;
            ulong lower = (ulong)(Environment.TickCount ^ Guid.NewGuid().GetHashCode());
            return (upper | lower);
        }

        /// <summary>
        /// Returns a unsigned integer in range [0, 0xffffffff].
        /// </summary>
        /// <returns>Random unsigned integer</returns>
        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = unchecked(oldState * Multiplier + Increment);
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            uint result = (xorShifted >> rot) + (xorShifted << ((-rot) & 31));
            return result;
        }

        /// <summary>
        /// Returns a random non-negative 32-bit integer in range [0, 0x7fffffff].
        /// </summary>
        /// <returns>Random non-negative int</returns>
        public int NextInt()
        {
            return (int)(NextUInt() >> 1);
        }

        /// <summary>
        /// Get a random integer in range [0, maxExclusive[.
        /// </summary>
        /// <param name="maxExclusive">Exclusive maximum value, must be positive</param>
        /// <returns>Random integer in range [0, maxExclusive[</returns>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentException("Argument must be greater than zero", nameof(maxExclusive));

            // \note has some bias (depending on maxExclusive)
            return NextInt() % maxExclusive;
        }

        /// <summary>
        /// Returns a unsigned 64-bit long in range [0, 0xffffffff_ffffffff].
        /// </summary>
        /// <returns>Random unsigned long</returns>
        // \todo [petri] this concatenates two 32-bit randoms, not too sure on the quality
        public ulong NextULong()
        {
            return ((ulong)NextUInt() << 32) + NextUInt();
        }

        /// <summary>
        /// Returns a random non-negative 64-bit integer in range [0, 0x7fffffff_ffffffff].
        /// </summary>
        /// <returns>Random non-negative long</returns>
        // \todo [petri] this concatenates two 32-bit randoms, not too sure on the quality
        public long NextLong()
        {
            return (long)(NextULong() >> 1);
        }

        /// <summary>
        /// Returns a random float value in range [0.0, 1.0[.
        /// </summary>
        /// <returns>Random float value</returns>
        public float NextFloat()
        {
            return ToFloat01 * (float)NextUInt();
        }

        /// <summary>
        /// Returns a random double value in range [0.0, 1.0[.
        /// </summary>
        /// <returns>Random double value</returns>
        public double NextDouble()
        {
            return ToDouble01 * (double)NextUInt();
        }

        /// <summary>
        /// Get a random integer in range [minInclusive, maxExclusive[.
        /// </summary>
        /// <param name="minInclusive">Inclusive minimum value.</param>
        /// <param name="maxExclusive">Exclusive maximum value, must be larger than minInclusive.</param>
        /// <returns>Random integer in range [minInclusive, maxExclusive[</returns>
        public int NextIntMinMax(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentException("MaxExclusive must be greater than minInclusive", nameof(maxExclusive));

            return NextInt(maxExclusive - minInclusive) + minInclusive;
        }

        /// <summary>
        /// Returns a random F32 value in range [0.0, 1.0[.
        /// </summary>
        /// <returns>Random F32 value</returns>
        public F32 NextF32()
        {
            return F32.FromRaw((int)(NextUInt() & 0xFFFF));
        }

        /// <summary>
        /// Returns a random F64 value in range [0.0, 1.0[.
        /// </summary>
        /// <returns>Random F64 value</returns>
        public F64 NextF64()
        {
            return F64.FromRaw((long)(ulong)NextUInt());
        }

        /// <summary>
        /// Returns a random F64Vec2 vector inside a circle with radius of 1.
        /// </summary>
        /// <returns>Random F64Vec2 vector</returns>
        public F64Vec2 NextInsideUnitCircle()
        {
            F64 theta  = F64.Pi2 * NextF64();
            F64 radius = F64.Sqrt(NextF64());

            F64 x = radius * F64.Cos(theta);
            F64 y = radius * F64.Sin(theta);

            return new F64Vec2(x, y);
        }

        /// <summary>
        /// Get a random boolean value.
        /// </summary>
        /// <returns>True or false, with 50% chance</returns>
        public bool NextBool()
        {
            return NextUInt() >= 0x80000000u;
        }

        /// <summary>
        /// Choose a random element from a list of values and return it. Returns the default value for empty list.
        /// </summary>
        /// <typeparam name="T">Type of items in list</typeparam>
        /// <param name="list">The chosen value, or default if an empty collection was provided</param>
        /// <returns></returns>
        public T Choice<T>(IList<T> list)
        {
            if (list.Count > 0)
                return list[NextInt(list.Count)];
            else
                return default;
        }

        /// <summary>
        /// Choose a random element from a stream of values and return it. Use this method when you don't know
        /// the number of elements in the stream beforehand. Returns the default value for empty collection.
        /// </summary>
        /// <typeparam name="T">Type of items in collection</typeparam>
        /// <param name="coll">Collection to randomize element from</param>
        /// <returns>A random element from the collection, or default if an empty collection was provided</returns>
        public T Choice<T>(IEnumerable<T> coll)
        {
            T chosen = default;
            int ndx = 1;
            foreach (T elem in coll)
            {
                if (NextInt(ndx) == 0)
                    chosen = elem;
                ndx++;
            }

            return chosen;
        }

        /// <summary>
        /// Choose a random element in a stream of integer weights using weighted randomization and return the index of the chosen weight.
        /// </summary>
        /// <param name="weights">Stream of integer weights</param>
        /// <returns>The index of the chosen element, or -1 if weights is empty</returns>
        public int GetWeightedIndex(IEnumerable<int> weights)
        {
            int chosen = -1;
            int sum = 0;
            int ndx = 0;
            foreach (int weight in weights)
            {
                sum += weight;
                if (NextInt(sum) < weight)
                    chosen = ndx;
                ndx++;
            }
            return chosen;
        }

        /// <summary>
        /// Choose a random element in a stream of s16.16 fixed-point weights using weighted randomization and return the index of the chosen weight.
        /// </summary>
        /// <param name="weights">Stream of s16.16 fixed-point weights</param>
        /// <returns>The index of the chosen element, or -1 if weights is empty</returns>
        public int GetWeightedIndex(IEnumerable<F32> weights)
        {
            int chosen = -1;
            int sum = 0;
            int ndx = 0;
            foreach (F32 weight in weights)
            {
                int rawWeight = weight.Raw;
                sum += rawWeight;
                if (NextInt(sum) < rawWeight)
                    chosen = ndx;
                ndx++;
            }
            return chosen;
        }

        public void ShuffleInPlace<T>(T[] items)
        {
            int len = items.Length;
            for (int toNdx = 0; toNdx < len - 1; toNdx++)
            {
                int fromNdx = toNdx + NextInt(len - toNdx);
                T tmp = items[toNdx];
                items[toNdx] = items[fromNdx];
                items[fromNdx] = tmp;
            }
        }

        public void ShuffleInPlace<T>(IList<T> items)
        {
            int len = items.Count;
            for (int toNdx = 0; toNdx < len - 1; toNdx++)
            {
                int fromNdx = toNdx + NextInt(len - toNdx);
                T tmp = items[toNdx];
                items[toNdx] = items[fromNdx];
                items[fromNdx] = tmp;
            }
        }

        public override bool Equals(object obj) => (obj is RandomPCG other) ? (this == other) : false;
        public override int GetHashCode() => _state.GetHashCode();
        public override string ToString() => $"RandomPCG({_state})";

        public bool Equals(RandomPCG other) => _state == other._state;

        public static bool operator ==(RandomPCG a, RandomPCG b)
        {
            if (ReferenceEquals(a, b))
                return true;
            else if (a is null || b is null)
                return false;
            else
                return a._state == b._state;
        }

        public static bool operator !=(RandomPCG a, RandomPCG b) => !(a == b);
    }

    public static partial class EnumerableExtensions
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, RandomPCG rnd)
        {
            T[] array = source.ToArray();
            rnd.ShuffleInPlace(array);
            return array;
        }
    }
}
