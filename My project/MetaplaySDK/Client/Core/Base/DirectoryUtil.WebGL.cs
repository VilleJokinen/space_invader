// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR

using System.Threading.Tasks;

namespace Metaplay.Core
{
    public static partial class DirectoryUtil
    {
        public static partial Task<string[]> GetDirectoryFilesAsync(string directory)
        {
            return WebBlobStore.ScanBlobsInDirectory(directory, recursive: false);
        }

        public static partial Task<string[]> GetDirectoryAndSubdirectoryFilesAsync(string directory)
        {
            return WebBlobStore.ScanBlobsInDirectory(directory, recursive: true);
        }

        public static partial Task EnsureDirectoryExistsAsync(string directory)
        {
            return Task.CompletedTask;
        }
    }
}

#endif
