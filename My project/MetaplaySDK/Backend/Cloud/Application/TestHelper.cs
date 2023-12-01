// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Serialization;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Metaplay.Cloud.Application
{
    public static class TestHelper
    {
        /// <summary>
        /// Initializes Metaplay & serialization. Sets the working directory to the current test project's
        /// root (eg, MetaplaySDK/Backend/Cloud.Tests/ or MetaplaySDK/Backend/Server.Tests/) to make file accesses be able to use canonical paths.
        /// This is needed because running the tests from different environments (eg, Visual Studio or command
        /// line) results in different initial working directories.
        /// </summary>
        public static void SetupForTests(IMetaSerializerTypeInfoProvider overrideTypeInfoProvider = null)
        {
            // Pipe serilog to Console. This will be then captured by the test framework.
            Serilog.Log.Logger =
                new LoggerConfiguration()
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture, theme: ConsoleTheme.None)
                .CreateLogger();

            // Initialize Metaplay core
            MetaplayCore.Initialize(overrideTypeInfoProvider);

            // Initialize serializer
            // \note Using the AppDomain base directory as that returns the directory where the build outputs
            //       reside in `dotnet test`, Visual Studio, and Rider (unlike `Assembly.GetEntryAssembly()).
            //       It is also more stable than `GetCurrentDirectory()` as the working directory can change.
            string appDomainDir = AppDomain.CurrentDomain.BaseDirectory;
            Assembly assembly = RoslynSerializerCompileCache.GetOrCompileAssembly(
                outputDir:                  appDomainDir,
                dllFileName:                "Metaplay.Generated.Test.dll",
                errorDir:                   Path.Join(appDomainDir, "Errors"),
                useMemberAccessTrampolines: false);

            Type serializerType = assembly.GetType("Metaplay.Generated.TypeSerializer");
            MetaSerialization.Initialize(serializerType, actorSystem: null);

            // Change directory to project directory (eg, MetaplaySDK/Backend/Cloud.Tests/ or MetaplaySDK/Backend/Server.Tests/) to enable relative file
            // paths to work properly. Depending on the environment where the tests are run, the build outputs may
            // be in slightly differing directories, so we search the 'bin/' path here to find the right location.
            string      fullPath    = Path.GetFullPath(appDomainDir);
            string[]    parts       = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            int         binIndex    = Array.IndexOf(parts, "bin");
            if (binIndex == -1)
                throw new InvalidOperationException($"The AppDomain.BaseDirectory path '{appDomainDir}' does not contain the directory 'bin/' in it -- unable to figure out what working directory to use for the tests!");
            string runDir = string.Join(Path.DirectorySeparatorChar, parts.Take(binIndex).ToArray());
            Log.Logger.Information("Changing working directory to {WorkingDirectory}", runDir);
            Directory.SetCurrentDirectory(runDir);
        }
    }

    public class TestSerializerTypeScanner : MetaSerializerTypeScanner
    {
        readonly List<Type>   _typesToScan;

        public TestSerializerTypeScanner(List<Type> typesToScan)
        {
            _typesToScan         = typesToScan;
        }

        protected override bool ShouldSkipTypeInScanning(Type type)
        {
            return (_typesToScan != null && !_typesToScan.Contains(type));
        }
    }
}
