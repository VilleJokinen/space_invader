// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Tasks
{
    /// <summary>
    /// An awaitable delay that uses <see cref="MetaTimer"/>s internally.
    /// This can be used in environments where <see cref="Task.Delay"/> does not work.
    /// </summary>
    public sealed class MetaDelay
    {
        public struct DelayAwaiter : ICriticalNotifyCompletion
        {
            readonly MetaDelay _delay;

            public DelayAwaiter(MetaDelay delay)
            {
                _delay = delay;
            }

            public bool IsCompleted => _delay.IsCompleted;

            public void OnCompleted(Action continuation)
            {
                if (IsCompleted)
                    continuation();
                else
                    _delay.OnFinished += continuation;
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                if (IsCompleted)
                    continuation();
                else
                    _delay.OnFinished += continuation;
            }

            public void GetResult()
            {
            }
        }
        readonly SynchronizationContext        _capturedContext;
        readonly CancellationTokenRegistration _registration;
        readonly MetaTimer                     _timer;

        event Action OnFinished;

        public DelayAwaiter GetAwaiter() => new DelayAwaiter(this);

        MetaDelay(TimeSpan delay, CancellationToken token)
        {
            _capturedContext = SynchronizationContext.Current;

            // don't create timer for infinite waits
            if (delay.TotalMilliseconds > 0)
                _timer = new MetaTimer(SetCompleted, this, delay, TimeSpan.FromMilliseconds(-1));

            if (token.CanBeCanceled)
                _registration = token.Register(SetCompleted, this, true);
        }

        public bool IsCompleted { get; private set; } = false;

        /// <summary>
        /// Called by the timer or CancellationToken when either completes.
        /// </summary>
        static void SetCompleted(object state)
        {
            MetaDelay delay = state as MetaDelay;
            if (delay == null)
                throw new NullReferenceException("Given delay was null!");

            try
            {
                if (delay.IsCompleted)
                    return;

                delay.IsCompleted = true;

#pragma warning disable VSTHRD001 // Avoid legacy thread switching APIs
                if (delay._capturedContext != null)
                    delay._capturedContext.Post(_ => delay.OnFinished?.Invoke(), null);
#pragma warning restore VSTHRD001 // Avoid legacy thread switching APIs
                else
                    delay.OnFinished?.Invoke();
            }
            finally
            {
                delay._timer?.Dispose();
                delay._registration.Dispose();
            }
        }

        public static Task Delay(TimeSpan delay)
        {
            return Delay(delay, default);
        }

        public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            long totalMilliseconds = (long)delay.TotalMilliseconds;
            if (totalMilliseconds < -1)
                throw new ArgumentOutOfRangeException(nameof(delay), "Invalid delay");

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            if (totalMilliseconds == 0)
                return Task.CompletedTask;

            return CreateDelay(delay, cancellationToken);
        }

        public static Task Delay(int millisecondsDelay)
        {
            return Delay(TimeSpan.FromMilliseconds(millisecondsDelay), default);
        }

        public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
        {
            return Delay(TimeSpan.FromMilliseconds(millisecondsDelay), cancellationToken);
        }

        static async Task CreateDelay(TimeSpan delay, CancellationToken cancellationToken)
        {
            MetaDelay delayAwaitable = new MetaDelay(delay, cancellationToken);
            await delayAwaitable;

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
