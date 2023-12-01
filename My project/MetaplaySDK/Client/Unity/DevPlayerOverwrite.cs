using Metaplay.Core.Message;
using Metaplay.Core.Tasks;
using Metaplay.Unity;
using System.Threading.Tasks;
using UnityEngine;

// Example use of the developer player overwrite feature.
// Allows overwriting current player with entity archive given in "ImportPlayer". When running in editor the archive can be pasted
// into the inspector GUI.
public class DevPlayerOverwrite : MonoBehaviour
{
    [TextArea(3, 50)]
    public string ImportPlayer;

    void Update()
    {
        if (MetaplaySDK.Connection.State.Status == ConnectionStatus.Connected && !string.IsNullOrEmpty(ImportPlayer))
            SubmitOverwrite();
    }

    void SubmitOverwrite()
    {
        string archiveToImport = ImportPlayer;
        ImportPlayer = null;
        MetaTask.Run(async () => await OverwritePlayer(archiveToImport));
    }

    async Task OverwritePlayer(string archive)
    {
        DevOverwritePlayerStateRequest req = new DevOverwritePlayerStateRequest(archive);
        try
        {
            DevOverwritePlayerStateFailure response = await MetaplaySDK.MessageDispatcher.SendRequestAsync<DevOverwritePlayerStateFailure>(req);
            Debug.LogError("Player overwrite failed!");
            if (response.Reason != null)
                Debug.LogError(response.Reason);
        }
        catch (TaskCanceledException)
        {
            // Session will terminate on successful overwrite, no response is expected
        }
    }
}
