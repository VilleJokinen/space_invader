// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

class WebApiBridgeRegistry {
  constructor(metaplayApiBridge, invokeSyncMethod, invokeAsyncMethod, bridgeClasses) {
    const self = this;
    self.bridgeClasses = bridgeClasses;
    self.nextRequestId = 200;
    self.syncBrowserMethods = {};
    self.asyncBrowserMethods = {};
    self.asyncCallbackHandlers = {};

    for (const classInfo of bridgeClasses) {
      // console.log('classInfo:', classInfo);
      const browserModule = metaplayApiBridge[classInfo.className];
      if (!browserModule) {
        throw new Error(`Module.${classInfo.className} is declared in the Unity client but no browser implementation for it was supplied. Verify initializeUnityRuntimeBridge is called and the given bridge implementations.`);
      }

      // Create mapping table for browser methods from methodId to actual method
      for (const methodInfo of classInfo.expectedBrowserMethods) {
        // console.log('Expected browser method:', methodInfo);
        const browserMethod = browserModule[methodInfo.methodName];
        if (!browserMethod) {
          throw new Error(`Browser method ${methodInfo.methodName}() not found in Module.${classInfo.className}!`);
        }

        if (methodInfo.isAsync) {
          self.asyncBrowserMethods[methodInfo.methodId] = browserMethod;
        } else {
          self.syncBrowserMethods[methodInfo.methodId] = browserMethod;
        }
      }

      // Create wrapper methods for browser -> C# invocations
      for (const methodInfo of classInfo.exportedMethods) {
        // console.log(`Exported method ${classInfo.className}.${methodInfo.methodName}()`);
        if (methodInfo.isAsync) {
          browserModule[methodInfo.methodName] = function asyncMethodWrapper(args) {
            return new Promise((resolve, reject) => {
              // Register callback handler for when C# operation completes
              const reqId = self.nextRequestId++;
              self.asyncCallbackHandlers[reqId] = (reply, errorStr) => {
                if (!errorStr) {
                  resolve(reply);
                } else {
                  reject(errorStr);
                }
              }

              // Invoke the C# method
              // console.log(`Invoking exported async method ${classInfo.className}.${methodInfo.methodName}():`, args);
              const argsBuffer = MetaplayUtil.stringToUTF8(JSON.stringify(args));
              try {
                Module.dynCall_viii(invokeAsyncMethod, reqId, methodInfo.methodId, argsBuffer);
              } finally {
                _free(argsBuffer);
              }
            });
          };
        } else { // sync method call
          browserModule[methodInfo.methodName] = function syncMethodWrapper(args) {
            // console.log(`Invoking exported sync method ${classInfo.className}.${methodInfo.methodName}(${args}) hasReturnValue=${methodInfo.hasReturnValue}`);
            const argsBuffer = MetaplayUtil.stringToUTF8(JSON.stringify(args));
            try {
              if (methodInfo.hasReturnValue) {
                const replyPtr = Module.dynCall_iii(invokeSyncMethod, methodInfo.methodId, argsBuffer);
                try {
                  const replyStr = UTF8ToString(replyPtr);
                  return JSON.parse(replyStr);
                } catch (e) {
                  console.error(`Failed to parse ${classInfo.className}.${methodInfo.methodName}() return value "${replyStr}" (expecting JSON):`, e);
                  throw e;
                } finally {
                  _free(replyPtr);
                }
              } else {
                Module.dynCall_vii(invokeSyncMethod, methodInfo.methodId, argsBuffer);
                return undefined;
              }
            } finally {
              _free(argsBuffer);
            }
          };
        }
      }
    }
  }
};
