// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.Tasks;
using Metaplay.Core.Web3;
using NUnit.Framework;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;



namespace Cloud.Serialization.Compilation.Tests
{
    public class SerializationCompilationTests
    {
        string _appDomainDir;

        [OneTimeSetUp]
        public void SetUp()
        {
            // \note Using the AppDomain base directory as that returns the directory where the build outputs
            //       reside in `dotnet test`, Visual Studio, and Rider (unlike `Assembly.GetEntryAssembly()).
            //       It is also more stable than `GetCurrentDirectory()` as the working directory can change.
            _appDomainDir = AppDomain.CurrentDomain.BaseDirectory;
        }

        [MetaSerializable(MetaSerializableFlags.AutomaticConstructorDetection)]
        public class MismatchingTypesDeserializationConstructor
        {
            [MetaMember(1)] public int Test { get; set; }

            public MismatchingTypesDeserializationConstructor(string test) { }
        }

        [MetaSerializable]
        public class IncorrectAttributeDeserializationConstructor
        {
            [MetaMember(1)] public int Test { get; set; }

            [MetaDeserializationConstructor]
            public IncorrectAttributeDeserializationConstructor(string test) { }
        }

        [MetaSerializable]
        public class DuplicateParameterAttributeDeserializationConstructor
        {
            [MetaMember(1)] public int Test { get; set; }
            [MetaMember(2)] public int test { get; set; }

            [MetaDeserializationConstructor]
            public DuplicateParameterAttributeDeserializationConstructor(int test, int Test) { }
        }

        [MetaSerializable]
        public class MultipleConstructorsWithAttributeDeserializationConstructor
        {
            [MetaMember(1)] public int    Test  { get; set; }
            [MetaMember(2)] public string Test2 { get; set; }

            [MetaDeserializationConstructor]
            public MultipleConstructorsWithAttributeDeserializationConstructor(int test, string test2) { }

            [MetaDeserializationConstructor]
            public MultipleConstructorsWithAttributeDeserializationConstructor(string test2, int test) { }
        }

        [MetaSerializable(MetaSerializableFlags.AutomaticConstructorDetection)]
        public class MultipleConstructorsDeserializationConstructor
        {
            [MetaMember(1)] public int    Test  { get; set; }
            [MetaMember(2)] public string Test2 { get; set; }

            public MultipleConstructorsDeserializationConstructor(int test, string test2) { }

            public MultipleConstructorsDeserializationConstructor(string test2, int test) { }
        }

        [Test]
        public void TestMismatchedConstructorParams()
        {
            Assert.That(
                () =>
                {
                    List<Type> typesToSerialize = new List<Type>() {typeof(MismatchingTypesDeserializationConstructor)};
                    InitializeAndCompile(typesToSerialize);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo($"Could not find matching constructor for type '{typeof(MismatchingTypesDeserializationConstructor).FullName}'. Constructor parameters must have the same name and type as the MetaMembers defined in this type."));
        }

        void InitializeAndCompile(List<Type> typesToSerialize)
        {
            MetaSerializerTypeRegistry.OverrideTypeScanner(new TestSerializerTypeScanner(typesToSerialize));
            Assembly _ = RoslynSerializerCompileCache.GetOrCompileAssembly(
                outputDir: _appDomainDir,
                dllFileName: "Metaplay.Generated.Test.dll",
                errorDir: Path.Join(_appDomainDir, "Errors"),
                useMemberAccessTrampolines: false);
        }

        [Test]
        public void TestConstructorAttributeHasInvalidSignature()
        {
            Assert.That(
                () =>
                {
                    List<Type> typesToSerialize = new List<Type>() {typeof(IncorrectAttributeDeserializationConstructor)};
                    InitializeAndCompile(typesToSerialize);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo($"Constructor in '{typeof(IncorrectAttributeDeserializationConstructor).FullName}' with attribute {nameof(MetaDeserializationConstructorAttribute)} does not have a valid signature. Constructor parameters must have the same name and type as the MetaMembers defined in this type."));
        }

        [Test]
        public void TestConstructorAttributeHasDuplicateNames()
        {
            Assert.That(
                () =>
                {
                    List<Type> typesToSerialize = new List<Type>() {typeof(DuplicateParameterAttributeDeserializationConstructor)};
                    InitializeAndCompile(typesToSerialize);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo($"Constructor in '{typeof(DuplicateParameterAttributeDeserializationConstructor).FullName}' has duplicate case-insensitive parameters, this is not supported for constructor based deserialization."));
        }

        [Test]
        public void TestMultipleConstructorsWithAttributeDeserializationConstructor()
        {
            Assert.That(
                () =>
                {
                    List<Type> typesToSerialize = new List<Type>() {typeof(MultipleConstructorsWithAttributeDeserializationConstructor)};
                    InitializeAndCompile(typesToSerialize);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo($"Type '{typeof(MultipleConstructorsWithAttributeDeserializationConstructor).FullName} has multiple constructors with the {nameof(MetaDeserializationConstructorAttribute)} attribute, this is not valid. Please ensure there is only one {nameof(MetaDeserializationConstructorAttribute)} attribute."));
        }

        [Test]
        public void TestMultipleConstructorsDeserializationConstructor()
        {
            Assert.That(
                () =>
                {
                    List<Type> typesToSerialize = new List<Type>() {typeof(MultipleConstructorsDeserializationConstructor)};
                    InitializeAndCompile(typesToSerialize);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo($"Multiple valid deserialization constructors found for '{typeof(MultipleConstructorsDeserializationConstructor).FullName}', please specify the correct constructor using [MetaDeserializationConstructor]."));
        }
    }
}
