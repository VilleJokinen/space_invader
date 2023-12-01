-keep public class io.metaplay.android.blockstore.MetaplayGmsBlockStore {
    public static *** * (...);
}
-keep public interface io.metaplay.android.blockstore.IMetaplayGmsBlockStoreCallback {
    public *** * (...);
}

# Print all that were removed. Convenient for debugging.
# -printusage removed_or_obfuscated_by_proguard.txt
