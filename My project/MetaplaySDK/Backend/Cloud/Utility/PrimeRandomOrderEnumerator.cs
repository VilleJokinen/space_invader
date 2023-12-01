// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

namespace Metaplay.Cloud.Utility
{
    /// <inheritdoc cref="PrimeRandomOrderEnumerator{T}"/>
    public class PrimeRandomOrderEnumerable<T> : IEnumerable<T>
    {
        readonly IList<T> _source;

        public PrimeRandomOrderEnumerable(IList<T> source)
        {
            _source = source;
        }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
            => new PrimeRandomOrderEnumerator<T>(_source);

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Iterates the members of a provided IList starting from a random index, continuing in a "pseudo-random" order.
    /// The actual "randomness" of it comes from skipping a number of items after each iteration and moduloing the iterator by the list count.
    /// The skip value is selected from a list of primes, so that the iterator will not return to the same slot again before every slot is iterated.
    /// </summary>
    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    public class PrimeRandomOrderEnumerator<T> : IEnumerator<T>
    {
        static readonly ThreadLocal<RandomPCG> _random = new ThreadLocal<RandomPCG>(() => RandomPCG.CreateNew());
        static readonly long[] _primes =
        {
            6803053861L,
            7114860299L,
            8515217443L,
            8067783043L,
            9969065297L,
            352797915011L,
            227769785641L,
            404911944739L,
            583163715697L,
            944200273903L,
            708422115743L,
            904925389937L,
            195852041687L,
            336096768101L,
            180075945079L,
            636372886801L,
            471226315487L,
            785074517527L,
            284658657659L,
            293143801501L,
            722603369393L,
            795380543921L,
            144280684423L,
            728040254863L,
            152961642697L,
            996614817893L,
            965758882553L,
            779882552011L,
            681311890781L,
            143548755991L,
        };

        readonly IList<T> _source;
        readonly long     _prime;
        long              _currentIdx;
        int               _iterator;

        public PrimeRandomOrderEnumerator(IList<T> source)
        {
            _iterator = -1;
            _source   = source;

            if (_source.Count > 0)
            {
                _currentIdx = _random.Value.NextInt(_source.Count);
                _prime      = _random.Value.Choice(_primes);
            }
            else
            {
                _currentIdx = 0;
                _prime      = 1;
            }
        }

        public bool MoveNext()
        {
            if (++_iterator >= _source.Count)
                return false;

            _currentIdx += _prime;
            _currentIdx %= _source.Count;

            return true;
        }

        public void Reset()
        {
            _currentIdx = _random.Value.NextInt(_source.Count);
            _iterator   = -1;
        }

        public T           Current => _source[(int)_currentIdx];
        object IEnumerator.Current => Current;

        public void Dispose() { }
    }
}
