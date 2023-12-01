// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#define UNITY_WEBGL_BUILD
#endif

// Enable synchronous APIs on Unity-clients, unless it's a webgl build. Server should use async APIs,
// or directly File-API if sync API is required.
#if UNITY_2017_1_OR_NEWER && !UNITY_WEBGL_BUILD
#   define HAS_SYNCHRONOUS_IO
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

#if UNITY_WEBGL_BUILD
using FileUtilImpl = Metaplay.Core.Impl.FileUtilImplWebGL;
#else
using FileUtilImpl = Metaplay.Core.Impl.FileUtilImplFileSystem;
#endif

namespace Metaplay.Core
{
    public static partial class FileUtil
    {
#if !UNITY_WEBGL_BUILD

        //[Obsolete("Use asynchronous IO instead!")]
        public static string ReadAllText(string filePath)
        {
#if UNITY_2017_1_OR_NEWER && !UNITY_EDITOR
            if (IsRemotePath(filePath))
                return ReadRemoteFileTextSync(filePath);
#endif

            return FileUtilImpl.ReadAllTextSync(filePath);
        }

        //[Obsolete("Use asynchronous IO instead!")]
        public static byte[] ReadAllBytes(string filePath)
        {
#if UNITY_2017_1_OR_NEWER && !UNITY_EDITOR
            if (IsRemotePath(filePath))
                return ReadRemoteFileBytesSync(filePath);
#endif

            return FileUtilImpl.ReadAllBytesSync(filePath);
        }

        //[Obsolete("Use asynchronous IO instead!")]
        public static string[] ReadAllLines(string filePath)
        {
            return SplitToLines(ReadAllText(filePath));
        }

#endif

        public static async Task<string> ReadAllTextAsync(string filePath)
        {
#if UNITY_2017_1_OR_NEWER && !UNITY_EDITOR
            if (IsRemotePath(filePath))
                return await ReadRemoteFileTextAsync(filePath);
#endif

            return await FileUtilImpl.ReadAllTextAsync(filePath);
        }

        public static async Task<byte[]> ReadAllBytesAsync(string filePath)
        {
#if UNITY_2017_1_OR_NEWER && !UNITY_EDITOR
            if (IsRemotePath(filePath))
                return await ReadRemoteFileBytesAsync(filePath);
#endif

            return await FileUtilImpl.ReadAllBytesAsync(filePath);
        }

        public static async Task<string[]> ReadAllLinesAsync(string filePath)
        {
            return SplitToLines(await ReadAllTextAsync(filePath));
        }

        /// <summary>
        /// Write a file atomically. With normal file system, we first write the file with a .new suffix
        /// in the name, which is then renamed to the final name. Note that the .new files must be explicitly
        /// cleared by the user, as such files may get left behind if the operation is terminated abruptly.
        /// In WebGL, the writes to IndexedDB are atomic, so no suffixing is needed.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="bytes"></param>
        /// <returns>True on success, false on failure</returns>
        public static Task<bool> WriteAllBytesAtomicAsync(string filePath, byte[] bytes) => FileUtilImpl.WriteAllBytesAtomicAsync(filePath, bytes);

        /// <summary>
        /// Writes a file. If the file already exits, it is overwritten. If write fails or the directory does not exits, the call throws.
        ///
        /// <para>
        /// The write is not atomic and if application is interrupted during the write, it may results in no file written, empty file written (truncating old file), a partial write (truncated new file), or a succesful complete write.
        /// </para>
        /// </summary>
        public static Task WriteAllBytesAsync(string filePath, byte[] bytes) => FileUtilImpl.WriteAllBytesAsync(filePath, bytes);

        /// <inheritdoc cref="WriteAllBytesAsync"/>
        public static Task WriteAllTextAsync(string filePath, string text) => FileUtilImpl.WriteAllTextAsync(filePath, text);

        /// <summary>
        /// Delete a file asynchronously. If the file doesn't exist, nothing happens.
        /// If trying to delete a directory, an exception is thrown.
        /// <para>
        /// Warning: There is no asynchronous delete operation in .NET nor Unity (except WebGL builds).
        /// In Unity, we emulate the deletion with a synchronous one.
        /// </para>
        /// </summary>
        public static Task DeleteAsync(string filePath) => FileUtilImpl.DeleteAsync(filePath);

#if HAS_SYNCHRONOUS_IO

