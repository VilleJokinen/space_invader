// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Threading.Tasks;

namespace Metaplay.Core
{
    public static partial class DirectoryUtil
    {
        /// <summary>
        /// Lists files in a directory and returns the full paths of the files. If directory
        /// does not exist or cannot be accessed, an empty array is returned.
        /// </summary>
        public static partial Task<string[]> GetDirectoryFilesAsync(string directory);

        /// <summary>
        /// Lists files in a directory and subdirectories and returns the full paths of the files. If directory
        /// does not exist or cannot be accessed, an empty array is returned.
        /// </summary>
        public static partial Task<string[]> GetDirectoryAndSubdirectoryFilesAsync(string directory);

        /// <summary>
        /// Creates the directory and all parent directories if they do no already exist.
        /// </summary>
        public static partial Task EnsureDirectoryExistsAsync(string directory);
    }
}
