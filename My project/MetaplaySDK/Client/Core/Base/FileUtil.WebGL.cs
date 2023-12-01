// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

#if UNITY_WEBGL_BUILD

using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Core.Impl
{
    public class FileUtilImplWebGL
    {
        public static async Task<bool> WriteAllBytesAtomicAsync(string filePath, byte[] bytes)
        {
            try
            {
                await WebBlobStore.WriteFileAsync(filePath, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Task WriteAllBytesAsync(string filePath, byte[] bytes)
        {
            return WebBlobStore.WriteFileAsync(filePath, bytes);
        }

        public static Task WriteAllTextAsync(string filePath, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return WriteAllBytesAsync(filePath, bytes);
        }

        public static Task DeleteAsync(string filePath)
        {
            return WebBlobStore.DeleteFileAsync(filePath);
        }

        public static async Task<string> ReadAllTextAsync(string filePath)
        {
            byte[] bytes = await WebBlobStore.ReadFileAsync(filePath);
            return Encoding.UTF8.GetString(bytes);
        }

        public static async Task<byte[]> ReadAllBytesAsync(string filePath)
        {
            return await WebBlobStore.ReadFileAsync(filePath);
        }
    }
}

#endif
