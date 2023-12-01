// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    public class UrlWhitelistTests
    {
        [RuntimeOptions("UrlTest", isStatic: false)]
        class TestOptions : RuntimeOptionsBase
        {
            public UrlWhitelist Whitelist { get; private set; } = null; // null to force parse something.
        }

        static (TestOptions Options, RuntimeOptionsBinder.BindingResults Results) BindYaml(string value, bool strictBindingChecks = true, bool throwOnError = true)
        {
            return YamlRuntimeOptionsTestUtil.BindYaml<TestOptions>(value, strictBindingChecks, throwOnError);
        }

        [Test]
        public void TestAllowAll()
        {
            CheckAllowAll(UrlWhitelist.AllowAll);
        }

        [Test]
        public void TestAllowAllFromYaml()
        {
            CheckAllowAll(BindYaml(@"
UrlTest:
    Whitelist: '*'
").Options.Whitelist);

            CheckAllowAll(BindYaml(@"
UrlTest:
    Whitelist:
    - '*'
").Options.Whitelist);
        }

        static void CheckAllowAll(UrlWhitelist list)
        {
            Assert.IsTrue(list.IsAllowed(new Uri("http://example.org")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://example.org:123/path?query")));
            Assert.IsTrue(list.IsAllowed(new Uri("scheme://something.everything/path?query")));
        }

        [Test]
        public void TestDenyAll()
        {
            CheckDenyAll(UrlWhitelist.DenyAll);
        }

        [Test]
        public void TestDenyAllFromYaml()
        {
            CheckDenyAll(BindYaml(@"
UrlTest:
    Whitelist:
").Options.Whitelist);
        }

        static void CheckDenyAll(UrlWhitelist list)
        {
            Assert.IsFalse(list.IsAllowed(new Uri("http://example.org")));
            Assert.IsFalse(list.IsAllowed(new Uri("http://example.org:123/path?query")));
            Assert.IsFalse(list.IsAllowed(new Uri("scheme://something.everything/path?query")));
        }

        [Test]
        public void TestMatchRules()
        {
            UrlWhitelist list = BindYaml(@"
UrlTest:
    Whitelist:
    - http://noport.com/
    - https://noport.org/
    - http://withport.com:81/
    - https://withport.com:181/
    - https://pathtest1.com/
    - https://pathtest2.com/exact
    - https://pathtest3.com/pathprefix/
    - myscheme://name.of.a.package/
").Options.Whitelist;

            Assert.IsTrue(list.IsAllowed(new Uri("http://noport.com")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://noport.com/")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://noport.com/asd")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://noport.com:80")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://noport.com:80/")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://noport.com:80/asd")));
            Assert.IsFalse(list.IsAllowed(new Uri("http://noport.com:81/")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://noport.com:80/")));

            Assert.IsTrue(list.IsAllowed(new Uri("https://noport.org")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://noport.org/")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://noport.org/asd")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://noport.org:443")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://noport.org:443/")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://noport.org:443/asd")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://noport.org:80/")));
            Assert.IsFalse(list.IsAllowed(new Uri("http://noport.org:443/")));

            Assert.IsTrue(list.IsAllowed(new Uri("http://withport.com:81")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://withport.com:81/")));
            Assert.IsTrue(list.IsAllowed(new Uri("http://withport.com:81/asd")));
            Assert.IsFalse(list.IsAllowed(new Uri("http://withport.com/")));
            Assert.IsFalse(list.IsAllowed(new Uri("http://withport.com:82/")));

            Assert.IsTrue(list.IsAllowed(new Uri("https://withport.com:181")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://withport.com:181/")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://withport.com:181/asd")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://withport.com")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://withport.com:182/")));

            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest1.com")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest1.com/")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest1.com/?foo")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest1.com/anything")));

            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest2.com")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest2.com/")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest2.com/exact")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest2.com/exact?foo")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest2.com/anything")));

            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest3.com")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest3.com/")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest3.com/pathprefix")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest3.com/pathprefix/")));
            Assert.IsTrue(list.IsAllowed(new Uri("https://pathtest3.com/pathprefix/anythingg")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest3.com/pathprefix/../foo")));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest3.com/pathprefix/../foo", new UriCreationOptions() { DangerousDisablePathAndQueryCanonicalization = true })));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest3.com/pathprefix/../pathprefix/something", new UriCreationOptions() { DangerousDisablePathAndQueryCanonicalization = true })));
            Assert.IsFalse(list.IsAllowed(new Uri("https://pathtest3.com/anything")));

            Assert.IsTrue(list.IsAllowed(new Uri("myscheme://name.of.a.package")));
            Assert.IsTrue(list.IsAllowed(new Uri("myscheme://name.of.a.package/anything")));
            Assert.IsFalse(list.IsAllowed(new Uri("myscheme2://name.of.a.package")));
            Assert.IsFalse(list.IsAllowed(new Uri("myscheme://wrong.name.of.a.package")));

            Assert.IsFalse(list.IsAllowed(new Uri("http://user@noport.com")));

            Assert.IsTrue(list.IsAllowed(new Uri("mySCHEME://name.of.a.package")));
            Assert.IsTrue(list.IsAllowed(new Uri("mySCHEME://NAME.of.a.package")));
            Assert.IsFalse(list.IsAllowed(new Uri("myscheme://nᵃme.of.a.package")));
        }
    }
}
