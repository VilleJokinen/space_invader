// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    /// <summary>
    /// Interface for storing binary blobs in a backing store. Implementations include on-disk
    /// and cloud-based (S3) storage. Cloud-based storage can also be used to distribute files
    /// to clients in a scalable manner (eg, over CloudFront).
    /// </summary>
    public interface IBlobStorage : IDisposable
    {
        Task<byte[]>    GetAsync    (string fileName);

        /// <summary>
        /// Puts the blob into the storage. On failure, throws.
        /// </summary>
        Task            PutAsync    (string fileName, byte[] bytes, BlobStoragePutHints hintsMaybe = null);
        Task            DeleteAsync (string fileName);
    }

    public class BlobStoragePutHints
    {
        /// <summary>
        /// If non-null, sets the ContentType of the Put file. Ignored if BlobStorage does not support content types.
        /// </summary>
        public string ContentType = null;
    }

    /// <summary>
    /// On-disk binary blob storage.
    /// </summary>
    public class DiskBlobStorage : IBlobStorage
    {
        string _dirName;

        public DiskBlobStorage(string dirName)
        {
            _dirName = dirName ?? throw new ArgumentNullException(nameof(dirName));

            if (!Directory.Exists(dirName))
                Directory.CreateDirectory(dirName);
        }

        public void Dispose()
        {
            // nada
        }

        /// <summary>
        /// Returns all the files in the given directory, without any path prefix.
        /// </summary>
        /// <param name="directory"></param>
        /// <returns></returns>
        public string[] GetFilesSync(string directory)
        {
            return Directory.GetFiles(Path.Combine(_dirName, directory))
                .Select(filePath => Path.GetFileName(filePath))
                .ToArray();
        }

        #region IBlobRepository

        public async Task<byte[]> GetAsync(string fileName)
        {
            try
            {
                //DebugLog.Info("DiskBlobStorage.GetAsync({0}): fetch from base provider", fileName);
                string filePath = Path.Combine(_dirName, fileName);
                byte[] bytes = await FileUtil.ReadAllBytesAsync(filePath).ConfigureAwaitFalse();
                //DebugLog.Info("DiskBlobStorage.GetAsync({0}): read {1} bytes", fileName, bytes.Length);
                return bytes;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (Exception ex)
            {
                // \todo [petri] re-throw some exceptions?
                DebugLog.Info("DiskBlobStorage.GetAsync({FileName}): read failed: {Exception}", fileName, ex);
                return null;
            }
        }

        public async Task PutAsync(string fileName, byte[] bytes, BlobStoragePutHints hintsMaybe = null)
        {
            // Create directory, if doesn't exist yet
            string dirName = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(dirName))
            {
                string fullDirName = Path.Combine(_dirName, dirName);
                // \todo [petri] Should handle differently in WebGL builds, but doesn't cause issues right now.
                if (!Directory.Exists(fullDirName))
                    Directory.CreateDirectory(fullDirName);
            }

            // Write file atomically. With real file system, write a .new suffixed file first, which is then renamed.
            // \todo: should we clean up the .new files?
            string fullDestinationPath = Path.Combine(_dirName, fileName);
            bool isSuccess = await FileUtil.WriteAllBytesAtomicAsync(fullDestinationPath, bytes).ConfigureAwaitFalse();
            if (!isSuccess)
                throw new IOException($"IO write to {fileName} failed.");
        }

        public async Task DeleteAsync(string fileName)
        {
            await FileUtil.DeleteAsync(Path.Combine(_dirName, fileName)).ConfigureAwaitFalse();
        }

        #endregion // IBlobRepository
    }
}
