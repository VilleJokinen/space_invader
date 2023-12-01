// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Cloud.RuntimeOptions
{
    /// <summary>
    /// Environment family is used to configure environment-dependent default values for various Runtime Options.
    /// There can be multiple individual environments in each family (eg, dev-0, dev-1 can belong to family Development).
    /// </summary>
    public enum EnvironmentFamily
    {
        Local,
        Development,
        Staging,
        Production
    };

    /// <summary>
    /// Mark a class as containing RuntimeOptions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RuntimeOptionsAttribute : Attribute
    {
        public readonly string  SectionName;        // Name of the section in the config files
        public readonly string  SectionDescription; // Description of the section
        public readonly bool    IsStatic;           // Is the options block static (i.e., cannot be updated during runtime)

        public RuntimeOptionsAttribute(string sectionName, bool isStatic, string sectionDescription = null)
        {
            SectionName        = sectionName ?? throw new ArgumentNullException(nameof(sectionName));
            SectionDescription = sectionDescription;
            IsStatic           = isStatic;
        }
    }

    /// <summary>
    /// Define a command line alias for the given RuntimeOption.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class CommandLineAliasAttribute : Attribute
    {
        public readonly string Alias;

        public CommandLineAliasAttribute(string alias) { Alias = alias; }
    }

    /// <summary>
    /// Define the RuntimeOption as Computed. Computed values cannot be assigned
    /// from runtime options and instead are computed from other runtime options.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ComputedValueAttribute : Attribute
    {
    }

    /// <summary>
    /// Base class for declaring a section of RuntimeOptions. The deriving concrete
    /// class should also be annotated with <see cref="RuntimeOptionsAttribute"/>.
    /// </summary>
    public abstract class RuntimeOptionsBase
    {
        public static bool IsServerApplication      => RuntimeOptionsRegistry.Instance.ApplicationName == "Server";
        public static bool IsBotClientApplication   => RuntimeOptionsRegistry.Instance.ApplicationName == "BotClient";

        public static bool IsLocalEnvironment       => RuntimeOptionsRegistry.Instance.EnvironmentFamily == EnvironmentFamily.Local;
        public static bool IsDevelopmentEnvironment => RuntimeOptionsRegistry.Instance.EnvironmentFamily == EnvironmentFamily.Development;
        public static bool IsStagingEnvironment     => RuntimeOptionsRegistry.Instance.EnvironmentFamily == EnvironmentFamily.Staging;
        public static bool IsProductionEnvironment  => RuntimeOptionsRegistry.Instance.EnvironmentFamily == EnvironmentFamily.Production;
        public static bool IsCloudEnvironment       => !IsLocalEnvironment;

        [IgnoreDataMember]
        protected IMetaLogger Log { get; }

        protected RuntimeOptionsBase()
        {
            Log = MetaLogger.ForContext(GetType());
        }

        /// <summary>
        /// Async on-loaded handler for ensuring that the options section is fully populated. Useful
        /// for resolving secrets or other external data.
        /// </summary>
        /// <returns></returns>
        public virtual Task OnLoadedAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Set of sources (files, environment variables, command line) for a given <see cref="RuntimeOptionsRegistry"/>.
    /// Also creates a per-source <see cref="IConfigurationRoot"/> for the purposes of tracking which members were
    /// defined in each source. This is quite a hack and there's the sources and values may temporarily mismatch
    /// when options are updated.
    /// </summary>
    public class RuntimeOptionsSourceSet
    {
        public struct SourceDeclaration
        {
            public readonly string                          Name;
            public readonly string                          Description;
            public readonly bool                            StrictBindingChecks;
            public readonly Action<ConfigurationBuilder>    RegisterFunc;

            public SourceDeclaration(string name, string description, bool strictBindingChecks, Action<ConfigurationBuilder> registerFunc)
            {
                Name = name;
                Description = description;
                StrictBindingChecks = strictBindingChecks;
                RegisterFunc = registerFunc;
            }
        }

        public class Source
        {
            public readonly string              Name;
            public readonly string              Description;
            public readonly bool                StrictBindingChecks;
            public readonly IConfigurationRoot  ConfigRoot;

            public bool TolerateUnknownFields => !StrictBindingChecks;
            public bool TolerateEmptyObjectValues => !StrictBindingChecks;

            public Source(string name, string description, bool strictBindingChecks, IConfigurationRoot configRoot)
            {
                Name                    = name;
                Description             = description;
                StrictBindingChecks     = strictBindingChecks;
                ConfigRoot              = configRoot;
            }
        }

        ConfigurationBuilder    _globalBuilder  = new ConfigurationBuilder();
        List<Source>            _sources        = new List<Source>();

        public IEnumerable<Source> Sources => _sources;

        public RuntimeOptionsSourceSet()
        {
        }

        public Source AddSource(SourceDeclaration declaration)
        {
            Serilog.Log.Information("Reading options {0}.. (defined in {1})", declaration.Name, declaration.Description);

            // Register to combined builder
            declaration.RegisterFunc(_globalBuilder);

            // Create individual builder
            ConfigurationBuilder builder = new ConfigurationBuilder();
            declaration.RegisterFunc(builder);
            IConfigurationRoot configRoot = builder.Build();
            Source source = new Source(declaration.Name, declaration.Description, declaration.StrictBindingChecks, configRoot);
            _sources.Add(source);
            return source;
        }

        public IConfigurationRoot BuildGlobalConfig() =>
            _globalBuilder.Build();
    }

    /// <summary>
    /// Registry for registering and accessing all the options classes. Discovers the classes via
    /// reflection, manages the updating due to changes in sources, and allows querying the current
    /// versions of each.
    /// </summary>
    public class RuntimeOptionsRegistry
    {
        public static RuntimeOptionsRegistry Instance { get; set; }

        static IMetaLogger _log = MetaLogger.ForContext<RuntimeOptionsRegistry>();

        const string DefaultOptionsPaths = "Config/Options.base.yaml;Config/Options.local.yaml";

        const string DefaultValue = "Default";
        const string ComputedValue = "Computed";

        class Entry
        {
            public readonly Type                        Type;
            public readonly RuntimeOptionsAttribute     Attribute;

            // Dynamic members, accessed under lock
            public string                               ContentHash     = null; // Hash of the current content, for filtering spurious updates
            public RuntimeOptionsBase                   Options         = null; // Current Options values
            public OrderedDictionary<string, string>    ValueSources    = null; // Source for each option value
            public OrderedDictionary<string, string>    Descriptions    = null; // Sparsely populated dictionary of descriptions for each option value

            // Helpers
            public string SectionName        => Attribute.SectionName;
            public string SectionDescription => Attribute.SectionDescription;

            public Entry(Type type, RuntimeOptionsAttribute attribute)
            {
                Type        = type ?? throw new ArgumentNullException(nameof(type));
                Attribute   = attribute ?? throw new ArgumentNullException(nameof(attribute));
            }

            public override string ToString() => $"RuntimeOptions.Entry(section={SectionName})";

            public class OptionValue
            {
                public readonly string  Name;
                public readonly string  Description;
                public readonly string  CommandLineAlias;
                public readonly bool    IsComputedValue;

                public OptionValue(string name, string description, string commandLineAlias, bool isComputedValue)
                {
                    Name = name;
                    Description = description;

                    if (commandLineAlias != null)
                    {
                        if (commandLineAlias.Length < 2 || commandLineAlias[0] != '-' || commandLineAlias[1] == '-')
                            throw new InvalidOperationException($"invalid command line format: {commandLineAlias}");
                        CommandLineAlias = commandLineAlias.Substring(1);
                    }
                    else
                        CommandLineAlias = null;

                    IsComputedValue = isComputedValue;
                }
            }

            public List<OptionValue> GetValues()
            {
                List<OptionValue> values = new List<OptionValue>();
                foreach (PropertyInfo prop in Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    string description;
                    if (prop.GetCustomAttribute<MetaDescriptionAttribute>() is MetaDescriptionAttribute descriptionAttr)
                        description = descriptionAttr.Description;
                    else
                        description = null;

                    string alias;
                    if (prop.GetCustomAttribute<CommandLineAliasAttribute>() is CommandLineAliasAttribute aliasAttr)
                        alias = aliasAttr.Alias;
                    else
                        alias = null;

                    bool isComputed;
                    if (prop.GetSetMethodOnDeclaringType() == null)
                        isComputed = true;
                    else if (prop.GetCustomAttribute<ComputedValueAttribute>() != null)
                        isComputed = true;
                    else
                        isComputed = false;
                    values.Add(new OptionValue(prop.Name, description, alias, isComputed));
                }
                return values;
            }
        }

        public bool                     IsRunningInContainer    { get; }
        public string                   ApplicationName         { get; }
        public EnvironmentFamily        EnvironmentFamily       { get; }

        RuntimeOptionsSourceSet         _sourceSet      = new RuntimeOptionsSourceSet();
        object                          _lock           = new object();
        readonly List<Entry>            _entries;

        AsyncManualResetEvent           _updatedEvent   = new AsyncManualResetEvent();

        public MetaTime                 _lastUpdateTime         { get; private set; }

        public RuntimeOptionsRegistry(string applicationName, string[] cmdLineArgs)
        {
            // Store application name
            if (string.IsNullOrEmpty(applicationName))
                throw new ArgumentException("Must provide valid application name", nameof(applicationName));
            ApplicationName = applicationName;

            // Find all classes deriving from RuntimeOptionsBase
            _entries = ResolveEntriesFromTypes();

            // Parse command line and environment
            CommandLineRuntimeOptions commandLineOptions = ParseCommandLineOrDie(cmdLineArgs);
            EnvironmentRuntimeOptionsSource environmentOptions = EnvironmentRuntimeOptionsSource.Parse(prefix: "Metaplay_");

            // Check whether running inside a container
            IsRunningInContainer = bool.TryParse(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), out bool inContainer) && inContainer;

            // Figure out current environment family (for environment-dependent default values)
            string envFamilyStr = Environment.GetEnvironmentVariable("METAPLAY_ENVIRONMENT_FAMILY");
            if (IsRunningInContainer && string.IsNullOrEmpty(envFamilyStr))
                throw new InvalidOperationException("The METAPLAY_ENVIRONMENT_FAMILY value must be defined when running in a container! You probably need to upgrade to the latest Helm chart.");
            EnvironmentFamily = string.IsNullOrEmpty(envFamilyStr) ? EnvironmentFamily.Local : EnumUtil.Parse<EnvironmentFamily>(envFamilyStr);

            _log.Information("Initializing RuntimeOptions with IsRunningInContainer={IsRunningInContainer}, ApplicationName={ApplicationName}, EnvironmentFamily={EnvironmentFamily}..", IsRunningInContainer, applicationName, EnvironmentFamily);

            // Configure options sources to use
            foreach (RuntimeOptionsSourceSet.SourceDeclaration declaration in GetOptionDeclarations(commandLineOptions, environmentOptions))
                _sourceSet.AddSource(declaration);

            // Update loop (triggered on each change to sources). Using a separate background "thread" with
            // signaling to ensure that only a single update action can be in-flight at a time.
            // \note Cancelation is not supported, since this is intended to live for the full lifecycle of the app.
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    // Wait for next update event
                    await _updatedEvent.WaitAsync().ConfigureAwait(false);
                    _updatedEvent.Reset();

                    // Perform the update of Options classes
                    try
                    {
                        await UpdateOptions(isInitial: false).ConfigureAwait(false);
                        _lastUpdateTime = MetaTime.Now;
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Failed to update RuntimeOptions:\n{Exception}", ex);
                    }
                }
            });

            // Register handler for configuration files changes to signal the updater above
            IConfigurationRoot configRoot = _sourceSet.BuildGlobalConfig();
            ChangeToken.OnChange(
                () => configRoot.GetReloadToken(),
                () => _updatedEvent.Set());
        }

        static List<Entry> ResolveEntriesFromTypes()
        {
            List<Entry> entries = new List<Entry>();

            // Resolve all classes deriving from RuntimeOptions
            foreach (Type type in TypeScanner.GetAllTypes())
            {
                if (type.IsClass && !type.IsAbstract && type.IsDerivedFrom<RuntimeOptionsBase>())
                {
                    // Store the entry
                    RuntimeOptionsAttribute attrib = type.GetCustomAttribute<RuntimeOptionsAttribute>();
                    if (attrib == null)
                        throw new InvalidOperationException($"Invalid RuntimeOption {type.ToNamespaceQualifiedTypeString()}. Type must be annotated with a RuntimeOptionsAttribute.");

                    // Check the type is well-behaving
                    RuntimeOptionsBinder.CheckRuntimeOptionsIsParseable(type);

                    if (!type.IsMetaFeatureEnabled())
                        continue;

                    entries.Add(new Entry(type, attrib));
                }
                else
                {
                    RuntimeOptionsAttribute strayAttrib = type.GetCustomAttribute<RuntimeOptionsAttribute>();
                    if (strayAttrib != null)
                        throw new InvalidOperationException($"Invalid RuntimeOption attribute on {type.ToNamespaceQualifiedTypeString()}. Runtime options must be a concrete class that inherits RuntimeOptionsBase.");
                }
            }

            return entries;
        }

        public static async Task InitializeAsync(string applicationName, string[] cmdLineArgs)
        {
            // Initialize options registry
            Instance = new RuntimeOptionsRegistry(applicationName, cmdLineArgs);

            // Initialize options with initial values
            await Instance.UpdateOptions(isInitial: true).ConfigureAwait(false);
        }

        /// <summary>
        /// Update all the current options blocks from sources. Called at start (isInitial=true) and
        /// whenever any of the sources change (isInitial=false).
        ///
        /// Duplicate updates are skipped on per-block level by hash comparisons. The options are only
        /// updated once the call to <see cref="RuntimeOptionsBase.OnLoadedAsync"/> has successfully
        /// completed. If the callback fails, it is retried a few times before giving up.
        /// </summary>
        /// <param name="isInitial"></param>
        /// <returns></returns>
        async Task UpdateOptions(bool isInitial)
        {
            // Bind values to a new set of runtime options
            RuntimeOptionsBinder.BindingResults results = BindConfigToOptions();

            if (results.Errors.Length > 0)
            {
                // Binding errors mean the configuration is broken. If initial config, halt immediately. For update, tolerate
                // and try again when the underlying file/source is updated. When that happens, this method will get called again.
                // Warnings might be useful, so print them too.

                foreach (Exception ex in results.Errors)
                    _log.Error("{Error}", ex);
                foreach (RuntimeOptionsBinder.Warning warning in results.Warnings)
                    _log.Warning("{Warning}", warning.Message);

                _log.Error($"Could not parse application configuration.");

                if (isInitial)
                {
                    Application.Application.ForceTerminate(SysexitsExUsage, "Invalid options given");
                    return; // unreachable
                }

                // Update failed. Try again later.
                return;
            }

            // Handle all warnings. If warnings are marked as fatal or if the warning source is the command line, kill the
            // application but only on launch. Killing application when it's running would be unexpected behavior.

            foreach (RuntimeOptionsBinder.Warning warning in results.Warnings)
                _log.Warning("{Warning}", warning.Message);

            if (isInitial && results.Warnings.Any(warning => warning.Source.Name == "CLI"))
            {
                PrintCliUsageErrorAndExitNoReturn(results.Warnings.Where(warning => warning.Source.Name == "CLI").Select(warning => warning.Message).ToArray());
                return; // unreachable
            }
            if (isInitial && results.Warnings.Length > 0 && ((EnvironmentOptions)results.Sections[typeof(EnvironmentOptions)].Options).ExitOnUnknownOptions)
            {
                _log.Error($"Unknown options were found and Environment:{nameof(EnvironmentOptions.ExitOnUnknownOptions)} is true; exiting.");
                Application.Application.ForceTerminate(SysexitsExUsage, "Unknown options given");
                return; // unreachable
            }

            // Values are bound to the runtime options, but they are not yet loaded. Load entries that need updating

            List<Entry> invalidatedEntries = new List<Entry>();
            foreach (Entry entry in _entries)
            {
                RuntimeOptionsBinder.BindingResults.Section section = results.Sections[entry.Type];

                // Skip static on update
                if (entry.Attribute.IsStatic && !isInitial)
                    continue;

                // Skip if contents haven't changed
                if (!isInitial && section.ContentHash == entry.ContentHash)
                    continue;

                invalidatedEntries.Add(entry);
            }

            // Load entries.
            await Task.WhenAll(invalidatedEntries.Select(async entry =>
            {
                RuntimeOptionsBinder.BindingResults.Section section = results.Sections[entry.Type];

                // Resolve sources for options
                // \note Sources are only resolved for the top-level members of the Options block
                OrderedDictionary<string, string> memberSources = new OrderedDictionary<string, string>();
                foreach (Entry.OptionValue value in entry.GetValues())
                {
                    string srcName = DefaultValue;

                    if (value.IsComputedValue)
                    {
                        // Read-only properties are computed values (derived data)
                        srcName = ComputedValue;
                    }
                    else if (section.MappingsSources.TryGetValue($"{entry.SectionName}:{value.Name}", out RuntimeOptionsSourceSet.Source source))
                    {
                        srcName = source.Name;
                    }

                    memberSources.Add(value.Name, srcName);
                }

                // Resolve descriptions for options
                // \note The resulting list is sparsely populated. Properties without descriptions do not have an entry
                OrderedDictionary<string, string> memberDescriptions = new OrderedDictionary<string, string>();
                foreach (Entry.OptionValue value in entry.GetValues())
                {
                    if (!string.IsNullOrEmpty(value.Description))
                        memberDescriptions.Add(value.Name, value.Description);
                }

                // Invoke on-loaded handler
                const int NumTries = 5;
                for (int tryNdx = 0; tryNdx < NumTries; tryNdx++)
                {
                    try
                    {
                        // Invoke the handler
                        await section.Options.OnLoadedAsync().ConfigureAwait(false);

                        // Set as current & store hash
                        lock (_lock)
                        {
                            entry.Options = section.Options;
                            entry.ValueSources = memberSources;
                            entry.Descriptions = memberDescriptions;
                            entry.ContentHash = section.ContentHash;
                        }

                        // Exit loop
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (tryNdx < NumTries - 1)
                        {
                            _log.Warning("{OptionsName}.OnLoadedAsync() failed, retrying..\n{Exception}", entry.Type.Name, ex);

                            // Wait a bit before trying again
                            await Task.Delay(1_000);
                        }
                        else
                        {
                            // Print with the partially computed values
                            _log.Information("{OptionsName}.OnLoadedAsync() failed too many times. Contents: {Options}", entry.Type.Name, PrettyPrint.Verbose(section.Options));

                            // On final try, re-throw
                            throw;
                        }
                    }
                }

                // Log contents after computed values have been computed
                if (isInitial)
                    _log.Information("Initial {Options}", PrettyPrint.Verbose(section.Options));
                else
                    _log.Information("Updating {Options}", PrettyPrint.Verbose(section.Options));
            }));
        }

        RuntimeOptionsBinder.BindingResults BindConfigToOptions()
        {
            List<RuntimeOptionsBinder.RuntimeOptionDefinition> definitions = new List<RuntimeOptionsBinder.RuntimeOptionDefinition>();
            foreach (Entry entry in _entries)
                definitions.Add(new RuntimeOptionsBinder.RuntimeOptionDefinition(entry.Type, entry.SectionName));
            return RuntimeOptionsBinder.BindToRuntimeOptions(definitions, _sourceSet);
        }

        /// <summary>
        /// Get the current version of a given options block. Every time this method is called,
        /// the underlying value may have changed, so only call this once for each logical block
        /// and keep copy of the returned <typeparamref name="TOptions"/> reference.
        /// </summary>
        /// <typeparam name="TOptions"></typeparam>
        /// <returns></returns>
        public TOptions GetCurrent<TOptions>() where TOptions : RuntimeOptionsBase
        {
            lock (_lock)
            {
                return (TOptions)_entries.Single(e => e.Type == typeof(TOptions)).Options;
            }
        }

        public (TOptions1, TOptions2) GetCurrent<TOptions1, TOptions2>() where TOptions1 : RuntimeOptionsBase where TOptions2 : RuntimeOptionsBase
        {
            lock (_lock)
            {
                TOptions1 opts1 = (TOptions1)_entries.Single(e => e.Type == typeof(TOptions1)).Options;
                TOptions2 opts2 = (TOptions2)_entries.Single(e => e.Type == typeof(TOptions2)).Options;
                return (opts1, opts2);
            }
        }

        public (TOptions1, TOptions2, TOptions3) GetCurrent<TOptions1, TOptions2, TOptions3>() where TOptions1 : RuntimeOptionsBase where TOptions2 : RuntimeOptionsBase where TOptions3 : RuntimeOptionsBase
        {
            lock (_lock)
            {
                TOptions1 opts1 = (TOptions1)_entries.Single(e => e.Type == typeof(TOptions1)).Options;
                TOptions2 opts2 = (TOptions2)_entries.Single(e => e.Type == typeof(TOptions2)).Options;
                TOptions3 opts3 = (TOptions3)_entries.Single(e => e.Type == typeof(TOptions3)).Options;
                return (opts1, opts2, opts3);
            }
        }

        public (TOptions1, TOptions2, TOptions3, TOptions4) GetCurrent<TOptions1, TOptions2, TOptions3, TOptions4>() where TOptions1 : RuntimeOptionsBase where TOptions2 : RuntimeOptionsBase where TOptions3 : RuntimeOptionsBase where TOptions4 : RuntimeOptionsBase
        {
            lock (_lock)
            {
                TOptions1 opts1 = (TOptions1)_entries.Single(e => e.Type == typeof(TOptions1)).Options;
                TOptions2 opts2 = (TOptions2)_entries.Single(e => e.Type == typeof(TOptions2)).Options;
                TOptions3 opts3 = (TOptions3)_entries.Single(e => e.Type == typeof(TOptions3)).Options;
                TOptions4 opts4 = (TOptions4)_entries.Single(e => e.Type == typeof(TOptions4)).Options;
                return (opts1, opts2, opts3, opts4);
            }
        }

        public (TOptions1, TOptions2, TOptions3, TOptions4, TOptions5) GetCurrent<TOptions1, TOptions2, TOptions3, TOptions4, TOptions5>() where TOptions1 : RuntimeOptionsBase where TOptions2 : RuntimeOptionsBase where TOptions3 : RuntimeOptionsBase where TOptions4 : RuntimeOptionsBase where TOptions5 : RuntimeOptionsBase
        {
            lock (_lock)
            {
                TOptions1 opts1 = (TOptions1)_entries.Single(e => e.Type == typeof(TOptions1)).Options;
                TOptions2 opts2 = (TOptions2)_entries.Single(e => e.Type == typeof(TOptions2)).Options;
                TOptions3 opts3 = (TOptions3)_entries.Single(e => e.Type == typeof(TOptions3)).Options;
                TOptions4 opts4 = (TOptions4)_entries.Single(e => e.Type == typeof(TOptions4)).Options;
                TOptions5 opts5 = (TOptions5)_entries.Single(e => e.Type == typeof(TOptions5)).Options;
                return (opts1, opts2, opts3, opts4, opts5);
            }
        }

        public (TOptions1, TOptions2, TOptions3, TOptions4, TOptions5, TOptions6) GetCurrent<TOptions1, TOptions2, TOptions3, TOptions4, TOptions5, TOptions6>() where TOptions1 : RuntimeOptionsBase where TOptions2 : RuntimeOptionsBase where TOptions3 : RuntimeOptionsBase where TOptions4 : RuntimeOptionsBase where TOptions5 : RuntimeOptionsBase where TOptions6 : RuntimeOptionsBase
        {
            lock (_lock)
            {
                TOptions1 opts1 = (TOptions1)_entries.Single(e => e.Type == typeof(TOptions1)).Options;
                TOptions2 opts2 = (TOptions2)_entries.Single(e => e.Type == typeof(TOptions2)).Options;
                TOptions3 opts3 = (TOptions3)_entries.Single(e => e.Type == typeof(TOptions3)).Options;
                TOptions4 opts4 = (TOptions4)_entries.Single(e => e.Type == typeof(TOptions4)).Options;
                TOptions5 opts5 = (TOptions5)_entries.Single(e => e.Type == typeof(TOptions5)).Options;
                TOptions6 opts6 = (TOptions6)_entries.Single(e => e.Type == typeof(TOptions6)).Options;
                return (opts1, opts2, opts3, opts4, opts5, opts6);
            }
        }

        public (TOptions1, TOptions2, TOptions3, TOptions4, TOptions5, TOptions6, TOptions7) GetCurrent<TOptions1, TOptions2, TOptions3, TOptions4, TOptions5, TOptions6, TOptions7>() where TOptions1 : RuntimeOptionsBase where TOptions2 : RuntimeOptionsBase where TOptions3 : RuntimeOptionsBase where TOptions4 : RuntimeOptionsBase where TOptions5 : RuntimeOptionsBase where TOptions6 : RuntimeOptionsBase where TOptions7 : RuntimeOptionsBase
        {
            lock (_lock)
            {
                TOptions1 opts1 = (TOptions1)_entries.Single(e => e.Type == typeof(TOptions1)).Options;
                TOptions2 opts2 = (TOptions2)_entries.Single(e => e.Type == typeof(TOptions2)).Options;
                TOptions3 opts3 = (TOptions3)_entries.Single(e => e.Type == typeof(TOptions3)).Options;
                TOptions4 opts4 = (TOptions4)_entries.Single(e => e.Type == typeof(TOptions4)).Options;
                TOptions5 opts5 = (TOptions5)_entries.Single(e => e.Type == typeof(TOptions5)).Options;
                TOptions6 opts6 = (TOptions6)_entries.Single(e => e.Type == typeof(TOptions6)).Options;
                TOptions7 opts7 = (TOptions7)_entries.Single(e => e.Type == typeof(TOptions7)).Options;
                return (opts1, opts2, opts3, opts4, opts5, opts6, opts7);
            }
        }

        /// <summary>
        /// Helper class for returning more palatable versions of the options to dashboard.
        /// </summary>
        public class OptionsWithSource
        {
            public readonly string                              Name;
            public readonly string                              Description;
            public readonly bool                                IsStatic;
            public readonly RuntimeOptionsBase                  Values;
            public readonly OrderedDictionary<string, string>   Sources;
            public readonly OrderedDictionary<string, string>   Descriptions;

            public OptionsWithSource(string name, string description, bool isStatic, RuntimeOptionsBase values, OrderedDictionary<string, string> sources, OrderedDictionary<string, string> descriptions)
            {
                Name         = name;
                Description  = description;
                IsStatic     = isStatic;
                Values       = values;
                Sources      = sources;
                Descriptions = descriptions;
            }
        }

        /// <summary>
        /// Getter for returning all the registered sources to the dashboard.
        /// </summary>
        /// <returns></returns>
        public List<object> GetAllSources()
        {
            return
                new object[]
                {
                    new { Name = DefaultValue, Description = "Default value, defined in Game Server code" },
                    new { Name = ComputedValue, Description = "Computed from other values" }
                }
                .Concat(_sourceSet.Sources.Select(src => new { Name = src.Name, Description = src.Description }))
                .ToList();
        }

        /// <summary>
        /// Getter for returning a single options block to the dashboard.
        /// </summary>
        /// <returns></returns>
        public OptionsWithSource GetOptions<TOptions>() where TOptions : RuntimeOptionsBase
        {
            lock (_lock)
            {
                Entry entry = _entries.Single(entry => entry.Type == typeof(TOptions));
                return new OptionsWithSource(entry.SectionName, entry.SectionDescription, entry.Attribute.IsStatic, entry.Options, entry.ValueSources, entry.Descriptions);
            }
        }

        /// <summary>
        /// Getter for returning the options to the dashboard.
        /// </summary>
        /// <returns></returns>
        public List<OptionsWithSource> GetAllOptions()
        {
            lock (_lock)
            {
                return _entries.Select(entry => new OptionsWithSource(entry.SectionName, entry.SectionDescription, entry.Attribute.IsStatic, entry.Options, entry.ValueSources, entry.Descriptions)).ToList();
            }
        }

        /// <summary>
        /// Returns declarations of all option sources.
        /// </summary>
        static List<RuntimeOptionsSourceSet.SourceDeclaration> GetOptionDeclarations(CommandLineRuntimeOptions commandLineOptions, EnvironmentRuntimeOptionsSource environmentOptions)
        {
            List<RuntimeOptionsSourceSet.SourceDeclaration> declarations = new List<RuntimeOptionsSourceSet.SourceDeclaration>();

            void AddPath(string path, string sourceDescription, bool strictBindingChecks)
            {
                if (path.StartsWith("env-yaml-base64:", StringComparison.Ordinal))
                {
                    // Indirection into another environment variable. In the environment value, the value is a base64-encoded literal
                    string environmentVariableName = path.Substring("env-yaml-base64:".Length);
                    string b64Encoded = Environment.GetEnvironmentVariable(environmentVariableName);

                    // Must exist but may be empty
                    if (b64Encoded == null)
                        throw new InvalidOperationException($"Invalid RuntimeOption path in {sourceDescription}. Environment variable {environmentVariableName} is not defined");
                    if (string.IsNullOrWhiteSpace(b64Encoded))
                        return;

                    string content;
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(b64Encoded);
                        content = Encoding.UTF8.GetString(bytes);
                    }
                    catch
                    {
                        throw new InvalidOperationException($"Invalid RuntimeOption path in {sourceDescription}. Environment variable '{environmentVariableName}' does not contain valid base64 data. Got '{b64Encoded}'");
                    }

                    declarations.Add(new RuntimeOptionsSourceSet.SourceDeclaration(
                        name:                   environmentVariableName,
                        description:            $"Environment variable {environmentVariableName}, via {sourceDescription}",
                        strictBindingChecks:    strictBindingChecks,
                        registerFunc:           builder =>
                                                {
                                                    builder.AddYaml(content);
                                                }));
                }
                else if (!path.Contains(':'))
                {
                    // Normal file
                    switch (Path.GetExtension(path))
                    {
                        case ".yaml":
                            string shortPath = path.Split('/').Last();
                            declarations.Add(new RuntimeOptionsSourceSet.SourceDeclaration(
                                name:                   shortPath,
                                description:            $"From the file {path}, via {sourceDescription}",
                                strictBindingChecks:    strictBindingChecks,
                                registerFunc:           builder =>
                                                        {
                                                            builder.SetBasePath(Directory.GetCurrentDirectory());
                                                            builder.AddYamlFile(path, optional: false, reloadOnChange: true);
                                                        }));
                            break;

                        default:
                            throw new InvalidOperationException($"Unsupported Options file type: {path}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Invalid RuntimeOption path in {sourceDescription}. Unrecognized scheme in path: {path}");
                }
            }

            string optionsPathOverride = Environment.GetEnvironmentVariable("METAPLAY_OPTIONS");
            string[] optionPaths = (optionsPathOverride ?? DefaultOptionsPaths).Split(";").Where(path => !string.IsNullOrEmpty(path)).ToArray();
            foreach (string path in optionPaths)
                AddPath(path, optionsPathOverride == null ? "built-in source" : "METAPLAY_OPTIONS environment variable", strictBindingChecks: true);

            // \note: EXTRA options are indended for infrastructure-injected variables. We tolerated unknown fields there to allow for forwards compatibility.
            string extraOptions = Environment.GetEnvironmentVariable("METAPLAY_EXTRA_OPTIONS");
            string[] extraOptionsPaths = extraOptions != null ? extraOptions.Split(";").Where(path => !string.IsNullOrEmpty(path)).ToArray() : Array.Empty<string>();
            foreach (string path in extraOptionsPaths)
                AddPath(path, "METAPLAY_EXTRA_OPTIONS environment variable", strictBindingChecks: false);

            declarations.Add(new RuntimeOptionsSourceSet.SourceDeclaration("Environment", "Passed in as an environment variable", strictBindingChecks: false, builder => builder.Add(environmentOptions.ConfigurationSource)));
            declarations.Add(new RuntimeOptionsSourceSet.SourceDeclaration("CLI", "Passed in as a command line option", strictBindingChecks: true, builder => builder.Add(commandLineOptions.ConfigurationSource)));
            return declarations;
        }

        /// <summary>
        /// Parses command line. On failure, kills the application and does not return.
        /// </summary>
        CommandLineRuntimeOptions ParseCommandLineOrDie(string[] cmdLineArgs)
        {
            // Resolve all the command line aliases for all the entries
            Dictionary<string, string> cmdLineAliases = new Dictionary<string, string>();
            foreach (Entry entry in _entries)
            {
                foreach (Entry.OptionValue value in entry.GetValues())
                {
                    if (value.CommandLineAlias != null)
                        cmdLineAliases.Add(value.CommandLineAlias, $"{entry.SectionName}:{value.Name}");
                }
            }

            try
            {
                return CommandLineRuntimeOptions.Parse(cmdLineArgs, cmdLineAliases);
            }
            catch (CommandLineRuntimeOptions.ParseError ex)
            {
                PrintCliUsageErrorAndExitNoReturn(new string[] { ex.Message });
                throw; // Unreachable, but compiler does not know
            }
        }

        const int SysexitsExUsage = 64; // EX_USAGE. See sysexits for more details

        /// <summary>
        /// Prints invalid command line error message to console and exits. Does not return.
        /// </summary>
        static void PrintCliUsageErrorAndExitNoReturn(string[] errors)
        {
            foreach (string error in errors)
                _log.Error("{CLIParseError}", error);

            Application.Application.ForceTerminate(SysexitsExUsage, "Invalid command line");
        }
    }
}
