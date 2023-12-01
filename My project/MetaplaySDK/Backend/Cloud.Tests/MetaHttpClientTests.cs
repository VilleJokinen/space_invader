// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Network;
using NUnit.Framework;
using System.Net;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    // \todo [petri] These are only run against the HttpClient-based MetaHttpClient -- should implement tests for UnityWebClient-based implementation

    class MetaHttpClientTests
    {
        [TestCase]
        public async Task TestGet()
        {
            using (MetaHttpResponse response = await MetaHttpClient.DefaultInstance.GetAsync("https://google.com"))
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.NotNull(response.Content);
                Assert.Greater(response.Content.Length, 100);
            }
        }

        [TestCase]
        public async Task TestCustomInstance()
        {
            using (MetaHttpClient client = new MetaHttpClient())
            using (MetaHttpResponse response = await client.GetAsync("https://google.com"))
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.NotNull(response.Content);
                Assert.Greater(response.Content.Length, 100);
            }
        }

        [TestCase]
        public async Task TestGet404()
        {
            using (MetaHttpResponse response = await MetaHttpClient.DefaultInstance.GetAsync("https://google.com/non-existent-page-asdhfaskjdhfkajshfkjahfkjahfdkjahdsfkjahdskfjhsadkjfhasdkjf"))
            {
                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
                Assert.NotNull(response.Content);
            }
        }

        [TestCase]
        public void TestInvalidHost()
        {
            Assert.ThrowsAsync<MetaHttpRequestError>(async () =>
            {
                using (MetaHttpResponse response = await MetaHttpClient.DefaultInstance.GetAsync("https://non-existent-host.asdfasjdhfakjshfakjdhfaksjhfasfd.com"))
                {
                    Assert.Fail(); // shold not be here!
                }
            });
        }
    }
}
