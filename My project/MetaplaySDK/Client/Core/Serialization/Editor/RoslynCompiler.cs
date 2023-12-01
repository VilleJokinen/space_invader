// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
// #define METAPLAY_DEBUG_GENERATED_SOURCE

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

#if UNITY_2018_1_OR_NEWER
using UnityEditor.Compilation;
#endif

// Avoid conflicting SyntaxTree
using CodeAnalysisSyntaxTree = Microsoft.CodeAnalysis.SyntaxTree;

namespace Metaplay.Core.Serialization
{
    public static class RoslynCompiler
    {
        public static void CompileSource(string dllFilePath, string pdbFilePath, string source, string symbolicFileName, IEnumerable<System.Reflection.Assembly> assemblyReferences)
        {
            SourceText sourceText = SourceText.From(source, Encoding.UTF8);
            CodeAnalysisSyntaxTree ast = CSharpSyntaxTree.ParseText(sourceText, path: symbolicFileName);

            string assemblyDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            List<MetadataReference> refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location), // System.Private.CoreLib.dll
#if NETCOREAPP // cloud
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location), // netstandard.dll
                MetadataReference.CreateFromFile(Path.Combine(assemblyDir, "mscorlib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyDir, "System.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyDir, "System.Core.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyDir, "System.Collections.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyDir, "System.Runtime.dll")),
#elif UNITY_2018_1_OR_NEWER
                MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location), // netstandard.dll
                MetadataReference.CreateFromFile(typeof(ArrayList).Assembly.Location), // System.Collections.dll
                MetadataReference.CreateFromFile(typeof(SortedSet<>).Assembly.Location), // System.Collections.Generic.dll
                MetadataReference.CreateFromFile(typeof(string).Assembly.Location), // System.Runtime.dll
#endif
            };

            foreach (System.Reflection.Assembly assembly in assemblyReferences)
                refs.Add(MetadataReference.CreateFromFile(assembly.Location));

            CSharpCompilationOptions options = new CSharpCompilationOptions(
                outputKind:             OutputKind.DynamicallyLinkedLibrary,
                metadataImportOptions:  MetadataImportOptions.All,
                optimizationLevel:      OptimizationLevel.Release,
                platform:               Platform.AnyCpu
                );

            PropertyInfo topLevelBinderFlagsProperty = typeof(CSharpCompilationOptions).GetProperty("TopLevelBinderFlags", BindingFlags.Instance | BindingFlags.NonPublic);
            topLevelBinderFlagsProperty.SetValue(options, 1u << 22); // set BinderFlags.IgnoreAccessibility

            CSharpCompilation compilation = CSharpCompilation.Create(
                "Metaplay.Generated",
                new[] { ast },
                refs,
                options);

            using (MemoryStream dllStream = new MemoryStream())
            using (MemoryStream pdbStream = new MemoryStream())
            {
                List<EmbeddedText> embeddedTexts = null;

#if METAPLAY_DEBUG_GENERATED_SOURCE
                embeddedTexts = new List<EmbeddedText>
                {
                    EmbeddedText.FromSource(symbolicFileName, sourceText)
                };
#endif
                EmitResult result = compilation.Emit(
                    dllStream,
                    pdbStream,
                    options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
                    embeddedTexts: embeddedTexts);

                if (result.Success)
                {
                    // Write .dll and .pdb files
                    File.WriteAllBytes(dllFilePath, dllStream.ToArray());
                    File.WriteAllBytes(pdbFilePath, pdbStream.ToArray());
                }
                else
                {
                    string diagnosticsStr = string.Join("\n", result.Diagnostics.Select(diag => Util.ObjectToStringInvariant(diag)));
                    throw new InvalidOperationException($"Failed to compile generated serializer due to the following errors:\n{diagnosticsStr}");
                }
            }
        }
    }

#if UNITY_2018_1_OR_NEWER
    public static class UnityAssemblyBuilder
    {
        public static void BuildDll(string dllFilePath, string sourceCode)
        {
            // Write out the source into a file (required by Unity's AssemblyBuilder)
            string sourcePath = dllFilePath.Replace(".dll", ".cs");
            File.WriteAllBytes(sourcePath, Encoding.UTF8.GetBytes(sourceCode));

            // Build the .dll from the source files (also generates .pdb)
            CompilerMessage[] buildFailMessages = null; // compiler messages in case build fails
            AssemblyBuilder builder = new AssemblyBuilder(dllFilePath, new string[] { sourcePath });
            builder.compilerOptions.AllowUnsafeCode = false;
            builder.compilerOptions.CodeOptimization = CodeOptimization.Debug; // \note Only used in Editor so opt for faster build times
            builder.referencesOptions = ReferencesOptions.None;
            builder.buildFinished += (string assemblyPath, CompilerMessage[] compilerMessages) =>
            {
                // If errors occurred, throw an exception
                int numErrors = compilerMessages.Count(m => m.type == CompilerMessageType.Error);
                if (numErrors != 0)
                    buildFailMessages = compilerMessages;
            };

            if (!builder.Build())
                throw new InvalidOperationException($"Failed to start build of Metaplay serializer generated code {dllFilePath}!");

            // Wait until build is finished
            while (builder.status != AssemblyBuilderStatus.Finished)
                Thread.Sleep(1);

            // Handle build failures
            if (buildFailMessages != null)
            {
                int numErrors = buildFailMessages.Count(m => m.type == CompilerMessageType.Error);
                int numWarnings = buildFailMessages.Count(m => m.type == CompilerMessageType.Warning);

                string errorsStr = string.Join("\n", buildFailMessages.Select(msg => msg.message));
                throw new InvalidOperationException($"Failed to compile generated serializer, got {numErrors} errors and {numWarnings} warnings:\n{errorsStr}");
            }
        }
    }
#endif // Unity
}
