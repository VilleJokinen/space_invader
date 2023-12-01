// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const MetaplayUtil = {
  // Allocate buffer and write input string into it as UTF8
  stringToUTF8(string) {
    if (string === null) {
        return null;
    }
    const bufferSize = lengthBytesUTF8(string) + 1;
    const buffer = _malloc(bufferSize);
    stringToUTF8(string, buffer, bufferSize);
    return buffer;
  },

  // Wrap a C# callback method with conversion of args/error into UTF8 strings allocated on the heap.
  // Note: This assumes the callback method as arguments (int reqId, string reply, string error).
  wrapAsyncCallbackMethod(callbackMethod) {
    return function(reqId, reply, errorStr) {
      if (errorStr === null) {
        // console.log('Invoking callback with result:', reply);
        const replyBuffer = MetaplayUtil.stringToUTF8(reply);
        try {
          Module.dynCall_viii(callbackMethod, reqId, replyBuffer, null);
        } catch (err) {
          console.error('Invoking Callback with result failed:', err);
        } finally {
          _free(replyBuffer);
        }
      } else {
        // console.log('Invoking callback with error:', errorStr);
        const errorBuffer = MetaplayUtil.stringToUTF8(errorStr);
        try {
          Module.dynCall_viii(callbackMethod, reqId, null, errorBuffer);
        } catch (err) {
          console.error('Invoking Callback with error failed:', err);
        } finally {
          _free(errorBuffer);
        }
      }
    };
  }
};
