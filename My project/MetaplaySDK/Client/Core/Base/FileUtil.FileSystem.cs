// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !UNITY_WEBGL || UNITY_EDITOR
#   define HAS_FILESYSTEM
#endif

// In pure .net, we dont want to expose SyncIO
#if !NETCOREAPP
#   define HAS_SYNCHRONOUS_IO
#endif

#if HAS_FILESYSTEM
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is non-WebGL.

using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Core.Impl
{
    public class FileUtilImplFileSystem
    {
        public static async Task<bool> WriteAllBytesAtomicAsync(string filePath, byte[] bytes)
        {
            try
            {
                string tempPath = filePath + ".new";
                using (FileAccessLock fsLock = await FileAccessLock.AcquireAsync(filePath).ConfigureAwait(false))
                {
                    // Write to a temp file, and then Move over to the destination. We use lock to prevent others clobbering the temp file.
                    //
                    // For Windows-like OSs, we cannot "MoveFile" over existing file. Instead, we must "ReplaceFile". Replace does not work if destination doesn't exist.
                    // On Posix-like OSs, "rename" would work on in both cases. But dotnet exposes windows-like API where the preconditions are first checked like on Windows
                    // but then ends up calling rename anyway.
                    //
                    // Since we have a lock, we don't need to handle races

                    await WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);

                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Replace(sourceFileName: tempPath, destinationFileName: filePath, destinationBackupFileName: null);
                        }
                        catch
                        {
                            // On windows, there are stray locks every now and then. Windows defender?
                            // Fall back to two-step operation which is no longer atomic.
                            // To avoid non-atomicy issues, we only do this on Development platforms, i.e. (Server on Windows) and (Unity Editor on Windows)
                            #if NETCOREAPP || UNITY_EDITOR
                            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                            {
                                File.Delete(filePath);
                                File.Move(sourceFileName: tempPath, destFileName: filePath);
                                return true;
                            }
                            #endif
                            throw;
                        }
                    }
                    else
                        File.Move(sourceFileName: tempPath, destFileName: filePath);

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task WriteAllBytesAsync(string filePath, byte[] bytes)
        {
            await File.WriteAllBytesAsync(filePath, bytes);
        }

        public static async Task WriteAllTextAsync(string filePath, string text)
        {
            await File.WriteAllTextAsync(filePath, text);
        }

        public static Task DeleteAsync(string filePath)
        {
            // There is no File.DeleteAsync(). To prevent this from potentially blocking on server,
            // we throttle deletions to at most one at a time.
            // \note: To throttle, we acquire a lock to pseudo path /?/delete-lock. Since the path
            //        contains '?' it's unlikely there would be real file-access to that path.
            return Task.Run(async () =>
            {
                using (FileAccessLock fsLock = await FileAccessLock.AcquireAsync("/?/delete-lock"))
                {
                    File.Delete(filePath);
                }
            });
        }

        public async static Task<string> ReadAllTextAsync(string filePath)
        {
            using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader textReader = new StreamReader(fileStream, System.Text.Encoding.UTF8))
                return await textReader.ReadToEndAsync();
        }

        public async static Task<byte[]> ReadAllBytesAsync(string filePath)
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buffer = new byte[fileStream.Length];
                int numBytesRead = await fileStream.ReadAsync(buffer);
                if (numBytesRead != buffer.Length)
                    throw new IOException($"Unable to read all bytes from file '{filePath}', only read {numBytesRead} out of {buffer.Length} bytes");
                return buffer;
            }
        }

#if HAS_SYNCHRONOUS_IO

        public static void DeleteSync(string filePath) => File.Delete(filePath);

        public static void WriteAllBytesSync(string filePath, byte[] bytes) => File.WriteAllBytes(filePath, bytes);

        public static void WriteAllTextSync(string filePath, string contents) => File.WriteAllText(filePath, contents);

#endif

        // \note: Legacy filesystem sync IO exposed

        public static string ReadAllTextSync(string filePath)
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader textReader = new StreamReader(fileStream, System.Text.Encoding.UTF8))
                return textReader.ReadToEnd();
        }

        public static byte[] ReadAllBytesSync(string filePath)
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BinaryReader binaryReader = new BinaryReader(fileStream))
                return binaryReader.ReadBytes((int)fileStream.Length);
        }
    }
}

#pragma warning restore MP_WGL_00
#endif // HAS_FILESYSTEM
