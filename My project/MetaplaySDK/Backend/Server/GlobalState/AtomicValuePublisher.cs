// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Threading;

namespace Metaplay.Server
{
    /// <summary>
    /// Represents an immutable value which can be used to efficiently broadcast values to multiple
    /// actors without worrying about thread-safety issues. Mainly used by <see cref="GlobalStateProxyActor"/>
    /// to broadcast values from <see cref="GlobalStateManager"/> to multiple actors (eg, <see cref="PlayerActor"/>s)
    /// on each node.
    ///
    /// Note that the class implementing IAtomicValue must implement <see cref="IEquatable{T}"/>, which is
    /// used to detect when values actually change.
    /// </summary>
    /// <typeparam name="T">Type of the class implementing IAtomicValue</typeparam>
    public interface IAtomicValue<T> : IEquatable<T> where T : class, IAtomicValue<T>
    {
    }

    /// <summary>
    /// Publisher of atomically changing immutable values. Can be used to broadcast changing values in a
    /// thread-safe manner to multiple subscribers. Mainly used by <see cref="GlobalStateProxyActor"/>
    /// to broadcast data to all <see cref="PlayerActor"/>s.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AtomicValuePublisher<T> where T : class, IAtomicValue<T>
    {
        private T _current;

        public AtomicValuePublisher() { }
        public AtomicValuePublisher(T value) { _current = value; }

        public void TryUpdate(T value)
        {
            if (_current == null || !_current.Equals(value))
                Update(value);
        }

        public void Update(T value) => Volatile.Write(ref _current, value);
        public T Get() => Volatile.Read(ref _current);

        public AtomicValueSubscriber<T> Subscribe() => new AtomicValueSubscriber<T>(this);
    }

    /// <summary>
    /// Subscribes to a <see cref="AtomicValuePublisher{T}"/> and allows for detecting and handling
    /// any changes to the subscribed-to data.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AtomicValueSubscriber<T> where T : class, IAtomicValue<T>
    {
        private AtomicValuePublisher<T> _publisher;
        public T                        Current { get; private set; }

        public AtomicValueSubscriber(AtomicValuePublisher<T> publisher)
        {
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        public void Update(Action<T, T> updateFunc)
        {
            // If value has changed, call the update handler
            T value = _publisher.Get();
            if (!ReferenceEquals(value, Current))
            {
                if (value != null)
                    updateFunc(value, Current);
                Current = value;
            }
        }
    }
}
