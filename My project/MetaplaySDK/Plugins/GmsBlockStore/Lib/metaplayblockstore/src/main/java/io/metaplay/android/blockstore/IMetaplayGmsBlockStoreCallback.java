// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
package io.metaplay.android.blockstore;

public interface IMetaplayGmsBlockStoreCallback
{
    public void OnSuccess(byte[] readData);
    public void OnFailure(String message);
}
