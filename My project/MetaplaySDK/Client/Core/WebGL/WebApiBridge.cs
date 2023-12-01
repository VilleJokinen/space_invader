// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL

using AOT;
using Metaplay.Core.Json;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using static System.FormattableString;

namespace Metaplay.Core
{
    /// <summary>
    /// Mark a class as containing external methods in the browser or methods exposed
    /// to the browser.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class MetaWebApiBridgeAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark a method to be exported from C# to the browser, i.e., to be callable from
    /// the browser.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MetaExportToBrowserAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark a method as a wrapper method for calling an external method in the browser.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MetaImportBrowserMethodAttribute : Attribute
    {
    }

    /// <summary>
    /// System for making simpler calls between the browser (JavaScript) and the application (C#),
    /// using Unity's own low-level primitives.
    /// <para>
    /// Note the following features & limitations:
    /// - Supports both synchronous and async methods.
    /// - Only supports a single argument that must be a JSON-encoded object (can be a struct or class).
    /// - Return values must be a JSON-encoded object (or void).
    /// </para>
    /// </summary>
    public static class WebApiBridge
    {
        /// <summary>
        /// Metadata about a method that is callable between the browser (JS) and the app (C#).
        /// </summary>
        class BridgeMethodInfo
        {
            public int      MethodId        { get; private set; }
            public string   MethodName      { get; private set; }
            public bool     IsAsync         { get; private set; }
            public bool     HasReturnValue  { get; private set; }

            public BridgeMethodInfo(int methodId, string methodName, bool isAsync, bool hasReturnValue)
            {
                MethodId = methodId;
                MethodName = methodName;
                IsAsync = isAsync;
                HasReturnValue = hasReturnValue;
            }

            public override string ToString() => Invariant($"BridgeMethodInfo(#{MethodId}, MethodName={MethodName}, IsAsync={IsAsync}, HasReturnValue={HasReturnValue})");
        }

        /// <summary>
        /// Metadata about a class that contains methods callable between the browser (JS) and the app (C#).
        /// </summary>
        class BridgeClassInfo
        {
            public string               ClassName               { get; private set; }
            public BridgeMethodInfo[]   ExpectedBrowserMethods  { get; private set; }
            public BridgeMethodInfo[]   ExportedMethods         { get; private set; }

            public BridgeClassInfo(string className, BridgeMethodInfo[] expectedBrowserMethods, BridgeMethodInfo[] exportedMethods)
            {
                ClassName = className;
                ExpectedBrowserMethods = expectedBrowserMethods;
                ExportedMethods = exportedMethods;
            }
        }

        delegate void AsyncCallbackDelegate (int reqId, string reply, string errorStr);
        delegate string InvokeMethodSyncDelegate (int methodId, string agrsJson);
        delegate void InvokeMethodAsyncDelegate (int reqId, int methodId, string agrsJson);

        static bool                                         _initialized            = false;
        static BridgeClassInfo[]                            _bridgeClasses;
        static int                                          _nextRequestId          = 100;
        static Dictionary<int, Action<string, string>>      _asyncCallbackHandlers  = new Dictionary<int, Action<string, string>>();
        static Dictionary<int, Func<string, string>>        _exportedSyncMethods    = new Dictionary<int, Func<string, string>>();
        static Dictionary<int, Func<string, Task<string>>>  _exportedAsyncMethods   = new Dictionary<int, Func<string, Task<string>>>();

        public static void Initialize()
        {
            // \note: WebApiBridge may already be initialized if MetaplaySDK was Stopped and restarted.
            if (_initialized)
                return;

            // Resolve the all the bridge APIs
            // \todo [petri] refactor: fills in _exported{Sync/Async}Methods
            ResolveBridgeAPIs();

            // Initialize JavaScript side
            string bridgeClassesJson = JsonSerialization.SerializeToString(_bridgeClasses);
            WebApiBridgeJs_Initialize(WebApiBridge_AsyncCallCallback, WebApiBridge_InvokeMethodSync, WebApiBridge_InvokeMethodAsync, bridgeClassesJson);

            _initialized = true;
        }

