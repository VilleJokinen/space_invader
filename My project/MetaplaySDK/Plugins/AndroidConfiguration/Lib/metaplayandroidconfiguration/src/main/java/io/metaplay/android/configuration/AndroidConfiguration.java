// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
package io.metaplay.android.configuration;

import android.provider.Settings.Secure;
import com.unity3d.player.*;

public final class AndroidConfiguration
{
    public static String GetAndroidId()
    {
        return Secure.getString(UnityPlayer.currentActivity.getContentResolver(), Secure.ANDROID_ID);
    }
}
