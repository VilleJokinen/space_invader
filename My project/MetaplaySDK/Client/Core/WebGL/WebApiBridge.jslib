// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const WebApiBridgePlugin = {
  $_webApiBridge: {
    invokeAsyncCallback: null, // Invoke the callback method for an async call from C#-to-JS
    invokeSyncMethod: null,   // Invoke a C# method from JS
    apiRegistry: null,
    callbackHandlers: null,
  },

  WebApiBridgeJs_Initialize: function(callbackMethod, invokeSyncMethod, invokeAsyncMethod, bridgeClassesPtr) {
    // console.log('Module.MetaplayApiBridge:', Module.MetaplayApiBridge);

    // Resolve bridge classes
    const bridgeClasses = JSON.parse(UTF8ToString(bridgeClassesPtr));
    // console.log('WebApiBridge classes:', bridgeClasses);

    // Build registry
    _webApiBridge.apiRegistry = new WebApiBridgeRegistry(Module.MetaplayApiBridge, invokeSyncMethod, invokeAsyncMethod, bridgeClasses);

    // Synchronously invoke the C# callback method
    _webApiBridge.invokeAsyncCallback = MetaplayUtil.wrapAsyncCallbackMethod(callbackMethod)
  },

  WebApiBridgeJs_JsonCallSync: async function(methodId, argsPtr) {
    const args = JSON.parse(UTF8ToString(argsPtr));
    // console.log(`WebApiBridgeJs_JsonCallSync(methodId=${methodId}:`, args);

    const method = _webApiBridge.apiRegistry.syncBrowserMethods[methodId];
    if (!method) {
      throw new Error(`Trying to invoke non-existent sync browser method #${methodId}`);
    }

    try {
      const result = method(args);
      return result;
    } catch(err) {
      console.log(`WebApiBridgeJs_JsonCallSync(): Call to browser method #${methodId} failed with ${err}`);
    }
  },

  WebApiBridgeJs_JsonCallAsync: async function(reqId, methodId, argsPtr) {
    const args = JSON.parse(UTF8ToString(argsPtr));
    // console.log(`WebApiBridgeJs_JsonCallAsync(reqId=${reqId}, methodId=${methodId}):`, args);

    const method = _webApiBridge.apiRegistry.asyncBrowserMethods[methodId];
    if (!method) {
      _webApiBridge.invokeAsyncCallback(reqId, null, `Trying to invoke non-existent async browser method #${methodId}`);
      return;
    }

    try {
      const result = await method(args);
      // console.log(`WebApiBridgeJs_JsonCallAsync() reqId=${reqId}, #${methodId} result:`, result);
      _webApiBridge.invokeAsyncCallback(reqId, JSON.stringify(result), null)
    } catch(err) {
      _webApiBridge.invokeAsyncCallback(reqId, null, err.toString())
    }
  },

  WebApiBridgeJs_InvokeAsyncCallback: async function(reqId, replyPtr, errorPtr) {
    // console.log(`WebApiBridgeJs_InvokeAsyncCallback(reqId=${reqId}):`, replyPtr, errorPtr);
    const handler = _webApiBridge.apiRegistry.asyncCallbackHandlers[reqId];
    if (!handler) {
      throw new Error(`WebApiBridgeJs_InvokeAsyncCallback(): No handler for requestId ${reqId}`);
    }

    if (!errorPtr) {
      const reply = JSON.parse(UTF8ToString(replyPtr));
      // console.log(`WebApiBridgeJs_InvokeAsyncCallback(reqId=${reqId}) success:`, reply);
      handler(reply, null);
    } else {
      const errorStr = UTF8ToString(errorPtr);
      console.log(`WebApiBridgeJs_InvokeAsyncCallback(reqId=${reqId}) failure:`, errorStr);
      handler(null, errorStr);
    }
  }
}

autoAddDeps(WebApiBridgePlugin, '$_webApiBridge')
mergeInto(LibraryManager.library, WebApiBridgePlugin)
