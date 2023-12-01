// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core;
using Metaplay.Core.Guild;
using NUnit.Framework;

namespace Cloud.Tests
{
    class GuildInviteCodeTests
    {

        [Test]
        public void TestRandomIds()
        {
            RandomPCG rng = RandomPCG.CreateFromSeed(0x12345);
            for (int repeat = 0; repeat < 10000; ++repeat)
            {
                ulong raw = rng.NextULong() & GuildInviteCode.MaxRawValue;
                GuildInviteCode code = GuildInviteCode.FromRaw(raw);

                GuildInviteCode code2;
                Assert.IsTrue(GuildInviteCode.TryParse(code.ToString(), out code2));
                Assert.AreEqual(code, code2);
                Assert.AreEqual(raw, code2.Raw);

                GuildInviteCode code3;
                Assert.IsTrue(GuildInviteCode.TryParse(code.ToString().Replace("-","").Replace('I', '1').Replace('A', 'a'), out code3));
                Assert.AreEqual(code, code3);
                Assert.AreEqual(raw, code3.Raw);
            }
        }
    }
}

#endif
