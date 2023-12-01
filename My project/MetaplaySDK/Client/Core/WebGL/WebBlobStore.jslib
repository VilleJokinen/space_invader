// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const WebBlobStorePlugin = {
  $_webBlobStore: {
    invokeCallback: null,
    blobStore: null,
  },

  WebBlobStoreJs_Initialize: async function(reqId, projectNamePtr, asyncCallbackMethod) {
    const projectName = UTF8ToString(projectNamePtr);
    // console.log('WebBlobStoreJs_Initialize()', projectName);

    // Synchronously invoke the C# callback method
    _webBlobStore.invokeCallback = MetaplayUtil.wrapAsyncCallbackMethod(asyncCallbackMethod);

    // Initialize BlobStore
    try {
      _webBlobStore.blobStore = await MetaWebBlobStore.create(`${projectName}/MetaWebBlobStore`);
      _webBlobStore.invokeCallback(reqId, "success", null);
    } catch (err) {
      _webBlobStore.invokeCallback(reqId, null, err.toString());
    }
  },

  WebBlobStoreJs_WriteBlob: async function(reqId, pathPtr, bytes64Ptr) {
    try {
      await _webBlobStore.blobStore.writeBlobAsync(UTF8ToString(pathPtr), UTF8ToString(bytes64Ptr));
      _webBlobStore.invokeCallback(reqId, "success", null);
    } catch(err) {
      _webBlobStore.invokeCallback(reqId, null, err.toString());
    }
  },

  WebBlobStoreJs_ReadBlob: async function(reqId, pathPtr) {
    try {
      const bytes64 = await _webBlobStore.blobStore.readBlobAsync(UTF8ToString(pathPtr));
      _webBlobStore.invokeCallback(reqId, bytes64, null);
    } catch(err) {
      _webBlobStore.invokeCallback(reqId, null, err.toString());
    }
  },

  WebBlobStoreJs_DeleteBlob: async function(reqId, pathPtr) {
    try {
      await _webBlobStore.blobStore.deleteBlobAsync(UTF8ToString(pathPtr));
      _webBlobStore.invokeCallback(reqId, "success", null);
    } catch(err) {
      _webBlobStore.invokeCallback(reqId, null, err.toString());
    }
  },

  WebBlobStoreJs_ScanBlobsInDirectory: async function(reqId, dirPathPtr, recursive) {
    try {
      const blobPaths = await _webBlobStore.blobStore.scanDirectoryAsync(UTF8ToString(dirPathPtr), recursive);
      // \note: '//' is a safe separator as:
      //        * blob names must start with '/'
      //        * blob names must not end with '/'
      //        * blob names must not contain '//'
      //        * blob names must not have empty name.
      //        Hence there cannot be any // strings except in each path join point where we will have ///. Here 
      //        first 2 are for separation and last 1 is the leading / of the next path, which will be preserved.
      _webBlobStore.invokeCallback(reqId, blobPaths.join('//'), null);
    } catch(err) {
      _webBlobStore.invokeCallback(reqId, null, err.toString());
    }
  },
}

autoAddDeps(WebBlobStorePlugin, '$_webBlobStore');
mergeInto(LibraryManager.library, WebBlobStorePlugin);