        static void ResolveBridgeAPIs()
        {
            // \todo [petri] detect name conflicts (classes & methods)
            List<BridgeClassInfo> bridgeClassInfos = new List<BridgeClassInfo>();
            int nextMethodId = 1;

            foreach (Type classInfo in TypeScanner.GetClassesWithAttribute<MetaWebApiBridgeAttribute>())
            {
                if (bridgeClassInfos.Any(ci => ci.ClassName == classInfo.Name))
                    throw new InvalidOperationException($"Multiple classes with {nameof(MetaWebApiBridgeAttribute)} have the same name '{classInfo.Name}'");

                List<BridgeMethodInfo> expectedBrowserMethods = new List<BridgeMethodInfo>();
                List<BridgeMethodInfo> exportedMethods = new List<BridgeMethodInfo>();

                foreach (MethodInfo methodInfo in classInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (expectedBrowserMethods.Any(mi => mi.MethodName == methodInfo.Name) || exportedMethods.Any(mi => mi.MethodName == methodInfo.Name))
                        throw new InvalidOperationException($"Multiple WebApiBridge methods in class {classInfo.Name} have the same name '{methodInfo.Name}()'");

                    // Resolve method type
                    Type returnType = methodInfo.ReturnType;
                    bool isAsync = returnType == typeof(Task) || returnType.IsGenericTypeOf(typeof(Task<>));
                    bool hasReturnValue = returnType != typeof(void) && returnType != typeof(Task);

                    // Register expected browser method
                    if (methodInfo.GetCustomAttribute<MetaImportBrowserMethodAttribute>() != null)
                        expectedBrowserMethods.Add(new BridgeMethodInfo(nextMethodId++, methodInfo.Name, isAsync, hasReturnValue));

                    // Register exported C# method
                    if (methodInfo.GetCustomAttribute<MetaExportToBrowserAttribute>() != null)
                    {
                        int methodId = nextMethodId++;
                        exportedMethods.Add(new BridgeMethodInfo(methodId, methodInfo.Name, isAsync, hasReturnValue));
                        //DebugLog.Debug("Registering method #{MethodId} {MethodName}() isAsync={IsAsync} hasReturnValue={HasReturnValue}", methodId, methodInfo.Name, isAsync, hasReturnValue);
                        if (isAsync)
                            _exportedAsyncMethods.Add(methodId, (string args) => (Task<string>)methodInfo.Invoke(null, new object[] { args }));
                        else
                            _exportedSyncMethods.Add(methodId, (string args) => (string)methodInfo.Invoke(null, new object[] { args }));
                    }
                }

                bridgeClassInfos.Add(new BridgeClassInfo(classInfo.Name, expectedBrowserMethods.ToArray(), exportedMethods.ToArray()));
            }

            _bridgeClasses = bridgeClassInfos.ToArray();
        }

        static int AllocRequestId()
        {
            return _nextRequestId++;
        }

        public static int GetBrowserMethodId(string className, string methodName)
        {
            if (!_initialized)
                throw new InvalidOperationException("WebApiBridge not yet initialized");

            foreach (BridgeClassInfo bridgeClass in _bridgeClasses)
            {
                if (bridgeClass.ClassName != className)
                    continue;
                foreach (BridgeMethodInfo methodInfo in bridgeClass.ExpectedBrowserMethods)
                {
                    if (methodInfo.MethodName != methodName)
                        continue;
                    return methodInfo.MethodId;
                }
                throw new InvalidOperationException($"No such method in Unity client {className}: {methodName}");
            }
            throw new InvalidOperationException($"No such bridge class defined in Unity client: {className}");
        }

        public static string JsonCallSync(int methodId, string argsJson)
        {
            if (!_initialized)
                throw new InvalidOperationException("WebApiBridge not yet initialized");

            // Invoke JavaScript synchronously
            // Debug.Log($"WebApiBridge.JsonCallSync(): methodId={methodId}, args={argsJson}");
            string result = WebApiBridgeJs_JsonCallSync(methodId, argsJson);
            // DebugLog.Info("WebApiBridge.JsonCallSync() result: {Result}", result);
            return result;
        }

        public static async Task<string> JsonCallAsync(int methodId, string argsJson)
        {
            if (!_initialized)
                throw new InvalidOperationException("WebApiBridge not yet initialized");

            // Register callback & invoke JavaScript asynchronously
            (int reqId, Task<string> task) = RegisterRequestHandler();
            // Debug.Log($"WebApiBridge.JsonCallAsync(): reqId={reqId}, methodId={methodId}, args={argsJson}");
            WebApiBridgeJs_JsonCallAsync(reqId, methodId, argsJson);
            string result = await task;
            // DebugLog.Info("WebApiBridge.JsonCallAsync() result: {Result}", result);
            return result;
        }

