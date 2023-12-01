// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Low-level wrapper over IndexedDB that provides async/await API instead of callbacks.
class MetaIndexedDB {
  static async _openIndexedDb(dbName, version, onUpgrade) {
    // Check that IndexedDB is available in browser (or throw)
    var indexedDB = null;
    try {
      indexedDB = window.indexedDB || window.mozIndexedDB || window.webkitIndexedDB || window.msIndexedDB;
    } catch (error) {
      console.log('MetaIndexedDB: Access to IndexedDB was not permitted');
    }
    if (!indexedDB) {
      throw new Error('MetaIndexedDB: IndexedDB is required, but not supported by your browser! Please make sure your browser is up-to-date.');
    }

    return new Promise((resolve, reject) => {
      const req = indexedDB.open(dbName, version);
      req.onsuccess = event => resolve(event.target.result);
      req.onerror = event => reject(event);
      req.onupgradeneeded = event => {
        const upDb = event.target.result;
        onUpgrade(upDb, event.oldVersion, event.newVersion);
      };
    });
  }

  // Create an instance of MetaIndexedDB
  static async create(dbName, version, onUpgrade) {
    // Open handle to IndexedDB
    const db = await MetaIndexedDB._openIndexedDb(dbName, version, onUpgrade);

    // Create instance
    return new MetaIndexedDB(db);
  }

  constructor(db) {
    this.db = db;
  }

  executeTxnAsync(objectStores, mode, txnBodyAsync) {
    return new Promise((resolve, reject) => {
      const txn = this.db.transaction(objectStores, mode);
      let result = undefined;
      txn.oncomplete = event => {
        resolve(result);
      };
      txn.onerror = event => {
        console.log('MetaIndexedDB transaction failed:', event);
        reject(event);
      };
      objectStores = objectStores.map(store => txn.objectStore(store));
      txnBodyAsync(txn, objectStores)
        .then(res => {
          result = res;
          txn.commit();
        })
        .catch(err => {
          console.log('MetaIndexedDB transaction raised an unexpected exception:', err);
          txn.abort();
          reject(err);
        });
    });
  }

  getAllAsync(objectStore, query) {
    return new Promise((resolve, reject) => {
      const req = objectStore.getAll(query);
      req.onsuccess = event => resolve(event.target.result);
      req.onerror = event => reject(event);
    });
  }

  getItemAsync(objectStore, key) {
    return new Promise((resolve, reject) => {
      const req = objectStore.get(key);
      req.onsuccess = event => resolve(event.target.result);
      req.onerror = event => reject(event);
    });
  }

  addItemAsync(objectStore, item) {
    return new Promise((resolve, reject) => {
      const req = objectStore.add(item);
      req.onsuccess = event => resolve(event.target.result);
      req.onerror = event => reject(event);
    });
  }

  updateItemAsync(objectStore, item) {
    return new Promise((resolve, reject) => {
      const req = objectStore.put(item);
      req.onsuccess = event => resolve(event.target.result);
      req.onerror = event => reject(event);
    });
  }

  deleteItemAsync(objectStore, key) {
    return new Promise((resolve, reject) => {
      const req = objectStore.delete(key);
      req.onsuccess = event => resolve(event.target.result);
      req.onerror = event => reject(event);
    });
  }
}
