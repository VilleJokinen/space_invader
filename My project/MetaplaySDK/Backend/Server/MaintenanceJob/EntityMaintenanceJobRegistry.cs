// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Server.MaintenanceJob.EntityRefresher;
using Metaplay.Server.MaintenanceJob.EntitySchemaMigrator;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Metaplay.Server.MaintenanceJob
{
    /// <summary>
    /// Marks the entity eligible for Refresh Maintenance Jobs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class EntityMaintenanceRefreshJobAttribute : Attribute
    {
        public EntityMaintenanceRefreshJobAttribute()
        {
        }
    }

    /// <summary>
    /// Marks the entity eligible for Schema Migration Maintenance Jobs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class EntityMaintenanceSchemaMigratorJobAttribute : Attribute
    {
        public EntityMaintenanceSchemaMigratorJobAttribute()
        {
        }
    }

    /// <summary>
    /// Register a general maintenance job for the target <c>PersistedEntityActor</c>.
    /// The <c>jobSpecType</c> parameter defines the <see cref="MaintenanceJobSpec"/> class that implements the job.
    /// </summary>
    // \todo [petri] Implement mechanisms to allow overriding SDK-level jobs with game-specific ones.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class EntityMaintenanceJobAttribute : Attribute
    {
        public readonly string                      JobTypeId;          // Unique id for the job type
        public readonly Type                        JobSpecType;
        public readonly Func<MaintenanceJobSpec>    JobSpecFactory;

        public EntityMaintenanceJobAttribute(string jobTypeId, Type jobSpecType)
        {
            if (!jobSpecType.IsDerivedFrom<MaintenanceJobSpec>())
                throw new ArgumentException($"{jobSpecType.ToGenericTypeString()} is not derived from {nameof(MaintenanceJobSpec)}", nameof(jobSpecType));

            MethodInfo createDefault = jobSpecType.GetMethod("CreateDefault", BindingFlags.Static | BindingFlags.Public, new Type[] { });
            if (createDefault == null)
                throw new InvalidOperationException($"{jobSpecType.ToGenericTypeString()} must have a 'public static {jobSpecType.ToGenericTypeString()} CreateDefault()' which instantiates the job spec with default arguments");
            if (createDefault.ReturnType != jobSpecType)
                throw new InvalidOperationException($"{jobSpecType.ToGenericTypeString()}.CreateDefault() must return the type itself ({jobSpecType.ToGenericTypeString()})");

            JobTypeId = jobTypeId;
            JobSpecType = jobSpecType;
            JobSpecFactory = () => (MaintenanceJobSpec)createDefault.InvokeWithoutWrappingError(null, new object[] { });
        }
    }

    public static class EntityMaintenanceJobRegistry
    {
        public static OrderedDictionary<EntityKind, Func<EntityRefresherJobSpec>>               RefreshJobSpecs;
        public static OrderedDictionary<EntityKind, Func<EntitySchemaMigratorJobSpec>>          SchemaMigrationJobSpecs;
        public static OrderedDictionary<(EntityKind, string), EntityMaintenanceJobAttribute>    GenericJobs;        // Key is (EntityKind, jobId)

        public static void Initialize()
        {
            OrderedDictionary<EntityKind, Func<EntityRefresherJobSpec>> refreshJobSpecs = new();
            OrderedDictionary<EntityKind, Func<EntitySchemaMigratorJobSpec>> schemaMigrationJobSpecs = new();
            OrderedDictionary<(EntityKind, string), EntityMaintenanceJobAttribute> genericJobs = new();

            foreach ((Type entityConfigType, EntityConfigBase entityConfig) in EntityConfigRegistry.Instance.TypeToEntityConfig)
            {
                if (entityConfig is not PersistedEntityConfig persistedConfig)
                    continue;

                EntityKind entityKind = entityConfig.EntityKind;

                // Refresh Jobs
                if (entityConfigType.GetCustomAttribute<EntityMaintenanceRefreshJobAttribute>() != null)
                {
                    Func<EntityRefresherJobSpec> spec = () => new EntityRefresherJobSpec(entityKind);
                    if (!refreshJobSpecs.TryAdd(entityKind, spec))
                        throw new InvalidOperationException($"[EntityMaintenanceRefreshJob] in {entityConfigType.ToNamespaceQualifiedTypeString()} registered for the same Kind {entityKind} as {refreshJobSpecs[entityKind].GetType().ToNamespaceQualifiedTypeString()}");
                }

                // Schema Migration Jobs
                if (entityConfigType.GetCustomAttribute<EntityMaintenanceSchemaMigratorJobAttribute>() != null)
                {
                    Func<EntitySchemaMigratorJobSpec> spec = () => new EntitySchemaMigratorJobSpec(entityKind);
                    if (!schemaMigrationJobSpecs.TryAdd(entityKind, spec))
                        throw new InvalidOperationException($"[EntityMaintenanceSchemaMigratorJob] in {entityConfigType.ToNamespaceQualifiedTypeString()} registered for the same Kind {entityKind} as {schemaMigrationJobSpecs[entityKind].GetType().ToNamespaceQualifiedTypeString()}");
                }

                // Generic Jobs (\note multiple jobs allowed for the same EntityActor, as long as JobTypeIds are unique)
                foreach (EntityMaintenanceJobAttribute attrib in entityConfigType.GetCustomAttributes<EntityMaintenanceJobAttribute>())
                {
                    if (!genericJobs.TryAdd((entityConfig.EntityKind, attrib.JobTypeId), attrib))
                        throw new InvalidOperationException($"[EntityMaintenanceJob({attrib.JobTypeId})] for {entityConfigType.ToNamespaceQualifiedTypeString()} registered multiple times");
                }
            }

            RefreshJobSpecs = refreshJobSpecs;
            SchemaMigrationJobSpecs = schemaMigrationJobSpecs;
            GenericJobs = genericJobs;

            // Check that [EntityMaintenanceRefreshJob] attribute is only used on classes deriving from PersistedEntityConfig
            foreach (Type type in TypeScanner.GetConcreteClassesWithAttribute<EntityMaintenanceRefreshJobAttribute>())
            {
                if (!type.IsDerivedFrom<PersistedEntityConfig>())
                    throw new InvalidOperationException($"Invalid [EntityMaintenanceRefreshJob] attribute on '{type.ToNamespaceQualifiedTypeString()}'. Attribute is only allowed on types implementing PersistedEntityConfig.");
            }

            // Check that [EntityMaintenanceSchemaMigratorJob] attribute is only used on classes deriving from PersistedEntityConfig
            foreach (Type type in TypeScanner.GetConcreteClassesWithAttribute<EntityMaintenanceSchemaMigratorJobAttribute>())
            {
                if (!type.IsDerivedFrom<PersistedEntityConfig>())
                    throw new InvalidOperationException($"Invalid [EntityMaintenanceSchemaMigratorJob] attribute on '{type.ToNamespaceQualifiedTypeString()}'. Attribute is only allowed on types implementing PersistedEntityConfig.");
            }

            // Check that [EntityMaintenanceJob] attribute is only used on classes deriving from PersistedEntityConfig
            foreach (Type type in TypeScanner.GetConcreteClassesWithAttribute<EntityMaintenanceJobAttribute>())
            {
                if (!type.IsDerivedFrom<PersistedEntityConfig>())
                    throw new InvalidOperationException($"Invalid [EntityMaintenanceJob] attribute on '{type.ToNamespaceQualifiedTypeString()}'. Attribute is only allowed on types implementing PersistedEntityConfig.");
            }
        }
    }
}
