// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !UNITY_WEBGL || UNITY_EDITOR

using System;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    public static partial class DirectoryUtil
    {
        public static partial Task<string[]> GetDirectoryFilesAsync(string directory)
        {
            try
            {
                string[] files = Directory.GetFiles(directory);
                CanonizeFolderSeparatorsInPlace(files);
                return Task.FromResult(files);
            }
            catch
            {
                return Task.FromResult(Array.Empty<string>());
            }
        }

        public static partial Task<string[]> GetDirectoryAndSubdirectoryFilesAsync(string directory)
        {
            try
            {
                string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                CanonizeFolderSeparatorsInPlace(files);
                return Task.FromResult(files);
            }
            catch
            {
                return Task.FromResult(Array.Empty<string>());
            }
        }

        static void CanonizeFolderSeparatorsInPlace(string[] paths)
        {
            // Always have the forward slash as the directory separator
            for (int ndx = 0; ndx < paths.Length; ++ndx)
                paths[ndx] = paths[ndx].Replace('\\', '/');
        }

        public static partial Task EnsureDirectoryExistsAsync(string directory)
        {
            try
            {
                _ = Directory.CreateDirectory(directory);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }
    }
}

#endif
