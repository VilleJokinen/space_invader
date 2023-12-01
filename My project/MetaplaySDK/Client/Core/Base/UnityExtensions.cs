// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_2017_1_OR_NEWER

using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine.Networking;

public static class UnityWebRequestExtension
{
    /// <summary>
    /// Allows awaiting Unity HTTP requests as <c>await UnityWebRequest.Get(requestUri).SendWebRequest()</c>.
    /// </summary>
    public static TaskAwaiter<UnityWebRequest> GetAwaiter(this UnityWebRequestAsyncOperation op)
    {
        TaskCompletionSource<UnityWebRequest> tcs = new TaskCompletionSource<UnityWebRequest>();
        op.completed += asyncOp => tcs.TrySetResult(op.webRequest);
        return tcs.Task.GetAwaiter();
    }
}

#endif // Unity
