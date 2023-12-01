// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using System;
using System.Reflection;

namespace Metaplay.Server.EntityArchive
{
    /// <summary>
    /// Sets the Archive importer and exporter for the entity.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class EntityArchiveImporterExporterAttribute : Attribute
    {
        public readonly string  ArchiveTag;
        public readonly Type    ImporterHandler;
        public readonly Type    ExporterHandler;

        /// <summary>
        /// Declares the archive tag (i.e. under which key they are in the JSON), and importer and exporter handler of entity.
        /// </summary>
        public EntityArchiveImporterExporterAttribute(string archiveTag, Type importerHandler, Type exporterType)
        {
            ArchiveTag = archiveTag;
            ImporterHandler = importerHandler;
            ExporterHandler = exporterType;
        }
    }

    public static partial class EntityArchiveUtils
    {
        readonly struct ArchivingSpec
        {
#pragma warning disable CS0618 // Type or member is obsolete #LegacyEntityHandlerCompat
            public readonly string                      ArchiveTag;
            public readonly Func<ImportEntityHandler>   _createImportHandler;
            public readonly Func<ExportEntityHandler>   _createExportHandler;

            public ArchivingSpec(string archiveTag, Func<ImportEntityHandler> createImportHandler, Func<ExportEntityHandler> createExportHandler)
            {
                ArchiveTag = archiveTag;
                _createImportHandler = createImportHandler;
                _createExportHandler = createExportHandler;
            }

            public ImportEntityHandler CreateImportHandler()
            {
                ImportEntityHandler handler = _createImportHandler();
                handler.Initialize();
                return handler;
            }
            public ExportEntityHandler CreateExportHandler()
            {
                ExportEntityHandler handler = _createExportHandler();
                return handler;
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }
        static OrderedDictionary<string, ArchivingSpec> s_archiveHandlers;

        public static void Initialize()
        {
            OrderedDictionary<string, ArchivingSpec> handlers = new();

            foreach ((Type entityConfigType, EntityConfigBase entityConfig) in EntityConfigRegistry.Instance.TypeToEntityConfig)
            {
                if (entityConfig is not PersistedEntityConfig persistedConfig)
                    continue;

                EntityArchiveImporterExporterAttribute ioAttr = entityConfigType.GetCustomAttribute<EntityArchiveImporterExporterAttribute>();
                if (ioAttr == null)
                    continue;

#pragma warning disable CS0618 // Type or member is obsolete #LegacyEntityHandlerCompat
                if (!ioAttr.ImporterHandler.IsDerivedFrom<ImportEntityHandler>())
                    throw new InvalidOperationException($"Importer handler defined in [EntityArchiveImporterExporterAttribute] on {entityConfigType.ToNamespaceQualifiedTypeString()} must derive from ImportEntityHandler.");
                if (!ioAttr.ExporterHandler.IsDerivedFrom<ExportEntityHandler>())
                    throw new InvalidOperationException($"Exporter handler defined in [EntityArchiveImporterExporterAttribute] on {entityConfigType.ToNamespaceQualifiedTypeString()} must derive from ExportEntityHandler.");

                if (ioAttr.ImporterHandler.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException($"There is no constructor for {ioAttr.ImporterHandler.ToNamespaceQualifiedTypeString()} or it is inaccessible");
                if (ioAttr.ExporterHandler.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException($"There is no constructor for {ioAttr.ExporterHandler.ToNamespaceQualifiedTypeString()} or it is inaccessible");

                Func<ImportEntityHandler> importActivator = () => (ImportEntityHandler)Activator.CreateInstance(ioAttr.ImporterHandler);
                Func<ExportEntityHandler> exportActivator = () => (ExportEntityHandler)Activator.CreateInstance(ioAttr.ExporterHandler);
#pragma warning restore CS0618 // Type or member is obsolete

                EntityKind handlerKind = importActivator().EntityKind;
                if (handlerKind != entityConfig.EntityKind)
                    throw new InvalidOperationException($"Importer handler defined in [EntityArchiveImporterExporterAttribute] on {entityConfigType.ToNamespaceQualifiedTypeString()} supports {handlerKind} entities. EntityConfig is for kind {entityConfig.EntityKind}.");

                ArchivingSpec archivingSpec = new ArchivingSpec(ioAttr.ArchiveTag, importActivator, exportActivator);
                if (!handlers.AddIfAbsent(ioAttr.ArchiveTag, archivingSpec))
                    throw new InvalidOperationException($"Importer {ioAttr.ImporterHandler.ToNamespaceQualifiedTypeString()} has same archive tag {ioAttr.ArchiveTag} as {handlers[ioAttr.ArchiveTag].CreateImportHandler().GetType().ToNamespaceQualifiedTypeString()}");
            }

            s_archiveHandlers = handlers;

            // Check that [EntityArchiveImporterExporter] attribute is only used on classes deriving from PersistedEntityConfig
            foreach (Type type in TypeScanner.GetConcreteClassesWithAttribute<EntityArchiveImporterExporterAttribute>())
            {
                if (!type.IsDerivedFrom<PersistedEntityConfig>())
                    throw new InvalidOperationException($"Invalid [EntityArchiveImporterExporter] attribute on '{type.ToNamespaceQualifiedTypeString()}'. Attribute is only allowed on types implementing PersistedEntityConfig.");
            }
        }
    }
}
