// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Generic Import and Export functions for MPC entries in GameConfigs
    /// </summary>
    public class GameConfigBinarySerialization
    {
        private GameConfigBinarySerialization(List<MpcEntrySerialization> entrySerializers)
        {
            EntrySerializers = entrySerializers;
        }

        List<MpcEntrySerialization> EntrySerializers { get; }

        class DummyGameConfigData: IGameConfigData<object>
        {
            public object ConfigKey => throw new NotImplementedException();
        }
        class DummyGameConfigKeyValue : GameConfigKeyValue<DummyGameConfigKeyValue>
        {
        }

        struct MpcEntrySerialization
        {
            public MethodInfo             Exporter;
            public MethodInfo             AliasExporter;
            public MethodInfo             ImportReader;
            public MethodInfo             NakedImportReader;
            public MethodInfo             Reserializer;
            public Func<object, object>   EntryGetter;
            public Action<object, object> EntrySetter;
            public string                 EntryName;
            public bool                   RequireArchiveEntry;

            public IEnumerable<ConfigArchiveEntry> Export(object configInstance)
            {
                object entry = EntryGetter(configInstance);
                if (entry == null)
                    throw new InvalidOperationException($"GameConfig entry {EntryName} is null!");
                object[] funcParams = new object[] { entry };
                byte[]   serialized = (byte[])Exporter.InvokeWithoutWrappingError(null, funcParams);
                yield return ConfigArchiveEntry.FromBlob($"{EntryName}.mpc", serialized);
                if (AliasExporter != null)
                {
                    object[] aliasFuncParams        = new object[] { entry };
                    byte[]   serializedAliases = (byte[])AliasExporter.InvokeWithoutWrappingError(null, aliasFuncParams);
                    if (serializedAliases != null)
                        yield return ConfigArchiveEntry.FromBlob($"{EntryName}.AliasTable.mpc", serializedAliases);
                }
            }

            public void Import(object configInstance, GameConfigImporter importer)
            {
                string archiveEntryName = $"{EntryName}.mpc";
                if (importer.Contains(archiveEntryName))
                {
                    object[] funcParams = new object[] { importer, archiveEntryName };
                    object   value      = ImportReader.InvokeWithoutWrappingError(null, funcParams);
                    EntrySetter(configInstance, value);
                }
                else if (RequireArchiveEntry)
                    throw new InvalidOperationException($"Required {importer.Params.Resources.GameConfigType} entry {archiveEntryName} is missing from archive!");
            }

            /// <summary>
            /// Imports the given archive to a raw <see cref="IGameConfigLibraryEntry"/> without validation/post processing
            /// </summary>
            public IGameConfigEntry Import(ConfigArchive archive)
            {
                string archiveEntryName = $"{EntryName}.mpc";
                if (archive.ContainsEntryWithName(archiveEntryName))
                {
                    object[] funcParams = new object[] { archive, archiveEntryName };
                    object   value      = NakedImportReader.InvokeWithoutWrappingError(null, funcParams);

                    return value as IGameConfigEntry;
                }
                else if (RequireArchiveEntry)
                    throw new InvalidOperationException($"Required entry {archiveEntryName} is missing from archive!");

                return null;
            }

            public ConfigArchiveEntry Reserialize(ConfigArchive archive, MetaSerializationFlags flags)
            {
                string archiveEntryName = $"{EntryName}.mpc";
                if (archive.ContainsEntryWithName(archiveEntryName))
                {
                    object[] funcParams = new object[] { archive, archiveEntryName, flags };
                    byte[]   serialized = (byte[])Reserializer.InvokeWithoutWrappingError(null, funcParams);
                    return ConfigArchiveEntry.FromBlob(archiveEntryName, serialized);
                }
                else if (RequireArchiveEntry)
                    throw new InvalidOperationException($"Required entry {archiveEntryName} is missing from archive!");

                return null;
            }
        };

        /// <summary>
        /// Imports the given <paramref name="libraries"/> from <paramref name="archive"/> without validation/post processing
        /// </summary>
        /// <param name="libraries">The list of libraries to import, case-insensitive</param>
        /// <returns></returns>
        public (Dictionary<string, IGameConfigEntry> entries, Dictionary<string, Exception> exceptions) ImportLibraries(ConfigArchive archive, List<string> libraries = null)
        {
            Dictionary<string, IGameConfigEntry> gameConfigLibraries = new Dictionary<string, IGameConfigEntry>();
            Dictionary<string, Exception>        exceptions = new Dictionary<string, Exception>();
            foreach (MpcEntrySerialization serialization in EntrySerializers.Where(x => libraries?.Any(y=> x.EntryName.Equals(y, StringComparison.InvariantCultureIgnoreCase)) ?? true))
            {
                try
                {
                    IGameConfigEntry gameConfigLibrary = serialization.Import(archive);
                    if (gameConfigLibrary != null)
                        gameConfigLibraries.Add(serialization.EntryName, gameConfigLibrary);
                }
                catch (Exception ex)
                {
                    exceptions.Add(serialization.EntryName, ex);
                }
            }

            return (gameConfigLibraries, exceptions);
        }

        public void ImportInto(GameConfigBase config, GameConfigImporter importer)
        {
            foreach (MpcEntrySerialization serialization in EntrySerializers)
                serialization.Import(config, importer);
        }

        public ConfigArchiveEntry[] Export(GameConfigBase config)
        {
            return EntrySerializers.SelectMany(x => x.Export(config)).ToArray();
        }

        public IEnumerable<ConfigArchiveEntry> Reserialize(ConfigArchive archive, MetaSerializationFlags serializationFlags)
        {
            List<ConfigArchiveEntry> updatedEntries = EntrySerializers.Select(x => x.Reserialize(archive, serializationFlags)).Where(x => x != null).ToList();
            foreach (ConfigArchiveEntry entry in archive.Entries)
            {
                yield return updatedEntries.Find(x => x.Name == entry.Name) ?? entry;
            }
        }

        public static GameConfigBinarySerialization Make(Type gameConfigType, IEnumerable<GameConfigRepository.GameConfigEntryInfo> Entries)
        {
            Func<GameConfigImporter, string, GameConfigLibrary<object, DummyGameConfigData>> DummyImportLibrary                = GameConfigLibrary<object, DummyGameConfigData>.ImportBinaryLibrary;
            Func<ConfigArchive, string, GameConfigLibrary<object, DummyGameConfigData>> DummyImportNakedLibrary = GameConfigLibrary<object, DummyGameConfigData>.ImportNakedBinaryLibrary;
            Func<GameConfigImporter, string, DummyGameConfigKeyValue>                        DummyImportKeyValueStructure      = DummyGameConfigKeyValue.ImportBinaryKeyValueStructure;
            Func<ConfigArchive, string, DummyGameConfigKeyValue> DummyImportNakedKeyValueStructure = DummyGameConfigKeyValue.ImportNakedBinaryKeyValueStructure;
            Func<GameConfigLibrary<object, DummyGameConfigData>, byte[]>                     DummyExportLibrary                = GameConfigLibrary<object, DummyGameConfigData>.ExportBinaryLibrary;
            Func<GameConfigLibrary<object, DummyGameConfigData>, byte[]>                     DummyExportLibraryAliases         = GameConfigLibrary<object, DummyGameConfigData>.ExportBinaryLibraryAliases;
            Func<DummyGameConfigKeyValue, byte[]>                                            DummyExportKeyValueStructure      = DummyGameConfigKeyValue.ExportBinaryKeyValueStructure;
            Func<ConfigArchive, string, MetaSerializationFlags, byte[]> DummyReserializeLibrary = GameConfigLibrary<object, DummyGameConfigData>.ReserializeBinaryLibraryItems;
            Func<ConfigArchive, string, MetaSerializationFlags, byte[]> DummyReserializeKeyValueStructure = DummyGameConfigKeyValue.ReserializeKeyValueStructure;

            List<MpcEntrySerialization> entrySerializers = Entries
                .Where(entryInfo => entryInfo.MpcFormat)
                .Select(entryInfo =>
                {
                    Type entryType = entryInfo.MemberInfo.GetDataMemberType();
                    MpcEntrySerialization serialization = new MpcEntrySerialization();
                    serialization.EntryName           = entryInfo.Name;
                    serialization.RequireArchiveEntry = entryInfo.RequireArchiveEntry;
                    serialization.EntryGetter         = entryInfo.MemberInfo.GetDataMemberGetValueOnDeclaringType();
                    serialization.EntrySetter         = entryInfo.MemberInfo.GetDataMemberSetValueOnDeclaringType();
                    if (entryType.IsGameConfigLibrary())
                    {
                        serialization.ImportReader      = GetPublicStaticMethod(entryType, DummyImportLibrary.Method.Name);
                        serialization.NakedImportReader = GetPublicStaticMethod(entryType, DummyImportNakedLibrary.Method.Name);
                        serialization.Exporter          = GetPublicStaticMethod(entryType, DummyExportLibrary.Method.Name);
                        serialization.AliasExporter     = GetPublicStaticMethod(entryType, DummyExportLibraryAliases.Method.Name);
                        serialization.Reserializer      = GetPublicStaticMethod(entryType, DummyReserializeLibrary.Method.Name);
                    }
                    else
                    {
                        serialization.ImportReader      = GetPublicStaticMethod(entryType, DummyImportKeyValueStructure.Method.Name);
                        serialization.NakedImportReader = GetPublicStaticMethod(entryType, DummyImportNakedKeyValueStructure.Method.Name);
                        serialization.Exporter          = GetPublicStaticMethod(entryType, DummyExportKeyValueStructure.Method.Name);
                        serialization.Reserializer      = GetPublicStaticMethod(entryType, DummyReserializeKeyValueStructure.Method.Name);
                    }
                    return serialization;
                }).ToList();


            return new GameConfigBinarySerialization(entrySerializers);
        }

        static MethodInfo GetPublicStaticMethod(Type type, string methodName)
        {
            return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                   ?? throw new InvalidOperationException($"No public static {methodName} method found on {type.ToGenericTypeString()}");
        }
    }
}
