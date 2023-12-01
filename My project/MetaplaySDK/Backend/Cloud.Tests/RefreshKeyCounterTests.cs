// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    // \note[jarkko]: These tests require consistent forward progress to works. If test threads get
    //                temporarily starved, the we might get false negatives.
    [NonParallelizable]
    class RefreshKeyCounterTests
    {
        // \note[jarkko]: Even with [NonParallelizable], the tests are suspectible to noise. Tolerate one retry
        //                if initial run fails.
        const int RetryCount = 2;

        [Test]
        [Retry(RetryCount)]
        public void TestAddition()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(1));
            Assert.AreEqual(0, counter.GetNumAlive());

            _ = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public void TestAliasHandle()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(1));
            Assert.AreEqual(0, counter.GetNumAlive());

            RefreshKeyCounter<int>.RefreshHandle handle1 = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());
            RefreshKeyCounter<int>.RefreshHandle handle2 = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());

            handle1.Remove();
            Assert.AreEqual(0, counter.GetNumAlive());
            handle2.Refresh();
            Assert.AreEqual(0, counter.GetNumAlive());
            handle2.Remove();
            Assert.AreEqual(0, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public void TestRemoval()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(1));
            Assert.AreEqual(0, counter.GetNumAlive());

            RefreshKeyCounter<int>.RefreshHandle handle = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());
            RefreshKeyCounter<int>.RefreshHandle handle2 = counter.Add(2);
            Assert.AreEqual(2, counter.GetNumAlive());
            handle.Remove();
            Assert.AreEqual(1, counter.GetNumAlive());
            handle.Remove();
            Assert.AreEqual(1, counter.GetNumAlive());
            handle2.Remove();
            Assert.AreEqual(0, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public void TestLongLived()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(5));
            Assert.AreEqual(0, counter.GetNumAlive());

            const int numSteps = 10;
            List<RefreshKeyCounter<int>.RefreshHandle> handles = new List<RefreshKeyCounter<int>.RefreshHandle>();
            long startMillisecond = MetaTime.Now.MillisecondsSinceEpoch;
            for (int i = 0; i < numSteps; i++)
            {
                foreach (RefreshKeyCounter<int>.RefreshHandle handle in handles)
                    handle.Refresh();
                handles.Add(counter.Add(i));

                while (MetaTime.Now.MillisecondsSinceEpoch < startMillisecond + i * 2);
                if (MetaTime.Now.MillisecondsSinceEpoch > startMillisecond + i * 2 + 3)
                    OnStarvation();
                Assert.AreEqual(i + 1, counter.GetNumAlive());
            }

            foreach (RefreshKeyCounter<int>.RefreshHandle handle in handles)
                handle.Remove();
            Assert.AreEqual(0, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public async Task TestExpirationWithQuery()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(1));
            Assert.AreEqual(0, counter.GetNumAlive());

            RefreshKeyCounter<int>.RefreshHandle handle = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());

            await DelayAtLeast(10);
            Assert.AreEqual(0, counter.GetNumAlive());
            handle.Refresh();
            Assert.AreEqual(0, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public async Task TestExpirationWithOldRefresh()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(1));
            Assert.AreEqual(0, counter.GetNumAlive());

            RefreshKeyCounter<int>.RefreshHandle handle = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());

            await DelayAtLeast(10);
            handle.Refresh();
            Assert.AreEqual(0, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public async Task TestRemovalAfterExpiration()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(1));
            Assert.AreEqual(0, counter.GetNumAlive());

            RefreshKeyCounter<int>.RefreshHandle handle = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());

            await DelayAtLeast(10);
            handle.Remove(); // expired and does nothing
            Assert.AreEqual(0, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public async Task TestRemovalAfterHandleExpiration()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(1));
            Assert.AreEqual(0, counter.GetNumAlive());

            RefreshKeyCounter<int>.RefreshHandle handle = counter.Add(1);
            Assert.AreEqual(1, counter.GetNumAlive());

            await DelayAtLeast(10);
            handle.Refresh(); // this will fail
            handle.Remove(); // expired and does nothing
            Assert.AreEqual(0, counter.GetNumAlive());
        }

        [Test]
        [Retry(RetryCount)]
        public void TestRefresh()
        {
            RefreshKeyCounter<int> counter = new RefreshKeyCounter<int>(MetaDuration.FromMilliseconds(2));
            Assert.AreEqual(0, counter.GetNumAlive());

            RefreshKeyCounter<int>.RefreshHandle handle = counter.Add(1);
            long startMillisecond = MetaTime.Now.MillisecondsSinceEpoch;
            Assert.AreEqual(1, counter.GetNumAlive());

            while (MetaTime.Now.MillisecondsSinceEpoch < startMillisecond + 2);
            if (MetaTime.Now.MillisecondsSinceEpoch > startMillisecond + 2)
                OnStarvation();
            Assert.AreEqual(1, counter.GetNumAlive());
            handle.Refresh();
            Assert.AreEqual(1, counter.GetNumAlive());

            while (MetaTime.Now.MillisecondsSinceEpoch < startMillisecond + 4);
            if (MetaTime.Now.MillisecondsSinceEpoch > startMillisecond + 4)
                OnStarvation();
            Assert.AreEqual(1, counter.GetNumAlive());
            handle.Refresh();
            Assert.AreEqual(1, counter.GetNumAlive());

            while (MetaTime.Now.MillisecondsSinceEpoch < startMillisecond + 6);
            if (MetaTime.Now.MillisecondsSinceEpoch > startMillisecond + 6)
                OnStarvation();
            Assert.AreEqual(1, counter.GetNumAlive());
            handle.Refresh();
            Assert.AreEqual(1, counter.GetNumAlive());
        }

        static Task DelayAtLeast(int milliseconds) => Task.Delay(milliseconds);

        static void OnStarvation()
        {
            throw new InconclusiveException("Skipping test due to unreliable scheduling. CPU starved?");
        }
    }
}
