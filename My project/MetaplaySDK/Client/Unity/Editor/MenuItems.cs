// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Metaplay.Unity
{
    public class MenuItems
    {
        [MenuItem("Metaplay/Clear Credentials", isValidateFunction: false, priority = 100)]
        public static void ClearCredentials()
        {
            Debug.Log("Clearing credentials");
            CredentialsStore.ClearCredentialsInEditor();
        }

        [MenuItem("Metaplay/Clear GameConfig Cache", isValidateFunction: false, priority = 101)]
        public static void ClearGameConfigCache()
        {
            string cachePath = Path.Combine(Application.persistentDataPath, "GameConfigCache"); // \todo [petri] move hard-coded path to some config

            if (Directory.Exists(cachePath))
            {
                Debug.Log($"Clearing cache directory: {cachePath}");
                DirectoryInfo dirInfo = new DirectoryInfo(cachePath);
                dirInfo.Delete(recursive: true);
            }
            else
                Debug.Log($"Cache directory does not exist: {cachePath}");
        }

        [MenuItem("Metaplay/Delete Offline Persisted State", isValidateFunction: false, priority = 102)]
        public static void DeleteOfflinePersistedState()
        {
            string filePath = DefaultOfflineServer.GetPersistedStatePath();
            if (AtomicBlobStore.TryDeleteBlob(filePath))
                Debug.Log($"Removed offline persisted state '{filePath}'");
            else
                Debug.Log($"No offline persisted state found in '{filePath}'");
        }

        [MenuItem("Metaplay/Destructive/Delete Local Database", isValidateFunction: false, priority = 200)]
        public static void DeleteLocalDatabase()
        {
            string path = "Backend/Server/bin";
            string[] databaseFiles = Directory.GetFiles(path, "*.db");
            foreach (string file in databaseFiles)
            {
                File.Delete(file);
            }
        }
    }
}
