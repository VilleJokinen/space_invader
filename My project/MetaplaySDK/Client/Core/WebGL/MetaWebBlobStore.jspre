// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Blob storage built on top of the low-level MetaIndexedDB. Provides an opinionated filesystem-like
// interface for reading, writing, and prefix-scanning blobs (files).
class MetaWebBlobStore {
  // IndexedDB stores (tables) & node types
  static STORE_NODES = 'nodes';
  static STORE_BLOBS = 'blobs';
  static NODETYPE_BLOB = 'blob';

  static async create(dbName) {
    // Create low-level database
    // console.log('MetaWebBlobStore.create():', dbName);
    const metaDb = await MetaIndexedDB.create(dbName, 1, (upDb, oldVersion, newVersion) => {
      console.log(`Metaplay: Upgrading blob store database from v${oldVersion} to v${newVersion}`);
      if (oldVersion < 1 && newVersion >= 1) {
        upDb.createObjectStore(MetaWebBlobStore.STORE_NODES, { keyPath: 'path' });
        upDb.createObjectStore(MetaWebBlobStore.STORE_BLOBS, { keyPath: 'path' });
      }
    });

    return new MetaWebBlobStore(metaDb);
  }

  constructor(metaDb) {
    this.metaDb = metaDb;

    // Cache IndexedDB values with proper names
    // this.IDBTransaction = window.IDBTransaction || window.webkitIDBTransaction || window.msIDBTransaction || { READ_WRITE: "readwrite" };
    this.IDBKeyRange = window.IDBKeyRange || window.webkitIDBKeyRange || window.msIDBKeyRange;
    if (!this.IDBKeyRange) {
      throw new Error('MetaWebBlobStore: Browser does not support window.IDBKeyRange object. Please consider upgrading your browser.');
    }
  }

  // Add or update a blob
  async writeBlobAsync(path, payload) {
    this._validatePath(path);
    await this.metaDb.executeTxnAsync([MetaWebBlobStore.STORE_NODES, MetaWebBlobStore.STORE_BLOBS], 'readwrite', async (txn, [nodeStore, blobStore]) => {
      const node = await this.metaDb.getItemAsync(nodeStore, path);
      if (node === undefined) {
        await this.metaDb.addItemAsync(nodeStore, { path: path, type: MetaWebBlobStore.NODETYPE_BLOB });
        await this.metaDb.addItemAsync(blobStore, { path: path, payload: payload });
      } else if (node.type !== MetaWebBlobStore.NODETYPE_BLOB) {
        throw new Error(`MetaWebBlobStore: Trying to override non-blob '${path}'`);
      } else {
        await this.metaDb.updateItemAsync(nodeStore, { path: path, type: MetaWebBlobStore.NODETYPE_BLOB });
        await this.metaDb.updateItemAsync(blobStore, { path: path, payload: payload });
      }
    })
  }

  async readBlobAsync(path) {
    this._validatePath(path);
    return await this.metaDb.executeTxnAsync([MetaWebBlobStore.STORE_BLOBS], 'readonly', async (txn, [blobStore]) => {
      const blob = await this.metaDb.getItemAsync(blobStore, path);
      if (blob !== undefined) {
        return blob.payload;
      } else {
        return null; // non-existent file returns null
      }
    });
  }

  async deleteBlobAsync(path) {
    this._validatePath(path);
    return await this.metaDb.executeTxnAsync([MetaWebBlobStore.STORE_NODES, MetaWebBlobStore.STORE_BLOBS], 'readwrite', async (txn, [nodeStore, blobStore]) => {
      const node = await this.metaDb.getItemAsync(nodeStore, path);
      if (node === undefined) {
        // ignore non-existent files quietly
      } else if (node.type !== MetaWebBlobStore.NODETYPE_BLOB) {
        throw new Error(`MetaWebBlobStore: Trying to delete non-blob ${path}`);
      } else {
        await this.metaDb.deleteItemAsync(nodeStore, path);
        await this.metaDb.deleteItemAsync(blobStore, path);
      }
    })
  }

  async scanDirectoryAsync(directoryPath, isRecursive) {
    let pathPrefix;
    if (directoryPath.endsWith('/')) {
      pathPrefix = directoryPath;
    } else {
      pathPrefix = directoryPath + '/';
    }

    return await this.metaDb.executeTxnAsync([MetaWebBlobStore.STORE_NODES], 'readonly', async (txn, [nodeStore]) => {
      const query = this.IDBKeyRange.bound(pathPrefix, pathPrefix + '\uffff', false, false);
      const nodes = await this.metaDb.getAllAsync(nodeStore, query);
      // Return only found files (ignore other types)
      const allBlobs = nodes.filter(node => node.type == MetaWebBlobStore.NODETYPE_BLOB);
      // Query is a prefix scan. If we want non-recursive search, filter out items that are not
      // on top level (i.e. do no have / after the path prefix).
      //
      // \note: this means the cost of this call is determined by the number of all decandents, not just
      //        it's immediate children. This can be surprisingly high, especially if folders near root
      //        are scanned.
      let desiredBlobs;
      if (isRecursive) {
        desiredBlobs = allBlobs;
      } else {
        desiredBlobs = allBlobs.filter(node => node.path.indexOf('/', pathPrefix.length) == -1);
      }
      return desiredBlobs.map(node => node.path);
    });
  }

  _validatePath(path) {
    if (typeof path !== 'string') {
      throw new Error(`MetaWebBlobStore: path must be a string, got type ${typeof path}`)
    } else if (path.length === 0) {
      throw new Error(`MetaWebBlobStore: path must be non-empty, got '${path}'`)
    } else if (!path.startsWith('/')) {
      throw new Error(`MetaWebBlobStore: path must start with a slash, got '${path}'`)
    } else if (path.endsWith('/')) {
      throw new Error(`MetaWebBlobStore: path must not end with a slash, got '${path}'`)
    } else if (path.indexOf('//') !== -1) {
      throw new Error(`MetaWebBlobStore: path must not contain double-slashes, got '${path}'`)
    }
  }
}
