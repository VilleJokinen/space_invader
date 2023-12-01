// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const AtomicBlobStorePlugin = {
  $_atomicBlobStore: {
    filePrefix: null,
    storage: null
  },

  AtomicBlobStoreJs_Initialize: function(projectNameStr) {
    _atomicBlobStore.filePrefix = `${UTF8ToString(projectNameStr)}:`;
    // console.log('AtomicBlobStoreJs_Initialize()', _atomicBlobStore.filePrefix);

    // Initialize localStorage (make sure it exists)
    let returnValue = null;
    try
    {
      if (!window.localStorage) {
        returnValue = 'Unable to find window.localStorage';
      } else {
        _atomicBlobStore.storage = window.localStorage;
        returnValue = null;
      }
    } catch {
      returnValue = 'Unable to access window.localStorage';
    }
    return MetaplayUtil.stringToUTF8(returnValue);
  },

  AtomicBlobStoreJs_WriteBlob: function(pathStr, bytesStr) {
    let returnValue = null;
    if (_atomicBlobStore.storage === null)
      returnValue = 'no local storage available';
    else {
      const keyName = `${_atomicBlobStore.filePrefix}${UTF8ToString(pathStr)}`;
      // console.log('AtomicBlobStoreJs_WriteBlob():', keyName);
      try {
        _atomicBlobStore.storage.setItem(keyName, UTF8ToString(bytesStr));
        returnValue = null;
      } catch(err) {
        returnValue = err.toString();
      }
    }
    return MetaplayUtil.stringToUTF8(returnValue);
  },

  AtomicBlobStoreJs_ReadBlob: function(pathStr) {
    if (_atomicBlobStore.storage === null)
      return null;
    const keyName = `${_atomicBlobStore.filePrefix}${UTF8ToString(pathStr)}`;
    // console.log('AtomicBlobStoreJs_ReadBlob():', keyName);
    const bytes64 = _atomicBlobStore.storage.getItem(keyName);
    return (bytes64 !== null) ? MetaplayUtil.stringToUTF8(bytes64) : null;
  },

  AtomicBlobStoreJs_DeleteBlob: function(pathStr) {
    if (_atomicBlobStore.storage === null)
      return;
    const keyName = `${_atomicBlobStore.filePrefix}${UTF8ToString(pathStr)}`;
    // console.log('AtomicBlobStoreJs_DeleteBlob():', keyName);
    _atomicBlobStore.storage.removeItem(keyName);
  }
}

autoAddDeps(AtomicBlobStorePlugin, '$_atomicBlobStore');
mergeInto(LibraryManager.library, AtomicBlobStorePlugin);
