// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
package io.metaplay.android.customtabs;

import android.net.Uri;
import androidx.browser.customtabs.*;
import com.unity3d.player.*;

public final class MetaplayAndroidCustomTabs
{
    public static void LaunchCustomTabs(String url)
    {
        CustomTabsIntent intent =
            new CustomTabsIntent.Builder()
            .setShowTitle(false)
            .setStartAnimations(UnityPlayer.currentActivity.getApplicationContext(), R.anim.customtabs_tabs_enter, R.anim.customtabs_app_exit)
            .setExitAnimations(UnityPlayer.currentActivity.getApplicationContext(), R.anim.customtabs_app_enter, R.anim.customtabs_tabs_exit)
            .build();
        intent.launchUrl(UnityPlayer.currentActivity, Uri.parse(url));
    }
}
