// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Tasks;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    class TaskQueueExecutorTest
    {
        [Test]
        public async Task TestEnqueueActionWithTimers()
        {
            for (int schedulerI = 0; schedulerI < 2; schedulerI++)
            {
                for (int delayPosI = 0; delayPosI < 5; delayPosI++)
                {
                    TaskQueueExecutor executor = new TaskQueueExecutor(schedulerI == 0 ? TaskScheduler.Default : TaskScheduler.Current);

                    int target = 0;
                    int expected = 4;

                    if (delayPosI == 1) await Task.Delay(10);
                    executor.EnqueueAsync(() => { if (target == 0) target = 1; });

                    if (delayPosI == 2) await Task.Delay(10);
                    executor.EnqueueAsync((int)1, (object arg1) => { if (target == (int)arg1) target = 2; });

                    if (delayPosI == 3) await Task.Delay(10);
                    executor.EnqueueAsync((int)2, (int)3, (object arg1, object arg2) => { if (target == (int)arg1) target = (int)arg2; });

                    if (delayPosI == 4) await Task.Delay(10);
                    executor.EnqueueAsync((int)3, (int)6, (int)2, (object arg1, object arg2, object arg3) =>
                    {
                        if (target == (int)arg1)
                            target = (int)arg2 - (int)arg3;
                    });

                    if (delayPosI == 5)
                    {
                        executor.EnqueueAsync(async () =>
                        {
                            await Task.Delay(10);
                            if (target == 4)
                                target = 5;
                        });
                        expected = 5;
                    }

                    TaskCompletionSource tcs = new TaskCompletionSource();
                    executor.EnqueueAsync(() => tcs.SetResult());

                    await tcs.Task;

                    Assert.AreEqual(expected, target, "With params: {0} -- delay {1}", schedulerI, delayPosI);
                }
            }
        }

        [Test]
        public async Task TestEnqueueActionWithCompletions()
        {
            for (int schedulerI = 0; schedulerI < 2; schedulerI++)
            {
                for (int delayPosI = 0; delayPosI < 5; delayPosI++)
                {
                    TaskQueueExecutor executor = new TaskQueueExecutor(schedulerI == 0 ? TaskScheduler.Default : TaskScheduler.Current);
                    TaskCompletionSource delayTcs = new TaskCompletionSource();

                    int target = 0;
                    int expected = 4;

                    if (delayPosI == 1)
                        await Task.Delay(10);

                    executor.EnqueueAsync(() =>
                    {
                        if (target == 0)
                            target = 1;
                        if (delayPosI == 2)
                            delayTcs.SetResult();
                    });
                    if (delayPosI == 2)
                        await delayTcs.Task;

                    executor.EnqueueAsync((int)1, (object arg1) =>
                    {
                        if (target == (int)arg1)
                            target = 2;
                        if (delayPosI == 3)
                            delayTcs.SetResult();
                    });
                    if (delayPosI == 3)
                        await delayTcs.Task;

                    executor.EnqueueAsync((int)2, (int)3, (object arg1, object arg2) =>
                    {
                        if (target == (int)arg1)
                            target = (int)arg2;
                        if (delayPosI == 4)
                            delayTcs.SetResult();
                    });
                    if (delayPosI == 4)
                        await delayTcs.Task;

                    executor.EnqueueAsync((int)3, (int)6, (int)2, (object arg1, object arg2, object arg3) =>
                    {
                        if (target == (int)arg1)
                            target = (int)arg2 - (int)arg3;
                    });

                    if (delayPosI == 5)
                    {
                        executor.EnqueueAsync(async () =>
                        {
                            await delayTcs.Task;
                            if (target == 4)
                                target = 5;
                        });
                        expected = 5;
                    }

                    TaskCompletionSource completionTcs = new TaskCompletionSource();
                    executor.EnqueueAsync(() => completionTcs.SetResult());

                    if (delayPosI == 5)
                        delayTcs.SetResult();

                    await completionTcs.Task;

                    Assert.AreEqual(expected, target, "With params: {0} -- delay {1}", schedulerI, delayPosI);
                }
            }
        }
    }
}
