// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Tasks;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity.Android
{
    public static class MetaplayGmsBlockStore
    {
        class WriteProxy : AndroidJavaProxy
        {
            TaskCompletionSource<int> _tsc;
            public WriteProxy(TaskCompletionSource<int> tcs) : base("io.metaplay.android.blockstore.IMetaplayGmsBlockStoreCallback")
            {
                _tsc = tcs;
            }
            public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
            {
                if (methodName == "OnSuccess")
                {
                    _tsc.TrySetResult(1);
                }
                else if (methodName == "OnFailure")
                {
                    AndroidJavaObject javaString = javaArgs[0];
                    string dotnetString;
                    if (javaString != null)
                        dotnetString = javaArgs[0].Call<string>("toString");
                    else
                        dotnetString = null;
                    _tsc.TrySetException(new Exception(dotnetString));
                }
                return null;
            }
        }

        /// <summary>
        /// Writes block into BlockStore with the given label. Throws on failure.
        /// </summary>
        public static Task WriteBlockAsync(string label, byte[] block, bool shouldBackupToCloud)
        {
            // Proxies and JNI calls must be done on JNI-attached thread. We temporarily attach a thread pool thread. We
            // don't want to use UnityMain -- even though is is attached, it might be busy, or even synchronously waiting
            // this call.

            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
            sbyte[] blockAsSbytes = new sbyte[block.Length];
            Buffer.BlockCopy(src: block, srcOffset: 0, dst: blockAsSbytes, dstOffset: 0, count: block.Length);

            _ = MetaTask.Run(() =>
            {
                _ = AndroidJNI.AttachCurrentThread();
                try
                {
                    WriteProxy cbProxy = new WriteProxy(tcs);
                    using (AndroidJavaClass clazz = new AndroidJavaClass("io.metaplay.android.blockstore.MetaplayGmsBlockStore"))
                    {
                        clazz.CallStatic("WriteBlock", label, blockAsSbytes, shouldBackupToCloud, cbProxy);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    _ = AndroidJNI.DetachCurrentThread();
                }
            }, MetaTask.BackgroundScheduler);

            return tcs.Task;
        }

        class ReadProxy : AndroidJavaProxy
        {
            TaskCompletionSource<byte[]> _tsc;
            public ReadProxy(TaskCompletionSource<byte[]> tcs) : base("io.metaplay.android.blockstore.IMetaplayGmsBlockStoreCallback")
            {
                _tsc = tcs;
            }
            public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
            {
                if (methodName == "OnSuccess")
                {
                    AndroidJavaObject javaBytes = javaArgs[0];
                    byte[] dotnetBytes;
                    if (javaBytes != null)
                    {
                        sbyte[] blockAsSbytes = AndroidJNIHelper.ConvertFromJNIArray<sbyte[]>(javaBytes.GetRawObject());
                        dotnetBytes = new byte[blockAsSbytes.Length];
                        Buffer.BlockCopy(src: blockAsSbytes, srcOffset: 0, dst: dotnetBytes, dstOffset: 0, count: blockAsSbytes.Length);
                    }
                    else
                        dotnetBytes = null;
                    _tsc.TrySetResult(dotnetBytes);
                }
                else if (methodName == "OnFailure")
                {
                    AndroidJavaObject javaString = javaArgs[0];
                    string dotnetString;
                    if (javaString != null)
                        dotnetString = javaArgs[0].Call<string>("toString");
                    else
                        dotnetString = null;
                    _tsc.TrySetException(new Exception(dotnetString));
                }
                return null;
            }
        }

        /// <summary>
        /// Reads block from BlockStore. If no block with the label exists, or it is empty, <c>null</c> is returned. Throws on failure.
        /// </summary>
        public static Task<byte[]> ReadBlockAsync(string label)
        {
            // as with WriteBlockAsync

            TaskCompletionSource<byte[]> tcs = new TaskCompletionSource<byte[]>();

            _ = MetaTask.Run(() =>
            {
                _ = AndroidJNI.AttachCurrentThread();
                try
                {
                    ReadProxy cbProxy = new ReadProxy(tcs);
                    using (AndroidJavaClass clazz = new AndroidJavaClass("io.metaplay.android.blockstore.MetaplayGmsBlockStore"))
                    {
                        clazz.CallStatic("ReadBlock", label, cbProxy);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    _ = AndroidJNI.DetachCurrentThread();
                }
            }, MetaTask.BackgroundScheduler);

            return tcs.Task;
        }

        /// <summary>
        /// Deletes a block in BlockStore with the given label. Throws on failure.
        /// </summary>
        public static Task DeleteBlockAsync(string label)
        {
            // as with WriteBlockAsync

            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

            _ = MetaTask.Run(() =>
            {
                _ = AndroidJNI.AttachCurrentThread();
                try
                {
                    WriteProxy cbProxy = new WriteProxy(tcs);
                    using (AndroidJavaClass clazz = new AndroidJavaClass("io.metaplay.android.blockstore.MetaplayGmsBlockStore"))
                    {
                        clazz.CallStatic("DeleteBlock", label, cbProxy);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    _ = AndroidJNI.DetachCurrentThread();
                }
            }, MetaTask.BackgroundScheduler);

            return tcs.Task;
        }
    }
}
