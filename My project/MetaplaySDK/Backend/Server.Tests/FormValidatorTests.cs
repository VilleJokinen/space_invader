// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Forms;
using Metaplay.Core.Model;
using Metaplay.Server.Forms;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Metaplay.Server.Tests
{
    [TestFixture]
    class FormValidatorTests
    {
        const string ContainsFailureString = "fail_contains";
        const string LessThanFailureString = "fail_less_than";
        const string ValidFailureString    = "fail_valid";
        const string NotNullFailureString  = "fail_null";
        const string CustomFailureString  = "fail_custom";
        const string RequiredFailureString  = "*required*";

        MetaFormValidationEngine _validationEngine;
        TestValidationClass      _fullyValid;
        TestValidationClass      _partiallyValid;
        TestValidationClass      _allEmpty;

        [SetUp]
        public void Init()
        {
            _validationEngine = new MetaFormValidationEngine();

            _allEmpty = new TestValidationClass();
            _partiallyValid = new TestValidationClass()
            {
                InterfaceTestStringNotEmpty = "notEmpty",
                TestAbstractString          = "",
                TestClass = new TestValidationNestedClass()
                {
                    TestEndTime   = MetaTime.Now - MetaDuration.FromDays(1),
                    TestStartTime = MetaTime.Now + MetaDuration.FromDays(1)
                },
                TestIntProperty = 4,
                TestIntValue    = 5,
                TestStruct      = new TestValidationStruct() {MemberString = "no"},
            };
            _fullyValid = new TestValidationClass()
            {
                InterfaceTestStringNotEmpty = "valid",
                TestAbstractString          = "valid",
                TestClass = new TestValidationNestedClass()
                {
                    TestStartTime = MetaTime.Now + MetaDuration.FromHours(1),
                    TestEndTime   = MetaTime.Now + MetaDuration.FromHours(2),
                },
                TestIntProperty = 9,
                TestIntValue    = 5,
                TestStruct      = new TestValidationStruct() {MemberString = "testValid"},
            };
        }

        public class MetaTestValidateStringContainsAttribute : MetaFormFieldValidatorBaseAttribute
        {
            public class Validator : MetaFormValidator<string>
            {
                readonly MetaTestValidateStringContainsAttribute _attribute;

                public Validator(MetaTestValidateStringContainsAttribute attribute)
                {
                    _attribute = attribute;
                }

                public override void Validate(string fieldOrForm, FormValidationContext ctx)
                {
                    if (string.IsNullOrEmpty(fieldOrForm))
                        return;

                    if (!fieldOrForm.Contains(_attribute.ContainText, StringComparison.InvariantCultureIgnoreCase))
                        ctx.Fail(ContainsFailureString);
                }
            }

            public MetaTestValidateStringContainsAttribute(string containText)
            {
                ContainText = containText;
            }

            public string ContainText { get; set; }

            public override string ValidationRuleName  => "contains";
            public override object ValidationRuleProps => new {ContainText};
            public override Type   CustomValidatorType => typeof(Validator);
        }

        public class ITestValidationClassValidator : MetaFormValidator<ITestValidationClass>
        {
            public override void Validate(ITestValidationClass fieldOrForm, FormValidationContext ctx)
            {
                if (fieldOrForm == null)
                {
                    ctx.Fail("interface_null");
                    return;
                }

                if (string.IsNullOrEmpty(fieldOrForm.InterfaceTestStringNotEmpty))
                    return;

                if (fieldOrForm.InterfaceTestStringNotEmpty != "valid")
                    ctx.Fail(ValidFailureString, nameof(ITestValidationClass.InterfaceTestStringNotEmpty));
            }
        }

        public class TestValidationClassValidator : MetaFormValidator<TestValidationClass>
        {
            public override void Validate(TestValidationClass fieldOrForm, FormValidationContext ctx)
            {
                if (fieldOrForm == null)
                {
                    ctx.Fail("class_null");
                    return;
                }

                if (fieldOrForm.TestIntProperty < fieldOrForm.TestIntValue)
                {
                    ctx.Fail(LessThanFailureString, nameof(TestValidationClass.TestIntProperty));
                    ctx.Fail(LessThanFailureString, nameof(TestValidationClass.TestIntValue));
                }
            }
        }

        public class TestValidationAbstractClassValidator : MetaFormValidator<TestValidationAbstractClass>
        {
            public override void Validate(TestValidationAbstractClass fieldOrForm, FormValidationContext ctx)
            {
                if (fieldOrForm == null)
                {
                    ctx.Fail("abstract_null");
                    return;
                }

                if (fieldOrForm.TestAbstractString == null)
                    return;

                if (fieldOrForm.TestAbstractString != "valid")
                    ctx.Fail(ValidFailureString, nameof(TestValidationAbstractClass.TestAbstractString));
            }
        }

        public class TestValidationNestedClassValidator : MetaFormValidator<TestValidationNestedClass>
        {
            public override void Validate(TestValidationNestedClass fieldOrForm, FormValidationContext ctx)
            {
                if (fieldOrForm == null)
                {
                    ctx.Fail(NotNullFailureString);
                    return;
                }

                if (fieldOrForm.TestEndTime <= fieldOrForm.TestStartTime)
                {
                    ctx.Fail(LessThanFailureString, nameof(TestValidationNestedClass.TestStartTime));
                    ctx.Fail(LessThanFailureString, nameof(TestValidationNestedClass.TestEndTime));
                }
            }
        }

        [MetaSerializable]
        [MetaFormClassValidator(typeof(ITestValidationClassValidator))]
        public interface ITestValidationClass
        {
            [MetaValidateRequired] public string InterfaceTestStringNotEmpty { get; }
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public struct TestValidationStruct
        {
            [MetaTestValidateStringContains("test")] [MetaValidateRequired]
            public string MemberString;
        }

        [MetaSerializable]
        public interface ITestValidationNestedClass
        {
            [MetaValidateInFuture] public MetaTime TestStartTime { get; set; }
            [MetaValidateInFuture] public MetaTime TestEndTime   { get; set; }
        }

        [MetaSerializableDerived(1)]
        [MetaFormClassValidator(typeof(TestValidationNestedClassValidator))]
        public class TestValidationNestedClass : ITestValidationNestedClass
        {
            [MetaMember(1)] public MetaTime TestStartTime { get; set; }
            [MetaMember(2)] public MetaTime TestEndTime   { get; set; }
        }

        [MetaFormClassValidator(typeof(TestValidationAbstractClassValidator))]
        public abstract class TestValidationAbstractClass : ITestValidationClass
        {
            [MetaValidateRequired] public ITestValidationNestedClass TestClass;

            [MetaValidateRequired] public abstract string TestAbstractString { get; set; }

            [MetaMember(101)] public string InterfaceTestStringNotEmpty { get; set; }
        }

        [MetaSerializableDerived(1)]
        [MetaFormClassValidator(typeof(TestValidationClassValidator))]
        [MetaFormClassValidator(typeof(TestValidationClassValidator))] // doubled here to test pruning of duplicates
        public class TestValidationClass : TestValidationAbstractClass
        {
            [MetaMember(1)] public TestValidationStruct TestStruct;

            [MetaMember(2)]
            [MetaValidateInRange(1, 10, CustomFailureString)]
            public int TestIntProperty { get; set; }
            [MetaMember(3)] [MetaValidateInRange(1, 10, CustomFailureString)]
            public int TestIntValue;
            [MetaMember(4)] public override sealed string TestAbstractString { get; set; }
        }

        [Test]
        public void TestTypeRegistryExists()
        {
            Assert.True(MetaFormTypeRegistry.Instance != null);
        }

        [Test]
        public void TestCorrectValidators()
        {
            MetaFormContentType contentType = MetaFormTypeRegistry.Instance.GetTypeSpec(typeof(TestValidationClass));

            Assert.True(contentType is MetaFormClassContentType);
            MetaFormClassContentType classType = (MetaFormClassContentType)contentType;

            Assert.IsNotNull(contentType.Validators.SingleOrDefault(x => x.GetType() == typeof(TestValidationClassValidator)), $"Could not find validator {nameof(TestValidationClassValidator)}");
            Assert.IsNotNull(contentType.Validators.SingleOrDefault(x => x.GetType() == typeof(TestValidationAbstractClassValidator)), $"Could not find validator {nameof(TestValidationAbstractClassValidator)}");
            Assert.IsNotNull(contentType.Validators.SingleOrDefault(x => x.GetType() == typeof(ITestValidationClassValidator)), $"Could not find validator {nameof(ITestValidationClassValidator)}");
            Assert.AreEqual(3, contentType.Validators.Count);

            void AssertMemberValidator(string fieldName, System.Type validatorType)
            {
                Assert.IsNotNull(
                    classType.Members.Single(x => x.FieldName == fieldName)
                        .FieldValidators.Single(x => x.GetType() == validatorType),
                    $"No validator found for member {fieldName} of type {validatorType.Name}");
            }

            AssertMemberValidator("TestIntProperty", typeof(MetaValidateInRangeAttribute.Validator));
            AssertMemberValidator("TestIntValue", typeof(MetaValidateInRangeAttribute.Validator));
            AssertMemberValidator("TestAbstractString", typeof(MetaValidateRequiredAttribute.Validator));
            AssertMemberValidator("InterfaceTestStringNotEmpty", typeof(MetaValidateRequiredAttribute.Validator));

            contentType = MetaFormTypeRegistry.Instance.GetTypeSpec(typeof(TestValidationNestedClass));
            classType   = (MetaFormClassContentType)contentType;

            Assert.IsNotNull(contentType.Validators.SingleOrDefault(x => x.GetType() == typeof(TestValidationNestedClassValidator)), $"Could not find validator {nameof(TestValidationNestedClassValidator)}");
            AssertMemberValidator("TestStartTime", typeof(MetaValidateInFutureAttribute.Validator));
            AssertMemberValidator("TestEndTime", typeof(MetaValidateInFutureAttribute.Validator));

            contentType = MetaFormTypeRegistry.Instance.GetTypeSpec(typeof(TestValidationStruct));
            classType   = (MetaFormClassContentType)contentType;

            AssertMemberValidator("MemberString", typeof(MetaTestValidateStringContainsAttribute.Validator));
        }

        [TestCase(typeof(TestValidationClass), "class_null", "abstract_null", "interface_null")]
        [TestCase(typeof(TestValidationAbstractClass), "abstract_null", "interface_null")]
        [TestCase(typeof(ITestValidationClass), "interface_null")]
        public void TestNull(Type validationType, params string[] expectedNulls)
        {
            ICollection<FormValidationResult> results = _validationEngine.ValidateObject(null, validationType);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(expectedNulls.Length, results.Count);

            foreach (string expectedNull in expectedNulls)
                AssertResultsContainsSingle(results, "", expectedNull);
        }

        [TestCase(typeof(TestValidationClass))]
        [TestCase(typeof(TestValidationAbstractClass))]
        [TestCase(typeof(ITestValidationClass))]
        public void TestFullyValid(Type validationType)
        {
            ICollection<FormValidationResult> results = _validationEngine.ValidateObject(_fullyValid, validationType);
            Assert.IsEmpty(results);
        }

        [TestCase(typeof(TestValidationClass))]
        [TestCase(typeof(TestValidationAbstractClass))]
        [TestCase(typeof(ITestValidationClass))]
        public void TestPartiallyValid(Type validationType)
        {
            ICollection<FormValidationResult> results = _validationEngine.ValidateObject(_partiallyValid, validationType);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(6, results.Count);

            AssertResultsContainsSingle(results, nameof(TestValidationClass.TestIntProperty), LessThanFailureString);
            AssertResultsContainsSingle(results, nameof(TestValidationClass.TestIntValue), LessThanFailureString);
            AssertResultsContainsSingle(results, nameof(TestValidationClass.TestAbstractString), ValidFailureString);
            AssertResultsContainsSingle(results, nameof(TestValidationClass.InterfaceTestStringNotEmpty), ValidFailureString);
            AssertResultsContainsSingle(results, "TestStruct/MemberString", ContainsFailureString);
            AssertResultsContainsSingle(results, nameof(TestValidationClass.TestAbstractString), RequiredFailureString);
        }

        [TestCase(typeof(TestValidationClass))]
        [TestCase(typeof(TestValidationAbstractClass))]
        [TestCase(typeof(ITestValidationClass))]
        public void TestAllEmpty(Type validationType)
        {
            ICollection<FormValidationResult> results = _validationEngine.ValidateObject(_allEmpty, validationType);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(5, results.Count);

            AssertResultsContainsSingle(results, nameof(TestValidationClass.TestIntProperty), CustomFailureString);
            AssertResultsContainsSingle(results, nameof(TestValidationClass.TestIntValue), CustomFailureString);
            AssertResultsContainsSingle(results, nameof(TestValidationClass.TestAbstractString), RequiredFailureString);
            AssertResultsContainsSingle(results, nameof(TestValidationClass.InterfaceTestStringNotEmpty), RequiredFailureString);
            AssertResultsContainsSingle(results, "TestStruct/MemberString", RequiredFailureString);
        }

        public static void AssertResultsContainsSingle(ICollection<FormValidationResult> results, string fieldPath, string failureMessage)
        {
            string regex = "^" + Regex.Escape(failureMessage).Replace("\\?", ".").Replace("\\*", ".*") + "$";
            Assert.DoesNotThrow(
                () =>
                {
                    Assert.IsNotNull(
                        results.SingleOrDefault(x => x.Path == fieldPath && Regex.IsMatch(x.Reason, regex)),
                        $"Failed to find expected failure with path {fieldPath} and message {failureMessage}!");
                },
                $"Results contained a duplicate {fieldPath} and message {failureMessage}!");
        }
    }
}
