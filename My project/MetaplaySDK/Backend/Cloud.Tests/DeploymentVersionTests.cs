// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Options;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    [TestFixture]
    public class DeploymentVersionTests
    {
        [TestCase("1.2.3", 1, 2, 3, null)]
        [TestCase("1.2.3-label", 1, 2, 3, "label")]
        [TestCase("0.0.0-label", 0, 0, 0, "label")]
        [TestCase("10.100.1000-XX", 10, 100, 1000, "XX")]
        public void TestParseValidString(string str, int major, int minor, int patch, string label)
        {
            DeploymentVersion vsn = DeploymentVersion.ParseFromString(str);
            Assert.AreEqual(major, vsn.Major);
            Assert.AreEqual(minor, vsn.Minor);
            Assert.AreEqual(patch, vsn.Patch);
            Assert.AreEqual(label, vsn.Label);
        }

        [TestCase("")]
        [TestCase("dummy")]
        [TestCase("-")]
        [TestCase("-label")]
        [TestCase("1")]
        [TestCase("1.2")]
        [TestCase("..")]
        [TestCase("..-label")]
        [TestCase(".1.2")]
        [TestCase("1..2")]
        [TestCase("1.09.2")]
        [TestCase("0.1.2.")]
        [TestCase("0.1.2-")]
        [TestCase("1.2.3-")]
        [TestCase("-1.2.3")]
        [TestCase("-0.0.0-label")]
        [TestCase("-1.0.0")]
        [TestCase("0.-0.0-label")]
        [TestCase("0.-1.0-XX")]
        public void TestParseInvalidString(string str)
        {
            Assert.Catch<InvalidOperationException>(() =>
            {
                DeploymentVersion.ParseFromString(str);
            });
        }

        [TestCase("0.0.0", "0.0.0")]
        [TestCase("0.1.0", "0.1.0")]
        [TestCase("9.99.999", "9.99.999")]
        [TestCase("5.10.0", "5.10.0")]
        [TestCase("5.10.0", "5.10.0-label")]
        [TestCase("5.10.0-lbl", "5.10.0-label")]
        [TestCase("9.99.999", "9.99.999-label")]
        public void TestEquality(string leftStr, string rightStr)
        {
            DeploymentVersion left = DeploymentVersion.ParseFromString(leftStr);
            DeploymentVersion right = DeploymentVersion.ParseFromString(rightStr);
            Assert.AreEqual(0, left.CompareTo(right));
            Assert.AreEqual(0, ((IComparable)left).CompareTo(right));
            Assert.AreEqual(left, right);
            Assert.IsTrue(left.Equals(right));
            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);
        }

        // All left values are greater-than right values
        [TestCase("0.0.1", "0.0.0")]
        [TestCase("0.0.2", "0.0.1")]
        [TestCase("0.0.10", "0.0.9")]
        [TestCase("0.1.0", "0.0.0")]
        [TestCase("0.1.0", "0.0.1")]
        [TestCase("0.1.0", "0.0.9")]
        [TestCase("0.15.0", "0.0.99")]
        [TestCase("0.15.0", "0.5.55")]
        [TestCase("0.15.0", "0.14.99")]
        [TestCase("1.0.0", "0.0.0")]
        [TestCase("1.0.0", "0.0.6")]
        [TestCase("1.0.0", "0.5.6")]
        [TestCase("2.0.0", "1.9.9")]
        [TestCase("2.0.0", "1.99.99")]
        public void TestCompare(string leftStr, string rightStr)
        {
            // Compare without labels
            {
                DeploymentVersion left = DeploymentVersion.ParseFromString(leftStr);
                DeploymentVersion right = DeploymentVersion.ParseFromString(rightStr);
                Assert.IsTrue(left.CompareTo(right) > 0);
                Assert.IsTrue(((IComparable)left).CompareTo(right) > 0);
                Assert.Greater(left, right);
                Assert.IsFalse(left == right);
                Assert.IsTrue(left != right);
                Assert.IsFalse(left < right);
                Assert.IsTrue(left > right);
            }

            // Compare with left having label
            {
                DeploymentVersion left = DeploymentVersion.ParseFromString(leftStr + "-label");
                DeploymentVersion right = DeploymentVersion.ParseFromString(rightStr);
                Assert.IsTrue(left.CompareTo(right) > 0);
                Assert.IsTrue(((IComparable)left).CompareTo(right) > 0);
                Assert.Greater(left, right);
                Assert.IsFalse(left == right);
                Assert.IsTrue(left != right);
                Assert.IsFalse(left < right);
                Assert.IsTrue(left > right);
            }

            // Compare with right having label
            {
                DeploymentVersion left = DeploymentVersion.ParseFromString(leftStr);
                DeploymentVersion right = DeploymentVersion.ParseFromString(rightStr + "-lbl");
                Assert.IsTrue(left.CompareTo(right) > 0);
                Assert.IsTrue(((IComparable)left).CompareTo(right) > 0);
                Assert.Greater(left, right);
                Assert.IsFalse(left == right);
                Assert.IsTrue(left != right);
                Assert.IsFalse(left < right);
                Assert.IsTrue(left > right);
            }

            // Compare with both having label
            {
                DeploymentVersion left = DeploymentVersion.ParseFromString(leftStr + "-label");
                DeploymentVersion right = DeploymentVersion.ParseFromString(rightStr + "-lbl");
                Assert.IsTrue(left.CompareTo(right) > 0);
                Assert.IsTrue(((IComparable)left).CompareTo(right) > 0);
                Assert.Greater(left, right);
                Assert.IsFalse(left == right);
                Assert.IsTrue(left != right);
                Assert.IsFalse(left < right);
                Assert.IsTrue(left > right);
            }
        }
    }
}
