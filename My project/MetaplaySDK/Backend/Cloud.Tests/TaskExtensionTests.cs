// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    [TestFixture]
    public class TaskExtensionsTests
    {
        class FooException : Exception
        {
        }

        [Test]
        public async Task TaskWithCancelAsync()
        {
            // Succeeds
            {
                TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
                CancellationTokenSource cts = new CancellationTokenSource(millisecondsDelay: 100);
                tcs.SetResult(123);

                await ((Task)tcs.Task).WithCancelAsync(cts.Token);
            }

            // Cancels
            {
                TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
                CancellationTokenSource cts = new CancellationTokenSource(millisecondsDelay: 100);
                Exception ex = Assert.ThrowsAsync(typeof(OperationCanceledException) , async () =>
                {
                    await ((Task)tcs.Task).WithCancelAsync(cts.Token);
                });
                Assert.AreEqual(((OperationCanceledException)ex).CancellationToken, cts.Token);
            }
        }

        [Test]
        public async Task TaskIntWithCancelAsync()
        {
            // Succeeds
            {
                TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
                CancellationTokenSource cts = new CancellationTokenSource(millisecondsDelay: 100);
                tcs.SetResult(123);

                int res = await (tcs.Task).WithCancelAsync(cts.Token);
                Assert.AreEqual(123, res);
            }

            // Cancels
            {
                TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
                CancellationTokenSource cts = new CancellationTokenSource(millisecondsDelay: 100);
                Exception ex = Assert.ThrowsAsync(typeof(OperationCanceledException) , async () =>
                {
                    await (tcs.Task).WithCancelAsync(cts.Token);
                });
                Assert.AreEqual(((OperationCanceledException)ex).CancellationToken, cts.Token);
            }
        }

        class DisposableObject : IDisposable
        {
            public bool IsDisposed;
            public int DisposeDelay;
            TaskCompletionSource _disposed = new TaskCompletionSource();
            public Task WhenDisposed => _disposed.Task;

            void IDisposable.Dispose()
            {
                // simulate work
                if (DisposeDelay != 0)
                    Thread.Sleep(DisposeDelay);
                IsDisposed = true;

                _disposed.SetResult();
            }
        }

        [Test]
        public void TaskContinueWithDisposeSynchronousCompletion()
        {
            // Success
            DisposableObject obj = new DisposableObject();
            Task.FromResult<DisposableObject>(obj).ContinueWithDispose(allowSynchronousExecution: true);
            Assert.AreEqual(true, obj.IsDisposed);

            // Nothing
            Task.FromException<DisposableObject>(new InvalidOperationException()).ContinueWithDispose(allowSynchronousExecution: true);
        }

        [Test]
        public void TaskContinueWithDisposeAsynchronousCompletion()
        {
            // Success (Dispose is NOT called on this thread)
            DisposableObject obj = new DisposableObject();
            obj.DisposeDelay = 100;
            Task.FromResult<DisposableObject>(obj).ContinueWithDispose(allowSynchronousExecution: false);
            Assert.AreEqual(false, obj.IsDisposed);

            // Nothing
            Task.FromException<DisposableObject>(new InvalidOperationException()).ContinueWithDispose(allowSynchronousExecution: false);
        }

        [Test]
        public async Task TaskContinueWithDisposeAsynchronousCompute()
        {
            // Wait Dispose does get called
            DisposableObject obj = new DisposableObject();
            Task.Run(async () =>
            {
                await Task.Delay(100);
                return obj;
            }).ContinueWithDispose();

            Assert.AreEqual(false, obj.IsDisposed);
            await obj.WhenDisposed;
            Assert.AreEqual(true, obj.IsDisposed);
        }

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
        [Test]
        public void TaskGetCompletedResult()
        {
            // Completed
            Task<int> completed = Task.FromResult<int>(123);
            Assert.AreEqual(completed.Result, completed.GetCompletedResult());

            // Failed. Must be same throw as with Result
            ExpectThrowsExpectFooExceptionInAggregate(() =>
            {
                _ = Task.FromException<int>(new FooException()).Result;
            });
            ExpectThrowsExpectFooExceptionInAggregate(() =>
            {
                _ = Task.FromException<int>(new FooException()).GetCompletedResult();
            });

            // Cancelled
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();
            ExpectThrowsOperationCancelled(() =>
            {
                _ = Task.FromCanceled<int>(cts.Token).Result;
            });
            ExpectThrowsOperationCancelled(() =>
            {
                _ = Task.FromCanceled<int>(cts.Token).GetCompletedResult();
            });

            // Pending
            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
            Assert.Throws(typeof(InvalidOperationException), () => tcs.Task.GetCompletedResult());
        }

        [Test]
        public void ValueTaskGetCompletedResult()
        {
            // Completed
            Assert.AreEqual(new ValueTask<int>(123).Result, new ValueTask<int>(123).GetCompletedResult());

            // Failed. Must be same throw as with Result
            Assert.Throws(typeof(FooException), () =>
            {
                _ = ValueTask.FromException<int>(new FooException()).Result;
            });
            Assert.Throws(typeof(FooException), () =>
            {
                _ = ValueTask.FromException<int>(new FooException()).GetCompletedResult();
            });

            // Cancelled
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws(typeof(TaskCanceledException), () =>
            {
                _ = ValueTask.FromCanceled<int>(cts.Token).Result;
            });
            Assert.Throws(typeof(TaskCanceledException), () =>
            {
                _ = ValueTask.FromCanceled<int>(cts.Token).GetCompletedResult();
            });

            // Pending
            ValueTask<int> task = DelayValueTask(1000);
            Assert.Throws(typeof(InvalidOperationException), () => task.GetCompletedResult());
        }
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

        async ValueTask<int> DelayValueTask(int ms)
        {
            await Task.Delay(ms);
            return ms;
        }

        static void ExpectThrowsExpectFooExceptionInAggregate(Action action)
        {
            try
            {
                action();
                Assert.Fail("expected throw");
            }
            catch (AggregateException ex)
            {
                ExpectFooExceptionInAggregate(ex);
            }
        }
        static void ExpectThrowsOperationCancelled(Action action)
        {
            try
            {
                action();
                Assert.Fail("expected throw");
            }
            catch (AggregateException aex)
            {
                if (aex.InnerExceptions.Count == 1 && aex.InnerException is TaskCanceledException)
                    return;
                throw;
            }
            catch
            {
                throw;
            }
        }

        static void ExpectFooExceptionInAggregate(Exception ex)
        {
            if (ex is AggregateException aex)
            {
                if (aex.InnerExceptions.Count == 1 && aex.InnerException is FooException)
                    return;
            }
            Assert.Fail("Expected AggregateException{FooException}");
        }
    }
}
