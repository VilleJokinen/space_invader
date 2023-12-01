// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const MetaplayWebGLPlugin = {
  MetaplayWebGLJs_Initialize: async function(onApplicationQuitCallback) {
    window.addEventListener('beforeunload', (event) => {
      console.log('beforeunload(): invoking onApplicationQuitCallback')
      Module.dynCall_v(onApplicationQuitCallback);
    });
  },
}

mergeInto(LibraryManager.library, MetaplayWebGLPlugin);
