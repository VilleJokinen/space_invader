// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR

using Metaplay.Core;
using System;
using System.Runtime.InteropServices;

namespace Metaplay.Unity
{
    // \todo [petri] uses base64 encoding, but localStorage persists everything as UTF-16, so there are more efficient encodings available
    public static class AtomicBlobStore
    {
        static bool _initialized = false;

        public static void Initialize()
        {
            if (!_initialized)
            {
                string errorStr = AtomicBlobStoreJs_Initialize(MetaplayCore.Options.ProjectName);
                if (errorStr != null)
                    DebugLog.Warning("AtomicBlobStore.Initialize failed: {0}", errorStr);

                _initialized = true;
            }
        }

        public static byte[] TryReadBlob(string path)
        {
            if (!_initialized)
                throw new InvalidOperationException($"AtomicBlobStore not yet initialized");

            string bytes64 = AtomicBlobStoreJs_ReadBlob(path);
            // DebugLog.Info("AtomicBlobStore.TryReadBlob(): {Bytes}", bytes64 != null ? bytes64 : "<null>");
            if (bytes64 != null)
                return Convert.FromBase64String(bytes64);
            else
                return null;
        }

        public static bool TryWriteBlob(string path, byte[] blob)
        {
            if (!_initialized)
                throw new InvalidOperationException($"AtomicBlobStore not yet initialized");

            // DebugLog.Info("AtomicBlobStore.TryWriteBlob(): {Path} {Length} bytes", path, blob.Length);
            string errorStr = AtomicBlobStoreJs_WriteBlob(path, Convert.ToBase64String(blob));
            if (errorStr != null)
            {
                DebugLog.Warning("AtomicBlobStore.TryWriteBlob failed: {0}", errorStr);
                return false;
            }
            return true;
        }

        public static bool TryDeleteBlob(string path)
        {
            if (!_initialized)
                throw new InvalidOperationException($"AtomicBlobStore not yet initialized");

            // DebugLog.Info("AtomicBlobStore.TryDeleteBlob(): {Path}");
            AtomicBlobStoreJs_DeleteBlob(path);
            return true;
        }

        [DllImport("__Internal")] static extern string AtomicBlobStoreJs_Initialize(string projectName);
        [DllImport("__Internal")] static extern string AtomicBlobStoreJs_WriteBlob(string path, string bytes64);
        [DllImport("__Internal")] static extern string AtomicBlobStoreJs_ReadBlob(string path);
        [DllImport("__Internal")] static extern void AtomicBlobStoreJs_DeleteBlob(string path);
    }
}

#endif
