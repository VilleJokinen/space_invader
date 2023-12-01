// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using NUnit.Framework;
using System.Collections.Generic;

namespace Cloud.Tests
{
    class EnvironmentRuntimeOptionsTests
    {
        [Test]
        public void Empty()
        {
            Assert.IsTrue(EnvironmentRuntimeOptionsSource.Parse("Metaplay_", new Metaplay.Core.OrderedDictionary<string, string>() { }).Definitions.Count == 0);
        }

        [TestCase("Metaplay_Ip")]
        [TestCase("METAPLAY_EXTRA_OPTIONS")]
        [TestCase("METAPLAY_clienT_SVC_12")]
        [TestCase("something__else")]
        public void Ignored(string key)
        {
            Assert.IsTrue(EnvironmentRuntimeOptionsSource.Parse("Metaplay_", new Metaplay.Core.OrderedDictionary<string, string>() { { key, "value" } }).Definitions.Count == 0);
        }

        [TestCase("Metaplay_Foo__Bar", "Foo:Bar")]
        [TestCase("Metaplay_Bar__Foo__Extra", "Bar:Foo:Extra")]
        public void Value(string key, string config)
        {
            IReadOnlyDictionary<string, string> definitions = EnvironmentRuntimeOptionsSource.Parse("Metaplay_", new Metaplay.Core.OrderedDictionary<string, string>() { { key, "value" }  }).Definitions;
            Assert.IsTrue(definitions.Count == 1);
            Assert.IsTrue(definitions[config] == "value");
        }
    }
}
