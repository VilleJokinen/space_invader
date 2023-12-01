// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.ProcFs;
using NUnit.Framework;
using System.IO;
using System.Text;

namespace Cloud.Tests
{
    class ProcFsTests
    {
        [Test]
        public void ProcFsNetstatReader()
        {
            string source =
                """
                Ip: Forwarding DefaultTTL InReceives InHdrErrors InAddrErrors ForwDatagrams InUnknownProtos InDiscards InDelivers OutRequests OutDiscards OutNoRoutes ReasmTimeout ReasmReqds ReasmOKs ReasmFails FragOKs FragFails FragCreates
                Ip: 1 64 212025117 0 0 0 0 0 212017247 180484937 11143 18 0 0 0 0 0 0 0
                Icmp: InMsgs InErrors InCsumErrors InDestUnreachs InTimeExcds InParmProbs InSrcQuenchs InRedirects InEchos InEchoReps InTimestamps InTimestampReps InAddrMasks InAddrMaskReps OutMsgs OutErrors OutDestUnreachs OutTimeExcds OutParmProbs OutSrcQuenchs OutRedirects OutEchos OutEchoReps OutTimestamps OutTimestampReps OutAddrMasks OutAddrMaskReps
                Icmp: 1243 0 0 1196 0 0 0 0 2 45 0 0 0 0 1307 0 1198 0 0 0 0 107 2 0 0 0 0
                """;

            using (ProcFsNetstatReader reader = new ProcFsNetstatReader(new MemoryStream(Encoding.UTF8.GetBytes(source))))
            {
                Assert.IsTrue(reader.TryGetValue("Icmp", "InMsgs", out long icmpInMsgs));
                Assert.AreEqual(1243, icmpInMsgs);
                Assert.IsFalse(reader.TryGetValue("Icmp", "foo", out long _));

                reader.UpdateFromProcFs();
                Assert.IsTrue(reader.TryGetValue("Icmp", "InDestUnreachs", out icmpInMsgs));
                Assert.AreEqual(1196, icmpInMsgs);
                Assert.IsFalse(reader.TryGetValue("Icmp", "foo", out long _));
            }
        }

        [Test]
        public void ProcFsSnmp6Reader()
        {
            string source =
                """
                Ip6InReceives                           841751
                Ip6InHdrErrors                          0
                Ip6InTooBigErrors                       0
                Ip6InNoRoutes                           0
                Ip6InAddrErrors                         0
                Ip6InOctets                             68214232
                Ip6OutOctets                            68041760
                """;

            using (ProcFsSnmp6Reader reader = new ProcFsSnmp6Reader(new MemoryStream(Encoding.UTF8.GetBytes(source))))
            {
                Assert.IsTrue(reader.TryGetValue("Ip6InReceives", out long ip6InReceives));
                Assert.AreEqual(841751, ip6InReceives);
                Assert.IsFalse(reader.TryGetValue("foo", out long _));

                reader.UpdateFromProcFs();
                Assert.IsTrue(reader.TryGetValue("Ip6InOctets", out long ip6InOctets));
                Assert.AreEqual(68214232, ip6InOctets);
                Assert.IsFalse(reader.TryGetValue("foo", out long _));
            }
        }
    }
}