        #region JavaScript bridge methods

        /// <summary>
        /// Allocate JavaScript invocation requestId and register a TaskCompletionSource for the
        /// callback. The actual JS invocation needs to be done outside of this method.
        /// </summary>
        /// <returns></returns>
        static (int reqId, Task<string> task) RegisterRequestHandler()
        {
            int reqId = AllocRequestId();
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
            _asyncCallbackHandlers.Add(reqId, (reply, errorStr) =>
            {
                if (errorStr == null)
                    tcs.TrySetResult(reply);
                else
                    tcs.TrySetException(new InvalidOperationException(errorStr));
            });

            return (reqId, tcs.Task);
        }

        [MonoPInvokeCallback(typeof(AsyncCallbackDelegate))]
        public static void WebApiBridge_AsyncCallCallback(int reqId, string reply, string errorStr)
        {
            // DebugLog.Info("WebApiBridge_AsyncCallCallback(): reqId={ReqId}, reply={Reply}, errorStr={ErrorStr}", reqId, reply, errorStr);
            if (_asyncCallbackHandlers.Remove(reqId, out Action<string, string> handler))
                handler(reply, errorStr);
            else
                Debug.LogError($"WebApiBridge_AsyncCallCallback(): No handler for requestId #{reqId}");
        }

        [MonoPInvokeCallback(typeof(InvokeMethodSyncDelegate))]
        public static string WebApiBridge_InvokeMethodSync(int methodId, string argsJson)
        {
            // DebugLog.Info("WebApiBridge_InvokeMethodSync(): methodId={MethodId}, args={Args}", methodId, argsJson);
            if (_exportedSyncMethods.TryGetValue(methodId, out Func<string, string> method))
            {
                // \todo [petri] validate method signature?
                string replyJson = method(argsJson); // \note void methods should just return null
                return replyJson;
            }
            else
            {
                Debug.LogError($"WebApiBridge_InvokeMethodSync(): Trying to invoke unknown methodId {methodId}");
                return null;
            }
        }

        [MonoPInvokeCallback(typeof(InvokeMethodAsyncDelegate))]
        public static void WebApiBridge_InvokeMethodAsync(int reqId, int methodId, string argsJson)
        {
            // DebugLog.Info("WebApiBridge_InvokeMethodAsync(): reqId={ReqId}, methodId={MethodId}, args={Args}", reqId, methodId, argsJson);
            if (_exportedAsyncMethods.TryGetValue(methodId, out Func<string, Task<string>> method))
            {
                // \todo [petri] validate method signature?
                Task<string> task = method(argsJson); // \note void methods should just return null
                task.ContinueWithCtx((Task<string> t) =>
                {
                    // DebugLog.Info("WebApiBridge_InvokeMethodAsync(): Task completed with {Status} ({IsSuccess})", t.Status, t.IsCompletedSuccessfully);
                    if (t.IsCompletedSuccessfully)
                        WebApiBridgeJs_InvokeAsyncCallback(reqId, replyJson: t.GetCompletedResult(), errorStr: null);
                    else
                        WebApiBridgeJs_InvokeAsyncCallback(reqId, replyJson: null, errorStr: t.Exception.ToString());
                });
            }
            else
            {
                Debug.LogError($"WebApiBridge_InvokeMethodAsync(): Trying to invoke unknown method #{methodId}");
            }
        }

        [DllImport("__Internal")] static extern void WebApiBridgeJs_Initialize(AsyncCallbackDelegate invokeCallback, InvokeMethodSyncDelegate invokeMethodSync, InvokeMethodAsyncDelegate invokeMethodAsync, string expectedBrowserMethodsJson);
        [DllImport("__Internal")] static extern string WebApiBridgeJs_JsonCallSync(int methodId, string argsJson);
        [DllImport("__Internal")] static extern void WebApiBridgeJs_JsonCallAsync(int reqId, int methodId, string argsJson);
        [DllImport("__Internal")] static extern void WebApiBridgeJs_InvokeAsyncCallback(int reqId, string replyJson, string errorStr);

        #endregion
    }
}

#endif // Unity WebGL
