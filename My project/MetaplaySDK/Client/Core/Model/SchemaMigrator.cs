// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using static System.FormattableString;

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Error happened while migrating a <see cref="ISchemaMigratable"/> object to the latest schema version.
    /// </summary>
    public class SchemaMigrationError : Exception
    {
        public readonly Type MigratableType;
        public readonly int  FromVersion;

        public SchemaMigrationError(Type migratableType, int fromVersion, Exception innerException) :
            base(Invariant($"Failed to run schema migration for {migratableType.ToGenericTypeString()} from v{fromVersion} to v{fromVersion+1}"), innerException)
        {
            MigratableType = migratableType;
            FromVersion = fromVersion;
        }
    }

    /// <summary>
    /// Container for schema migration steps related to a <see cref="ISchemaMigratable"/> type.
    /// </summary>
    public class SchemaMigrator
    {
        public readonly Type                MigratableType;
        public readonly MetaVersionRange    SupportedSchemaVersions;
        public int                          OldestSupportedSchemaVersion    => SupportedSchemaVersions.MinVersion;
        public int                          CurrentSchemaVersion            => SupportedSchemaVersions.MaxVersion;

        Dictionary<int, Action<ISchemaMigratable>>  _migrateFuncs;

        SchemaMigrator(Type migratableType, MetaVersionRange supportedSchemaVersions, Dictionary<int, Action<ISchemaMigratable>> migrateFuncs)
        {
            MigratableType          = migratableType;
            SupportedSchemaVersions = supportedSchemaVersions;
            _migrateFuncs           = migrateFuncs;
        }

        public static SchemaMigrator CreateForType(Type migratableType)
        {
            SupportedSchemaVersionsAttribute schemaVersionAttrib = migratableType.GetCustomAttribute<SupportedSchemaVersionsAttribute>();
            if (schemaVersionAttrib == null)
                throw new InvalidOperationException($"The type {migratableType.ToGenericTypeString()} implements ISchemaMigratable, but does not declare [SupportedSchemaVersions] attribute.");

            // Validate schema versions
            MetaVersionRange supportedSchemaVersions = schemaVersionAttrib.SupportedSchemaVersions;
            if (supportedSchemaVersions.MinVersion <= 0 || supportedSchemaVersions.MaxVersion <= 0)
                throw new ArgumentException($"SupportedSchemaVersion values must be >= 1, got {supportedSchemaVersions}");
            if (supportedSchemaVersions.MaxVersion < supportedSchemaVersions.MinVersion)
                throw new ArgumentException($"SupportedSchemaVersion.MaxVersion must be greater than MinVersion, got {supportedSchemaVersions}");

            // Find all migration methods
            Dictionary<int, Action<ISchemaMigratable>> migrateFuncs = new Dictionary<int, Action<ISchemaMigratable>>();
            foreach (MethodInfo methodInfo in migratableType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                // Register individual per-version migration functions declared with MigrationFromVersionAttribute
                foreach (MigrationFromVersionAttribute migrateAttrib in methodInfo.GetCustomAttributes<MigrationFromVersionAttribute>())
                {
                    if (methodInfo.IsStatic)
                        throw new InvalidOperationException($"Schema migration method {migratableType.ToGenericTypeString()}.{methodInfo.Name}() is static! Only non-static methods are allowed.");
                    if (methodInfo.ReturnType != typeof(void))
                        throw new InvalidOperationException($"Schema migration method {migratableType.ToGenericTypeString()}.{methodInfo.Name}() returns a value! Only void methods are allowed.");
                    if (methodInfo.GetParameters().Length != 0)
                        throw new InvalidOperationException($"Schema migration method {migratableType.ToGenericTypeString()}.{methodInfo.Name}() takes parameters! Only methods with no parameters are allowed.");

                    Action<ISchemaMigratable> migrateFunc = (ISchemaMigratable migratable) =>
                    {
                        methodInfo.InvokeWithoutWrappingError(migratable, Array.Empty<object>());
                    };
                    if (migrateFuncs.ContainsKey(migrateAttrib.FromVersion))
                        throw new InvalidOperationException($"Type {migratableType.ToGenericTypeString()} contains duplicate migration methods from schema version {migrateAttrib.FromVersion}");
                    migrateFuncs.Add(migrateAttrib.FromVersion, migrateFunc);
                }

                // Get additional migration actions by calling the RegisterMigrationsFunction with each of the supported versions, if one exists
                if (methodInfo.GetCustomAttribute<RegisterMigrationsFunctionAttribute>() != null)
                {
                    // Instantiate the CallRegisterMigrationsFunc generic wrapper with the actual migratable concrete type,
                    // to allow the user-facing API to deal in concrete types.
                    Delegate callPrototype = new CallRegisterMigrationsFuncDelegate<ISchemaMigratable>(CallRegisterMigrationsFunc<ISchemaMigratable>);
                    IEnumerable<(int, Action<ISchemaMigratable>)> registeredMigrateFuncs = callPrototype.Method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(new Type[] { migratableType })
                        .CreateDelegate<Func<MethodInfo, int, int, IEnumerable<(int, Action<ISchemaMigratable>)>>>()
                        .Invoke(methodInfo, supportedSchemaVersions.MinVersion, supportedSchemaVersions.MaxVersion);

                    foreach ((int version, Action<ISchemaMigratable> migrateFunc) in registeredMigrateFuncs)
                    {
                        if (migrateFuncs.ContainsKey(version))
                            throw new InvalidOperationException($"Type {migratableType.ToGenericTypeString()} contains duplicate migration methods from schema version {version}");
                        migrateFuncs.Add(version, migrateFunc);
                    }
                }
            }

            // Check that all required migrations are specified
            // Resolve fromVersions required by SupportedSchemaVersions but not present in migrateMethods
            IEnumerable<int> requiredFromVersions = Enumerable.Range(supportedSchemaVersions.MinVersion, supportedSchemaVersions.MaxVersion - supportedSchemaVersions.MinVersion);
            int[] missingMigrations = requiredFromVersions.Except(migrateFuncs.Keys).OrderBy(v => v).ToArray();
            if (missingMigrations.Length > 0)
            {
                string missingVersions = string.Join(", ", missingMigrations.Select(vsn => Invariant($"v{vsn}->v{vsn + 1}")));
                throw new InvalidOperationException($"Missing migrations for {migratableType.ToGenericTypeString()} versions: {missingVersions}");
            }

            // Check that no extra (unused) migrations are specified.
            // This is the reverse of the above check. This is intended to catch cases where SupportedSchemaVersions accidentally has the wrong range.
            int[] extraMigrations = migrateFuncs.Keys.Except(requiredFromVersions).OrderBy(v => v).ToArray();
            if (extraMigrations.Length > 0)
            {
                string extraVersions = string.Join(", ", extraMigrations.Select(vsn => Invariant($"v{vsn}->v{vsn + 1}")));
                throw new InvalidOperationException($"Extra migrations for {migratableType.ToGenericTypeString()} versions: {extraVersions}. These are not covered by the range specified in the [SupportedSchemaVersions(...)] attribute. Either remove the extra migrations or adjust the range.");
            }

            return new SchemaMigrator(migratableType, supportedSchemaVersions, migrateFuncs);
        }

        delegate IEnumerable<(int, Action<ISchemaMigratable>)> CallRegisterMigrationsFuncDelegate<TMigratableType>(MethodInfo registerMethod, int minVersion, int maxVersion) where TMigratableType : ISchemaMigratable;
        public static IEnumerable<(int, Action<ISchemaMigratable>)> CallRegisterMigrationsFunc<TMigratableType>(MethodInfo registerMethod, int minVersion, int maxVersion) where TMigratableType : ISchemaMigratable
        {
            for (int version = minVersion; version < maxVersion; version++)
            {
                Action<TMigratableType> func = (Action<TMigratableType>)registerMethod.InvokeWithoutWrappingError(null, new object[] { version });
                if (func != null)
                    yield return (version, migratable => func.Invoke((TMigratableType)migratable));
            }
        }

        public void RunMigrations(ISchemaMigratable migratable, int fromVersion)
        {
            for (int version = fromVersion; version < SupportedSchemaVersions.MaxVersion; version++)
            {
                try
                {
                    _migrateFuncs[version](migratable);
                }
                catch (Exception ex)
                {
                    throw new SchemaMigrationError(migratable.GetType(), version, ex);
                }
            }
        }
    }
}
