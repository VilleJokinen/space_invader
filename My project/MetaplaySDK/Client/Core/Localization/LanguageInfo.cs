// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Core.Localization
{
    /// <summary>
    /// Identifier for a language supported by the game.
    /// </summary>
    [MetaSerializable]
    public class LanguageId : StringId<LanguageId> { }

    /// <summary>
    /// Information related to a language supported by the game.
    /// </summary>
    [MetaSerializable]
    public class LanguageInfo : IGameConfigData<LanguageId>
    {
        [MetaMember(1)] public LanguageId   LanguageId  { get; private set; }
        [MetaMember(2)] public string       DisplayName { get; private set; }

        public LanguageId ConfigKey => LanguageId;

        public LanguageInfo() { }
        public LanguageInfo(LanguageId id, string name)
        {
            LanguageId = id;
            DisplayName = name;
        }
    }

    /// <summary>
    /// Represents a single localizable string.
    /// </summary>
    [MetaSerializable]
    public class TranslationId : StringId<TranslationId> { }

    /// <summary>
    /// Represents a full localizations for a single target language.
    /// Should contain a translation for each valid <see cref="TranslationId"/>.
    /// </summary>
    public class LocalizationLanguage
    {
        public LanguageId                               LanguageId      { get; }
        public ContentHash                              Version         { get; }
        public OrderedDictionary<TranslationId, string> Translations    { get; }

        [MetaSerializable]
        public class BinaryV1
        {
            [MetaMember(1)] public LanguageId                               LanguageId;
            [MetaMember(2), MaxCollectionSize(int.MaxValue)] public OrderedDictionary<TranslationId, string> Translations;
        }

        public LocalizationLanguage(LanguageId languageId, ContentHash version, OrderedDictionary<TranslationId, string> translations)
        {
            LanguageId      = languageId;
            Version         = version;
            Translations    = translations;
        }

        public static LocalizationLanguage FromBytes(LanguageId languageId, ContentHash version, byte[] bytes)
        {
            LocalizationLanguage language = ImportBinary(version, bytes);
            if (language.LanguageId != languageId)
                throw new ArgumentException($"Language '{languageId}' was specified, but blob contains '{language.LanguageId}'", nameof(languageId));
            return language;
        }

        public static LocalizationLanguage FromSpreadsheetContent(LanguageId languageId, SpreadsheetContent sheet)
        {
            OrderedDictionary<TranslationId, string> translations = new OrderedDictionary<TranslationId, string>();

            foreach (List<SpreadsheetCell> row in sheet.Cells)
            {
                if (row.Count != 2)
                    throw new InvalidOperationException($"Error parsing {languageId} from sheet: Expecting exactly two values on row");

                translations.Add(TranslationId.FromString(row[0].Value), row[1].Value);
            }

            // \note: spreadsheets do not have versions.
            return new LocalizationLanguage(languageId, version: ContentHash.None, translations);
        }

        public ConfigArchiveEntry ExportBinaryConfigArchiveEntry()
        {
            return ConfigArchiveEntry.FromBlob($"{LanguageId}.mpc", ExportBinary());
        }

        const byte PayloadHeaderMagicPrefixByte = 255; // Arbitrary byte different from (byte)WireDataType.NullableStruct

        public byte[] ExportBinary()
        {
            BinaryV1 binary = new BinaryV1();
            binary.LanguageId = LanguageId;
            binary.Translations = Translations;
            return MetaSerialization.SerializeTagged(binary, MetaSerializationFlags.IncludeAll, logicVersion: null);
        }

        public byte[] ExportCompressedBinary()
        {
            using (FlatIOBuffer buffer = new FlatIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    writer.WriteByte(PayloadHeaderMagicPrefixByte);

                    writer.WriteVarInt(1); // schema version
                    writer.WriteVarInt((int)CompressionAlgorithm.Deflate);

                    byte[] compressed = CompressUtil.DeflateCompress(ExportBinary());
                    writer.WriteBytes(compressed, 0, compressed.Length);
                }
                return buffer.ToArray();
            }
        }

        public static LocalizationLanguage ImportBinary(ContentHash version, byte[] bytes)
        {
            // Check header and decompress (unless header is missing because it's legacy).

            if (bytes[0] == (byte)WireDataType.NullableStruct)
            {
                BinaryV1 binary = MetaSerialization.DeserializeTagged<BinaryV1>(bytes, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                return new LocalizationLanguage(binary.LanguageId, version, binary.Translations);
            }
            else if (bytes[0] == (byte)PayloadHeaderMagicPrefixByte)
            {
                using (IOReader reader = new IOReader(bytes, offset: 1, size: bytes.Length - 1))
                {
                    int schemaVersion = reader.ReadVarInt();
                    if (schemaVersion == 1)
                    {
                        CompressionAlgorithm compressionAlgorithm = (CompressionAlgorithm)reader.ReadVarInt();

                        if (compressionAlgorithm == CompressionAlgorithm.Deflate)
                        {
                            bytes = CompressUtil.DeflateDecompress(bytes, offset: reader.Offset);
                            BinaryV1 binary = MetaSerialization.DeserializeTagged<BinaryV1>(bytes, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                            return new LocalizationLanguage(binary.LanguageId, version, binary.Translations);
                        }
                        else
                            throw new InvalidOperationException($"Invalid compression algorithm {compressionAlgorithm}");
                    }
                    else
                        throw new InvalidOperationException($"Invalid schema version: {schemaVersion}");
                }
            }
            else
                throw new InvalidOperationException($"Expected {nameof(bytes)} to start with either byte {(byte)WireDataType.NullableStruct} ({nameof(WireDataType)}.{nameof(WireDataType.NullableStruct)}) or {PayloadHeaderMagicPrefixByte}, but it starts with {bytes[0]}");
        }
    }

    /// <summary>
    /// Provider for conveniently fetching and parsing <see cref="LocalizationLanguage"/> from a backing
    /// <see cref="IBlobProvider"/>. Does de-duplicating in-memory caching for the versioned language files.
    ///
    /// Note: currently fetched data is never forgotten, so they will slowly leak memory over time.
    /// </summary>
    public class LocalizationLanguageProvider
    {
        public IBlobProvider                                                        BlobProvider { get; }

        string                                                                      _basePath;
        ConcurrentCache<ValueTuple<LanguageId, ContentHash>, LocalizationLanguage>  _cache = new ConcurrentCache<(LanguageId, ContentHash), LocalizationLanguage>();

        public LocalizationLanguageProvider(IBlobProvider blobProvider, string basePath)
        {
            BlobProvider    = blobProvider;
            _basePath       = basePath;
        }

        public async Task<LocalizationLanguage> GetAsync(LanguageId languageId, ContentHash version)
        {
            return await _cache.GetAsync((languageId, version), async ((LanguageId langId, ContentHash hash) t) =>
            {
                string configName = Path.Combine(_basePath, $"{t.langId}.mpc");
                byte[] bytes = await BlobProvider.GetAsync(configName, t.hash);
                return LocalizationLanguage.FromBytes(t.langId, version, bytes);
            });
        }
    }
}
