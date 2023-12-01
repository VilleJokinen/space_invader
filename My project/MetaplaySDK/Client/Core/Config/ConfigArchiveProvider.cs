// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Threading.Tasks;

namespace Metaplay.Core.Config
{
    public interface ConfigArchiveProvider
    {
        Task<ConfigArchive> GetAsync(ContentHash version);

        /// <summary>
        /// Stores the config archive into the the provider. On failure, throws.
        /// </summary>
        Task PutAsync(ConfigArchive archive);
    }

    public class StaticConfigArchiveProvider : ConfigArchiveProvider
    {
        ConfigArchive _archive;

        public StaticConfigArchiveProvider(ConfigArchive archive)
        {
            _archive = archive;
        }

        #region ConfigArchiveProvider

        public Task<ConfigArchive> GetAsync(ContentHash version)
        {
            if (version != _archive.Version)
                throw new InvalidOperationException($"StaticConfigArchiveProvider.GetAsync({version}): can only get {_archive.Version}");

            return Task.FromResult(_archive);
        }

        public Task PutAsync(ConfigArchive archive)
        {
            throw new InvalidOperationException($"Not supported");
        }

        #endregion
    }

    public class BlobConfigArchiveProvider : ConfigArchiveProvider
    {
        IBlobProvider   _blobProvider;
        string          _configName;

        public BlobConfigArchiveProvider(IBlobProvider blobProvider, string configName)
        {
            _blobProvider   = blobProvider;
            _configName     = configName;
        }

        #region ConfigArchiveProvider

        public async Task<ConfigArchive> GetAsync(ContentHash version)
        {
            byte[] bytes = await _blobProvider.GetAsync(_configName, version);
            return (bytes != null) ? ConfigArchive.FromBytes(bytes) : null;
        }

        public async Task PutAsync(ConfigArchive archive)
        {
            byte[] bytes = ConfigArchiveBuildUtility.ToBytes(archive);
            await _blobProvider.PutAsync(_configName, archive.Version, bytes);
        }

        #endregion
    }
}
