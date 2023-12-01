// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
package io.metaplay.android.blockstore;

import com.google.android.gms.auth.blockstore.*;
import com.google.android.gms.common.api.ApiException;
import com.unity3d.player.*;
import java.util.ArrayList;

public final class MetaplayGmsBlockStore
{
    static BlockstoreClient s_client;

    static void ensureClient()
    {
        if (s_client == null)
            s_client = Blockstore.getClient(UnityPlayer.currentActivity.getApplicationContext());
    }

    public static void WriteBlock(String label, byte[] block, boolean shouldBackupToCloud, IMetaplayGmsBlockStoreCallback cb)
    {
        ensureClient();

        StoreBytesData data = new StoreBytesData.Builder()
            .setKey(label)
            .setBytes(block)
            .setShouldBackupToCloud(shouldBackupToCloud)
            .build();

        s_client.storeBytes(data)
            .addOnSuccessListener(result ->
            {
                if (result == block.length)
                    cb.OnSuccess(null);
                else
                    cb.OnFailure("torn write");
            })
            .addOnFailureListener(ex ->
            {
                cb.OnFailure(ex.getMessage());
            });
    }

    public static void ReadBlock(String label, IMetaplayGmsBlockStoreCallback cb)
    {
        ensureClient();

        ArrayList<String> keys = new ArrayList<String>();
        keys.add(label);

        RetrieveBytesRequest request = new RetrieveBytesRequest.Builder()
            .setKeys(keys)
            .build();

        s_client.retrieveBytes(request)
            .addOnSuccessListener(response ->
            {
                RetrieveBytesResponse.BlockstoreData data = response.getBlockstoreDataMap().get(label); // \note get returns null if missing
                if (data == null)
                    cb.OnSuccess(null);
                else
                {
                    byte[] bytes = data.getBytes();
                    if (bytes == null || bytes.length == 0)
                        cb.OnSuccess(null);
                    else
                        cb.OnSuccess(bytes);
                }
            })
            .addOnFailureListener(ex ->
            {
                cb.OnFailure(ex.getMessage());
            });
    }

    public static void DeleteBlock(String label, IMetaplayGmsBlockStoreCallback cb)
    {
        ensureClient();

        ArrayList<String> keys = new ArrayList<String>();
        keys.add(label);

        DeleteBytesRequest request = new DeleteBytesRequest.Builder()
            .setKeys(keys)
            .build();

        s_client.deleteBytes(request)
            .addOnSuccessListener(result ->
            {
                // If result==false, the item didn't exist before. It is fine too
                cb.OnSuccess(null);
            })
            .addOnFailureListener(ex ->
            {
                cb.OnFailure(ex.getMessage());
            });
    }
}
