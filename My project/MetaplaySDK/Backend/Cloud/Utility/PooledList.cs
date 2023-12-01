// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// A simple list that uses <see cref="ArrayPool{T}"/> to not allocate. Remember to dispose!
    /// Create one using the static <see cref="PooledList{TListItem}.Create"/> method.
    /// Can be used for algorithms that require allocating temporary short-lived lists
    /// when long GC times might become a problem.
    /// </summary>
    public sealed class PooledList<TListItem> : IList<TListItem>, IList, IReadOnlyList<TListItem>, IDisposable
    {
        static readonly ArrayPool<TListItem> _pool = ArrayPool<TListItem>.Shared; // Is thread-safe

        const int DefaultCapacity = 4;

        TListItem[]            _arr;
        public int             Count    { get; private set; }
        public int             Capacity => _arr.Length;
        public Span<TListItem> Span     => new(_arr, 0, Count);

        bool ICollection.           IsSynchronized => false;
        object ICollection.         SyncRoot       => this;
        bool ICollection<TListItem>.IsReadOnly     => false;
        bool IList.                 IsReadOnly     => false;
        bool IList.                 IsFixedSize    => false;

        public TListItem this[int index]
        {
            get
            {
                EnsureArrSet();
                if (index >= Count || index < 0)
                    throw new IndexOutOfRangeException();

                return _arr[index];
            }
            set
            {
                EnsureArrSet();
                if (index >= Count || index < 0)
                    throw new IndexOutOfRangeException();

                _arr[index] = value;
            }
        }

        object IList.this[int index]
        {
            get => this[index];
            set => this[index] = (TListItem)value;
        }

        /// <summary>
        /// Create a new <see cref="PooledList{TListItem}"/> with the specified capacity.
        /// </summary>
        public static PooledList<TListItem> Create(int capacity = DefaultCapacity)
            => new(capacity);

        PooledList(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentException("Capacity cannot be less than 0!");

            _arr  = _pool.Rent(capacity);
            Count = 0;
        }

        /// <summary>
        /// Add an item to the end of the list.
        /// </summary>
        /// <param name="item"></param>
        public void Add(in TListItem item)
        {
            EnsureArrSet();
            EnsureCapacityForAdd();

            _arr[Count++] = item;
        }

        /// <inheritdoc />
        public int IndexOf(TListItem item)
        {
            EqualityComparer<TListItem> comparer = EqualityComparer<TListItem>.Default;
            for (int i = 0; i < Count; i++)
            {
                if (comparer.Equals(_arr[i], item))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Inserts an item at the specified index, moving the existing item to the end of the list.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Insert(int index, TListItem item)
        {
            EnsureArrSet();
            EnsureCapacityForAdd();
            if (index > Count || index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "Cannot insert into an index that doesn't exist");

            _arr[Count] = _arr[index];
            _arr[index] = item;

            Count++;
        }

        /// <summary>
        /// Sort the elements in the list using the <see cref="IComparable{T}"/> implementation of the item type.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Sort() => Span.Sort();

        /// <summary>
        /// Sort the elements in the list using the specified <see cref="Comparison{T}"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void Sort(Comparison<TListItem> comparison) => Span.Sort(comparison);

        /// <summary>
        /// Removes the item, replacing it with the last item in the list.
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        void IList.Remove(object value)
        {
            EnsureArrSet();
            int index = (this as IList).IndexOf(value);
            if (index != -1)
                RemoveAt(index);
        }

        /// <summary>
        /// Removes the item at the specified index, replacing it with the last item in the list.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void RemoveAt(int index)
        {
            EnsureArrSet();
            if (index >= Count || index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "Cannot remove an element that doesn't exist");

            if (index == Count - 1)
                _arr[index] = default;
            else
            {
                _arr[index]     = _arr[Count - 1];
                _arr[Count - 1] = default;
            }

            Count--;
        }

        void ICollection<TListItem>.Add(TListItem item) => this.Add(item);

        /// <exception cref="InvalidCastException"></exception>
        int IList.Add(object value)
        {
            this.Add((TListItem)value);
            return Count - 1;
        }

        /// <inheritdoc cref="IList.Clear"/>
        public void Clear()
        {
            EnsureArrSet();
            _arr.AsSpan(0, Count).Fill(default);
            Count = 0;
        }

        /// <inheritdoc cref="IList.Contains"/>
        /// <exception cref="InvalidCastException"></exception>
        bool IList.Contains(object value) => this.Contains((TListItem)value);

        /// <inheritdoc cref="IList.IndexOf"/>
        /// <exception cref="InvalidCastException"></exception>
        int IList.IndexOf(object value) => this.IndexOf((TListItem)value);

        /// <inheritdoc cref="IList.Insert"/>
        /// <exception cref="InvalidCastException"></exception>
        void IList.Insert(int index, object value) => this.Insert(index, (TListItem)value);

        /// <inheritdoc cref="ICollection{T}.Contains"/>
        public bool Contains(TListItem item) => IndexOf(item) != -1;

        /// <inheritdoc cref="ICollection{T}.CopyTo"/>
        public void CopyTo(TListItem[] array, int arrayIndex)
        {
            EnsureArrSet();
            Array.Copy(_arr, 0, array, arrayIndex, Count);
        }

        /// <inheritdoc cref="ICollection.CopyTo"/>
        public void CopyTo(Array array, int index)
        {
            EnsureArrSet();
            Array.Copy(_arr, 0, array, index, Count);
        }

        /// <summary>
        /// Removes the item, replacing it with the last item in the list.
        /// </summary>
        /// <returns>true if item was removed.</returns>
        public bool Remove(TListItem item)
        {
            EnsureArrSet();

            int index = IndexOf(item);
            if (index != -1)
            {
                RemoveAt(index);
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_arr != null)
            {
                _pool.Return(_arr);
                _arr = null;
            }
            #if DEBUG
            GC.SuppressFinalize(this);
        }

        [SuppressMessage("Performance", "CA1821:Remove empty Finalizers", Justification = "Just for debug purposes.")]
        ~PooledList()
        {
            throw new NotSupportedException($"Always remember to dispose a {nameof(PooledList<TListItem>)}!");
            #endif
        }

        public SimpleArrayEnumerator<TListItem> GetEnumerator()
        {
            EnsureArrSet();
            return new SimpleArrayEnumerator<TListItem>(_arr, Count);
        }

        IEnumerator<TListItem> IEnumerable<TListItem>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void EnsureCapacityForAdd()
        {
            if (Count == Capacity)
                Grow();
        }

        void Grow()
        {
            EnsureArrSet();
            if (Count != Capacity)
                throw new InvalidOperationException("Grow() should only be called when Count == Capacity!");

            int         newCapacity = Capacity == 0 ? DefaultCapacity : Capacity * 2;
            TListItem[] newArray    = _pool.Rent(newCapacity);
            Array.Copy(_arr, newArray, Count);
            _pool.Return(_arr, true);
            _arr = newArray;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        void EnsureArrSet()
        {
            MetaDebug.Assert(_arr != null, $"{nameof(PooledList<TListItem>)} needs to created with the static {nameof(Create)} method.");
        }

        /// <summary>
        /// A simple enumerator that iterates an array sequentially.
        /// </summary>
        public struct SimpleArrayEnumerator<TItem> : IEnumerator<TItem>
        {
            readonly TItem[] _arr;
            readonly int     _count;
            int              _it;

            public SimpleArrayEnumerator(TItem[] arr, int count) : this()
            {
                _arr   = arr;
                _count = count;
                _it    = -1;
            }

            public bool MoveNext()
            {
                _it++;
                return _it < _count;
            }

            public void Reset()
            {
                _it = -1;
            }

            public TItem Current => _arr[_it];

            object IEnumerator.Current => Current;

            public void Dispose() { }
        }
    }
}
