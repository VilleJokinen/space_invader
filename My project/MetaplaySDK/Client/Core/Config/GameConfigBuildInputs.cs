// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Forms;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// The necessary data to identify a game config build data source.
    /// </summary>
    /// This data is used in GameConfigBuildParameters to declare the active source(s) to be used by the game config
    /// build. Note that the parameters are also serialized into the built game config metadata. No sensitive info
    /// such as credentials should be communicated as part of the source.
    [MetaSerializable]
    public abstract class GameConfigBuildSource
    {
        protected GameConfigBuildSource() { }
        [MetaFormDontCaptureDefault]
        public abstract string DisplayName { get; }
    }

    /// <summary>
    /// Metadata about a game config build source captured at the time of the build.
    /// </summary>
    /// Any mutable data related to the build source is captured by the game config build
    /// at the time running the build as build source metadata.
    [MetaSerializable, MetaAllowNoSerializedMembers]
    public abstract class GameConfigBuildSourceMetadata
    {
    }

    class GoogleSheetsIdValidator : MetaFormValidator<string>
    {
        static readonly Regex validator = new Regex("^([a-zA-Z0-9-_]{15,})$", RegexOptions.Compiled);
        public override void Validate(string fieldOrForm, FormValidationContext ctx)
        {
            if(fieldOrForm == null || fieldOrForm.Length < 15)
                ctx.Fail("This field has a minimum length of 15.");
            else if (!validator.IsMatch(fieldOrForm))
                ctx.Fail("This field may only contain a-z, A-Z, 0-9, -, and _.");
        }
    }

    [MetaSerializableDerived(101)]
    public class GoogleSheetBuildSource : GameConfigBuildSource
    {
        [MetaMember(1),
         MetaValidateRequired,
         MetaFormDisplayProps(
            displayName: "Google Spreadsheet Name",
            DisplayHint = "Name of the Google Spreadsheet to use as a data source.",
            DisplayPlaceholder = "Enter Google Spreadsheet Name")]
        public string Name    { get; private set; }
        [MetaMember(2),
         MetaFormFieldCustomValidator(typeof(GoogleSheetsIdValidator)),
         MetaFormDisplayProps(
            displayName: "Google Spreadsheet ID",
            DisplayHint = "ID of the Google Spreadsheet to use as a data source.",
            DisplayPlaceholder = "Enter Google Spreadsheet ID")]
        public string SpreadsheetId { get; private set; }

        GoogleSheetBuildSource() { }

        public GoogleSheetBuildSource(string name, string spreadsheetId)
        {
            Name    = name;
            SpreadsheetId = spreadsheetId;
        }

        public override string DisplayName => Name;

        protected bool Equals(GoogleSheetBuildSource other)
        {
            return Name == other.Name && SpreadsheetId == other.SpreadsheetId;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((GoogleSheetBuildSource)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, SpreadsheetId);
        }
    }

    [MetaSerializableDerived(101)]
    public class GoogleSheetBuildSourceMetadata : GameConfigBuildSourceMetadata
    {
        [MetaMember(1)] public string SpreadsheetTitle { get; set; }

        public static GoogleSheetBuildSourceMetadata FromSpreadsheetMetadata(GoogleSpreadsheetMetadata metadata)
        {
            return new GoogleSheetBuildSourceMetadata { SpreadsheetTitle = metadata.Title };
        }
    }

    [MetaSerializableDerived(102), MetaFormHidden]
    public class FileSystemBuildSource : GameConfigBuildSource
    {
        protected bool Equals(FileSystemBuildSource other)
        {
            return FileFormat == other.FileFormat;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((FileSystemBuildSource)obj);
        }

        public override int GetHashCode()
        {
            return (int)FileFormat;
        }

        [MetaSerializable]
        public enum Format
        {
            Binary,
            Csv,
        }

        [MetaMember(1)] public Format FileFormat;
        public override string DisplayName => $"LocalFile/{FileFormat}";

        FileSystemBuildSource() { }
        public FileSystemBuildSource(Format fileFormat)
        {
            FileFormat = fileFormat;
        }
    }

    public class StaticSourceDataItem : IGameConfigSourceData
    {
        object _data;

        public StaticSourceDataItem(object data)
        {
            _data = data;
        }
        public Task<object> Get()
        {
            return Task.FromResult(_data);
        }
    }

    [MetaSerializableDerived(103), MetaFormHidden, MetaAllowNoSerializedMembers]
    public class SingleStaticDataBuildSource : GameConfigBuildSource, IGameConfigSourceFetcher
    {
        public override string DisplayName => "SingleStaticData";
        StaticSourceDataItem _data;

        SingleStaticDataBuildSource() { }
        public SingleStaticDataBuildSource(object data)
        {
            _data = new StaticSourceDataItem(data);
        }
        public IGameConfigSourceData Fetch(string itemName)
        {
            return _data;
        }

        public Task<GameConfigBuildSourceMetadata> GetMetadataAsync(CancellationToken ct)
        {
            return Task.FromResult<GameConfigBuildSourceMetadata>(null);
        }
    }

    [MetaSerializableDerived(104), MetaFormHidden, MetaAllowNoSerializedMembers]
    public class StaticDataDictionaryBuildSource : GameConfigBuildSource, IGameConfigSourceFetcher
    {
        public override string DisplayName => "StaticDataDictionary";
        Dictionary<string, StaticSourceDataItem> _dataDict;

        StaticDataDictionaryBuildSource() { }
        public StaticDataDictionaryBuildSource(IEnumerable<(string key, object payload)> data)
        {
            _dataDict = data.ToDictionary(x => x.key, x => new StaticSourceDataItem(x.payload));
        }
        public IGameConfigSourceData Fetch(string itemName)
        {
            return _dataDict[itemName];
        }
        public Task<GameConfigBuildSourceMetadata> GetMetadataAsync(CancellationToken ct)
        {
            return Task.FromResult<GameConfigBuildSourceMetadata>(null);
        }
    }

    public class GoogleSheetBuildSourceFetcher : IGameConfigSourceFetcher
    {
        public delegate Task<IReadOnlyList<SpreadsheetContent>> SheetFetchFunc(object credentials, string spreadsheetId, IEnumerable<string> tabNames, CancellationToken ct);

        class Spreadsheet : IGameConfigSourceData
        {
            GoogleSheetBuildSourceFetcher _fetcher;
            public string                 TabName;
            public SpreadsheetContent     Content;

            public Spreadsheet(GoogleSheetBuildSourceFetcher fetcher, string tabName)
            {
                _fetcher = fetcher;
                TabName  = tabName;
            }

            public async Task<object> Get()
            {
                if (Content == null)
                    await _fetcher.Run();
                return Content;
            }
        }

        GoogleSheetBuildSource _source;
        object                 _credentials;
        List<Spreadsheet>      _sheets = new List<Spreadsheet>();
        SheetFetchFunc         _fetchFunc;

        public GoogleSheetBuildSourceFetcher(GoogleSheetBuildSource source, object credentials, SheetFetchFunc customFetchFunc = null)
        {
            _source      = source;
            _credentials = credentials;
            _fetchFunc = customFetchFunc ?? GoogleSheetFetcher.Instance.FetchSheetsAsync;
        }

        public IGameConfigSourceData Fetch(string itemName)
        {
            Spreadsheet sheet = _sheets.Find(x => x.TabName == itemName);
            if (sheet == null)
            {
                sheet = new Spreadsheet(this, itemName);
                _sheets.Add(sheet);
            }

            return sheet;
        }

        async Task Run()
        {
            List<Spreadsheet>               sheetsToFetch = _sheets.Where(x => x.Content == null).ToList();
            IEnumerable<SpreadsheetContent> results       = await _fetchFunc(_credentials, _source.SpreadsheetId, sheetsToFetch.Select(x => x.TabName), CancellationToken.None);

            foreach ((SpreadsheetContent Value, int Index) result in results.ZipWithIndex())
                sheetsToFetch[result.Index].Content = result.Value;
        }
        public async Task<GameConfigBuildSourceMetadata> GetMetadataAsync(CancellationToken ct)
        {
            return GoogleSheetBuildSourceMetadata.FromSpreadsheetMetadata(await GoogleSheetFetcher.Instance.FetchSpreadsheetMetadataAsync(_credentials, _source.SpreadsheetId, ct));
        }
    }

    public class LocalFileBuildSourceFetcher : IGameConfigSourceFetcher
    {
        readonly string _directory;
        readonly FileSystemBuildSource.Format _fileFormat;

        class BinaryFile : IGameConfigSourceData
        {
            public readonly string Path;

            public BinaryFile(string path)
            {
                Path = path;
            }

            public async Task<object> Get()
            {
                return await FileUtil.ReadAllBytesAsync(Path);
            }
        }

        class CsvFile : IGameConfigSourceData
        {
            readonly BinaryFile _binary;

            public CsvFile(BinaryFile binary)
            {
                _binary = binary;
            }

            public async Task<object> Get()
            {
                byte[] data = (byte[])await _binary.Get();
                return GameConfigHelper.ParseCsvToSpreadsheet(_binary.Path, data);
            }
        }

        public LocalFileBuildSourceFetcher(string directory, FileSystemBuildSource.Format fileFormat)
        {
            _directory  = directory;
            _fileFormat = fileFormat;
        }

        public IGameConfigSourceData Fetch(string itemName)
        {
            string fileName = itemName;
            if (_fileFormat == FileSystemBuildSource.Format.Csv)
            {
                if (!fileName.EndsWith(".csv", StringComparison.Ordinal))
                    fileName += ".csv";
            }
            BinaryFile binary = new BinaryFile(Path.Combine(_directory, fileName));
            if (_fileFormat == FileSystemBuildSource.Format.Csv)
                return new CsvFile(binary);
            return binary;
        }

        public Task<GameConfigBuildSourceMetadata> GetMetadataAsync(CancellationToken ct)
        {
            return Task.FromResult<GameConfigBuildSourceMetadata>(null);
        }
    }

    public interface IGameConfigSourceData
    {
        Task<object> Get();
    }

    public interface IGameConfigSourceFetcher
    {
        IGameConfigSourceData Fetch(string itemName);
        Task<GameConfigBuildSourceMetadata> GetMetadataAsync(CancellationToken ct);
    }

    public interface IGameConfigSourceFetcherProvider
    {
        Task<IGameConfigSourceFetcher> GetFetcherForBuildSourceAsync(GameConfigBuildSource source, CancellationToken ct);
    }

    public class GameConfigSourceFetcherProvider<TConfig> : IGameConfigSourceFetcherProvider where TConfig : IGameConfigSourceFetcherConfig
    {
        protected TConfig Config { get; private set; }

        public GameConfigSourceFetcherProvider(TConfig config)
        {
            Config = config;
        }

        public virtual Task<IGameConfigSourceFetcher> GetFetcherForBuildSourceAsync(GameConfigBuildSource source, CancellationToken ct)
        {
            MetaDebug.Assert(source != null, $"Config build source can't be null");

            // handle SDK build source types
            switch (source)
            {
                case GoogleSheetBuildSource googleSheetBuildSource:
                    return GetGoogleSheetFetcherAsync(googleSheetBuildSource, ct);
                case FileSystemBuildSource fileSystemBuildSource:
                    return Task.FromResult(GetFileSystemFetcher(fileSystemBuildSource));
                case IGameConfigSourceFetcher staticData:
                    return Task.FromResult(staticData);
            }

            throw new NotImplementedException($"No config build fetcher available for build source {source} ({source.GetType()})");
        }

        protected virtual Task<IGameConfigSourceFetcher> GetGoogleSheetFetcherAsync(GoogleSheetBuildSource source, CancellationToken ct)
        {
            throw new NotImplementedException("Google sheets integration incomplete");
        }

        protected virtual IGameConfigSourceFetcher GetFileSystemFetcher(FileSystemBuildSource source)
        {
            throw new NotImplementedException("File system build source integration incomplete");
        }
    }

    public class DefaultGameConfigSourceFetcherProvider : GameConfigSourceFetcherProvider<GameConfigSourceFetcherConfigCore>
    {
        public DefaultGameConfigSourceFetcherProvider(GameConfigSourceFetcherConfigCore config) : base(config)
        {
        }

        protected override async Task<IGameConfigSourceFetcher> GetGoogleSheetFetcherAsync(GoogleSheetBuildSource source, CancellationToken ct)
        {
            if (Config == null)
                throw new InvalidOperationException($"Google sheets fetcher needs to be configured for build source {source}");
            object credentials = await Config.GetGoogleCredentialsAsync(ct);
            if (credentials == null)
                throw new InvalidOperationException($"Google sheets fetcher not configured for build source {source}");
            return new GoogleSheetBuildSourceFetcher(source, credentials, Config.CustomSheetFetchFunc);
        }

        protected override IGameConfigSourceFetcher GetFileSystemFetcher(FileSystemBuildSource source)
        {
            if (string.IsNullOrEmpty(Config?.LocalFileSourcesPath))
                throw new InvalidOperationException($"Local file sources path not configured for build source {source}");

            return new LocalFileBuildSourceFetcher(Config.LocalFileSourcesPath, source.FileFormat);
        }
    }

    public interface IGameConfigSourceFetcherConfig { }

    public class GameConfigSourceFetcherConfigCore : IGameConfigSourceFetcherConfig
    {
        public string GoogleCredentialsJson;
        public string GoogleCredentialsFilePath;
        public (string, string)? GoogleCredentialsClientSecret;
        public string LocalFileSourcesPath;
        public GoogleSheetBuildSourceFetcher.SheetFetchFunc CustomSheetFetchFunc;

        protected GameConfigSourceFetcherConfigCore() { }

        protected GameConfigSourceFetcherConfigCore Clone()
        {
            return (GameConfigSourceFetcherConfigCore)this.MemberwiseClone();
        }

        public static GameConfigSourceFetcherConfigCore Create()
        {
            return new GameConfigSourceFetcherConfigCore();
        }

        void ClearGoogleCredentialsFields()
        {
            GoogleCredentialsJson         = null;
            GoogleCredentialsFilePath     = null;
            GoogleCredentialsClientSecret = null;
        }

        public GameConfigSourceFetcherConfigCore WithGoogleCredentialsJson(string googleCredentialsJson)
        {
            GameConfigSourceFetcherConfigCore ret = Clone();
            ret.ClearGoogleCredentialsFields();
            ret.GoogleCredentialsJson = googleCredentialsJson;
            return ret;
        }

        public GameConfigSourceFetcherConfigCore WithGoogleCredentialsFilePath(string googleCredentialsFilePath)
        {
            GameConfigSourceFetcherConfigCore ret = Clone();
            ret.ClearGoogleCredentialsFields();
            ret.GoogleCredentialsFilePath = googleCredentialsFilePath;
            return ret;
        }

        public GameConfigSourceFetcherConfigCore WithGoogleCredentialsFromUserInput(string clientId, string clientSecret)
        {
            GameConfigSourceFetcherConfigCore ret = Clone();
            ret.ClearGoogleCredentialsFields();
            ret.GoogleCredentialsClientSecret = (clientId, clientSecret);
            return ret;
        }

        public GameConfigSourceFetcherConfigCore WithLocalFileSourcesPath(string localFileSourcesPath)
        {
            GameConfigSourceFetcherConfigCore ret = Clone();
            ret.LocalFileSourcesPath = localFileSourcesPath;
            return ret;
        }

        public async Task<object> GetGoogleCredentialsAsync(CancellationToken ct)
        {
            if (GoogleCredentialsClientSecret != null)
                return await GoogleSheetFetcher.Instance.CredentialsFromUserInputAsync(GoogleCredentialsClientSecret?.Item1, GoogleCredentialsClientSecret?.Item2, ct);
            if (GoogleCredentialsFilePath != null)
                return await GoogleSheetFetcher.Instance.CredentialsFromFileAsync(GoogleCredentialsFilePath, ct);
            if (GoogleCredentialsJson != null)
                return GoogleSheetFetcher.Instance.CredentialsFromJsonString(GoogleCredentialsJson);
            return null;
        }
    }
}
