// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Tasks;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    class MetaDelayTests
    {
        [Test]
        public async Task DelayInt()
        {
            await TestEqual(true, () => Task.Delay(0), () => MetaDelay.Delay(0));
            await TestEqual(true, () => Task.Delay(100), () => MetaDelay.Delay(100));
            await TestEqual(true, () => Task.Delay(-100), () => MetaDelay.Delay(-100));
            await TestEqual(false, () => Task.Delay(-1), () => MetaDelay.Delay(-1));
        }

        [Test]
        public async Task DelayIntCt()
        {
            (CancellationToken, bool)[] tokens = new (CancellationToken, bool)[]
            {
                (default, false),
                CreateCancellationToken(0),
                CreateCancellationToken(-1),
                CreateCancellationToken(500),
            };

            foreach ((CancellationToken ct, bool ctWillTrigger) in tokens)
            {
                await TestEqual(true, () => Task.Delay(0, ct), () => MetaDelay.Delay(0, ct));
                await TestEqual(true, () => Task.Delay(100, ct), () => MetaDelay.Delay(100, ct));
                await TestEqual(true, () => Task.Delay(-100, ct), () => MetaDelay.Delay(-100, ct));
                await TestEqual(ctWillTrigger, () => Task.Delay(-1, ct), () => MetaDelay.Delay(-1, ct));
            }
        }

        [Test]
        public async Task DelayTimespan()
        {
            await TestEqual(true, () => Task.Delay(TimeSpan.FromMilliseconds(0)), () => MetaDelay.Delay(TimeSpan.FromMilliseconds(0)));
            await TestEqual(true, () => Task.Delay(TimeSpan.FromMilliseconds(100)), () => MetaDelay.Delay(TimeSpan.FromMilliseconds(100)));
            await TestEqual(true, () => Task.Delay(TimeSpan.FromMilliseconds(-100)), () => MetaDelay.Delay(TimeSpan.FromMilliseconds(-100)));
            await TestEqual(false, () => Task.Delay(Timeout.InfiniteTimeSpan), () => MetaDelay.Delay(Timeout.InfiniteTimeSpan));
            await TestEqual(false, () => Task.Delay(TimeSpan.FromDays(10)), () => MetaDelay.Delay(TimeSpan.FromDays(10)));
            await TestEqual(false, () => Task.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond - 1)), () => MetaDelay.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond - 1)));
            await TestEqual(false, () => Task.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond + 1)), () => MetaDelay.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond + 1)));
        }

        [Test]
        public async Task DelayTimespanCt()
        {
            (CancellationToken, bool)[] tokens = new (CancellationToken, bool)[]
            {
                (default, false),
                CreateCancellationToken(0),
                CreateCancellationToken(-1),
                CreateCancellationToken(500),
            };

            foreach ((CancellationToken ct, bool ctWillTrigger) in tokens)
            {
                await TestEqual(true, () => Task.Delay(TimeSpan.FromMilliseconds(0), ct), () => MetaDelay.Delay(TimeSpan.FromMilliseconds(0), ct));
                await TestEqual(true, () => Task.Delay(TimeSpan.FromMilliseconds(100), ct), () => MetaDelay.Delay(TimeSpan.FromMilliseconds(100), ct));
                await TestEqual(true, () => Task.Delay(TimeSpan.FromMilliseconds(-100), ct), () => MetaDelay.Delay(TimeSpan.FromMilliseconds(-100), ct));
                await TestEqual(false, () => Task.Delay(Timeout.InfiniteTimeSpan, ct), () => MetaDelay.Delay(Timeout.InfiniteTimeSpan, ct));
                await TestEqual(false, () => Task.Delay(TimeSpan.FromDays(10), ct), () => MetaDelay.Delay(TimeSpan.FromDays(10), ct));
                await TestEqual(false, () => Task.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond - 1), ct), () => MetaDelay.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond - 1), ct));
                await TestEqual(false, () => Task.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond + 1), ct), () => MetaDelay.Delay(TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond + 1), ct));
            }
        }

        static async Task TestEqual(bool isFinite, Func<Task> referenceTaskFactory, Func<Task> metaDelayFactory)
        {
            Task referenceTask;
            try
            {
                referenceTask = referenceTaskFactory();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Must also synchronously throw.
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = metaDelayFactory());
                return;
            }

            Task metaDelay = metaDelayFactory();
            if (referenceTask.IsCanceled)
                Assert.IsTrue(metaDelay.IsCanceled);
            if (referenceTask.IsCompleted)
                Assert.IsTrue(metaDelay.IsCompleted);

            if (isFinite)
            {
                try
                {
                    await referenceTask;
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        await metaDelay;
                    }
                    catch(OperationCanceledException)
                    {
                        return;
                    }

                    Assert.Fail("Expected OperationCanceledException");
                }

                await metaDelay;
            }
        }

        static (CancellationToken, bool) CreateCancellationToken(int cancelledAfterMillis)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            bool isFinite;
            if (cancelledAfterMillis > 0)
            {
                cts.CancelAfter(cancelledAfterMillis);
                isFinite = true;
            }
            else if (cancelledAfterMillis == 0)
            {
                cts.Cancel();
                isFinite = true;
            }
            else
            {
                isFinite = false;
            }
            return (cts.Token, isFinite);
        }
    }
}
