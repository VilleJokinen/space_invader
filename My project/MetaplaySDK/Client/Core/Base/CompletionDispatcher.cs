// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    /// <summary>
    /// Helper for dispatching async completion results to both Callback delegate and
    /// Task-based completion handlers.
    /// </summary>
    public class CompletionDispatcher<T>
    {
        Action<T> _cb;
        TaskCompletionSource<T> _tcs;

        public CompletionDispatcher()
        {
        }

        /// <summary>
        /// Adds Callback delegate that is called once when operation completes.
        /// </summary>
        public void RegisterAction(Action<T> cb)
        {
            _cb += cb;
        }

        /// <summary>
        /// Gets Task that completes when the operation completes.
        /// </summary>
        public Task<T> GetTask()
        {
            if (_tcs == null)
                _tcs = new TaskCompletionSource<T>();
            return _tcs.Task;
        }

        /// <summary>
        /// Cancels any Tasks, and forgets any Action delegates. Dispatcher is reset to initial state.
        /// </summary>
        public void Cancel()
        {
            _cb = null;
            _tcs?.SetCanceled();
            _tcs = null;
        }

        /// <summary>
        /// Invokes all Action delegates with the value and completes any Tasks with the value. Dispatcher is reset to initial state.
        /// </summary>
        public void Dispatch(T value)
        {
            _cb?.Invoke(value);
            _cb = null;
            _tcs?.SetResult(value);
            _tcs = null;
        }

        /// <summary>
        /// Invokes all Action delegates with the value and Faults any Tasks with the exception. Dispatcher is reset to initial state.
        /// </summary>
        public void DispatchAndThrow(T value, Exception ex)
        {
            _cb?.Invoke(value);
            _cb = null;
            _tcs?.SetException(ex);
            _tcs = null;
        }
    }
}
