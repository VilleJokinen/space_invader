// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Profiling;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Compilation cache for generated tagged serializer.
    /// </summary>
    public static class RoslynSerializerCompileCache
    {
        public const int AbiVersion = 1; // Bump ABI version to force invalidation of cache

        public static void EnsureDllUpToDate(
            string outputDir,
            string dllFileName,
            string errorDir,
            bool enableCaching,
            bool forceRoslyn,
            bool useMemberAccessTrampolines,
            bool generateRuntimeTypeInfo)
        {
            string hashFileName = dllFileName + ".md5";
            string pdbFileName  = dllFileName.Replace(".dll", ".pdb");
            string sourceFileName = dllFileName.Replace(".dll", ".cs");

            string dllFilePath = Path.Combine(outputDir, dllFileName);
            string pdbFilePath = Path.Combine(outputDir, pdbFileName);
            string hashFilePath = Path.Combine(outputDir, hashFileName);

            // Ensure outputDir exists
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Generate serializer source
            TaggedSerializerRoslynGenerator.GeneratedSource generatedSource;
            string sourceHash;
            using (ProfilerScope.Create("RoslynSerializerCompileCache.GenerateSource"))
            {
                generatedSource = TaggedSerializerRoslynGenerator.GenerateSerializerCode(MetaSerializerTypeRegistry.AllTypes, MetaSerializerTypeRegistry.FullProtocolHash,
                    useMemberAccessTrampolines, generateRuntimeTypeInfo);
                string assemblyDeps = string.Join(";", generatedSource.ReferencedAssemblies.Select(assembly => assembly.ToString()).ToArray());
                sourceHash = Util.ComputeMD5(generatedSource.Source) + Invariant($".v{AbiVersion}.deps={assemblyDeps}");
            }

            // If caching is enabled, see if already has the file
            if (enableCaching)
            {
                try
                {
                    // If cached hash matches, use .dll from cache
                    string existingHash = File.ReadAllText(hashFilePath).Trim();
                    if (sourceHash == existingHash)
                    {
                        if (!File.Exists(dllFilePath))
                            throw new FileNotFoundException($"Cached .dll not found");

                        if (!File.Exists(pdbFilePath))
                            throw new FileNotFoundException($"Cached .pdb not found");

                        // Found in cache, return
                        return;
                    }
                }
                catch (Exception)
                {
                }
            }

            try
            {
                // Compile generated source into a .dll
                //System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

#if UNITY_2018_1_OR_NEWER
                bool isUnity = true;
#else
                bool isUnity = false;
#endif

                if (forceRoslyn || !isUnity)
                {
                    using (ProfilerScope.Create("RoslynSerializerCompileCache.RoslynCompiler"))
                    {
                        // On non-Unity platforms (or if forced), use Roslyn directly to build the .dll
                        RoslynCompiler.CompileSource(dllFilePath, pdbFilePath, generatedSource.Source, symbolicFileName: sourceFileName, generatedSource.ReferencedAssemblies);
                    }
                }
                else
                {
#if UNITY_2018_1_OR_NEWER
                    using (ProfilerScope.Create("RoslynSerializerCompileCache.UnityAssemblyBuilder"))
                    {
                        // In Unity, use the AssemblyBuilder as it avoids the overhead of creating another Roslyn instance and can be up to 4x faster!
                        UnityAssemblyBuilder.BuildDll(dllFilePath, generatedSource.Source);
                    }
#else
                    throw new InvalidOperationException("Should not be here!");
#endif
                }
                //DebugLog.Info("Generated serializer build time: {0:0.00}s", sw.ElapsedMilliseconds / 1000.0);

                // Write MD5 hash into file, so can check against it for changes
                File.WriteAllText(hashFilePath, sourceHash);
            }
            catch (Exception ex)
            {
                DebugLog.Error("Failed to compile {0}, writing out {1}/{2}: {3}", dllFileName, errorDir, sourceFileName, ex);
                Directory.CreateDirectory(errorDir);
                File.WriteAllText($"{errorDir}/{sourceFileName}", generatedSource.Source);
                throw;
            }
        }

        public static Assembly GetOrCompileAssembly(string outputDir, string dllFileName, string errorDir, bool useMemberAccessTrampolines)
        {
            EnsureDllUpToDate(
                outputDir,
                dllFileName,
                errorDir,
                enableCaching: true,
                forceRoslyn: false,
                useMemberAccessTrampolines,
                generateRuntimeTypeInfo: false);

            string pdbFileName = dllFileName.Replace(".dll", ".pdb");
            byte[] dllBytes = File.ReadAllBytes(Path.Combine(outputDir, dllFileName));
            byte[] pdbBytes = File.ReadAllBytes(Path.Combine(outputDir, pdbFileName));

            return Assembly.Load(dllBytes, pdbBytes);
        }

        public static void CompileAssembly(string outputDir, string dllFileName, string errorDir, bool useMemberAccessTrampolines, bool generateRuntimeTypeInfo)
        {
            EnsureDllUpToDate(
                outputDir,
                dllFileName,
                errorDir,
                enableCaching: false,
                forceRoslyn: false,
                useMemberAccessTrampolines,
                generateRuntimeTypeInfo);
        }
    }
}