        /// <summary>
        /// Deletes a file. If the file doesn't exist, nothing happens.
        /// If trying to delete a directory, an exception is thrown.
        /// </summary>
        public static void Delete(string filePath) => FileUtilImpl.DeleteSync(filePath);

        /// <inheritdoc cref="WriteAllBytesAsync(string, byte[])"/>
        public static void WriteAllBytes(string filePath, byte[] bytes) => FileUtilImpl.WriteAllBytesSync(filePath, bytes);

        /// <inheritdoc cref="WriteAllBytesAsync(string, byte[])"/>
        public static void WriteAllText(string filePath, string contents) => FileUtilImpl.WriteAllTextSync(filePath, contents);

#endif

#if UNITY_2017_1_OR_NEWER && !UNITY_EDITOR
        /// <summary>
        /// Checks if the path requires loading using <c>UnityWebRequest</c>.
        /// </summary>
        /// <returns>True if the path should be loaded with UnityWebRequest</returns>
        public static bool IsRemotePath(string filePath)
        {
#if UNITY_ANDROID
            return filePath.Contains("jar:file:/");
#elif UNITY_WEBGL_BUILD
            return filePath.Contains("://"); // usually http:// or https:// but could be other transports
#else
            return false;
#endif
        }

#if !UNITY_WEBGL_BUILD
        public static byte[] ReadRemoteFileBytesSync(string filePath)
        {
            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(filePath))
            {
                request.SendWebRequest();
                while (!request.isDone) {}
                return request.downloadHandler.data;
            }
        }
#endif

        public static async Task<byte[]> ReadRemoteFileBytesAsync(string filePath)
        {
            using (UnityEngine.Networking.UnityWebRequest request = await UnityEngine.Networking.UnityWebRequest.Get(filePath).SendWebRequest())
            {
                return request.downloadHandler.data;
            }
        }

#if !UNITY_WEBGL_BUILD
        public static string ReadRemoteFileTextSync(string filePath)
        {
            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(filePath))
            {
                request.SendWebRequest();
                while (!request.isDone) {}
                return request.downloadHandler.text;
            }
        }
#endif

        public static async Task<string> ReadRemoteFileTextAsync(string filePath)
        {
            using (UnityEngine.Networking.UnityWebRequest request = await UnityEngine.Networking.UnityWebRequest.Get(filePath).SendWebRequest())
            {
                return request.downloadHandler.text;
            }
        }
#endif // Unity build

        /// <summary>
        /// Normalize a path with the following rules:
        /// - Retain both relative and absolute paths.
        /// - Remove any "." parts in the path.
        /// - Remove any ".." by traversing up in the directory hierarchy.
        /// - Remove final directory suffix (except for root paths "/" and "C:/").
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static string NormalizePath(string path)
        {
            if (path == "")
                return "";

            string[] parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            bool isAbsolute = parts[0] == "" || parts[0].Contains(":");
            int numRelativeParents = 0;

            Stack<string> output = new Stack<string>();
            for (int ndx = isAbsolute ? 1 : 0; ndx < parts.Length; ndx++)
            {
                string part = parts[ndx];
                if (part == "")
                {
                    // \note ndx == 0 is handled above
                    if (ndx == parts.Length - 1)
                        continue; // ignore last empty part (separator suffixed path)
                    else
                        throw new ArgumentException($"Path '{path}' is invalid: contains two subsequent directory separators");
                }
                else if (part == "..")
                {
                    if (output.Count > 0)
                        output.Pop();
                    else if (isAbsolute)
                        throw new ArgumentException($"Absolute path '{path}' references the parent of the root directory");
                    else
                        numRelativeParents++;
                }
                else if (part == ".")
                    continue; // ignore "."
                else
                    output.Push(part);
            }

            if (isAbsolute)
                return $"{parts[0]}/" + string.Join("/", output.Reverse());
            else
                return string.Join("/", Enumerable.Repeat("..", numRelativeParents).Concat(output.Reverse()));
        }

        static string[] SplitToLines(string contents)
        {
            string line;
            List<string> lines = new List<string>();

            using (StringReader sr = new StringReader(contents))
                while ((line = sr.ReadLine()) != null)
                    lines.Add(line);

            return lines.ToArray();
        }
    }
}
