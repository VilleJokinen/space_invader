// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const WebSocketConnectorPlugin = {
  $_WebSocketState: {
    sockets: [],
    closeCallback: null,
    errorCallback: null,
    openCallback: null,
    messageCallback: null,
    sendError: null,
    receiveMessage: null,
  },

  WebSocketConnectorJs_Initialize: function (closeCallback, errorCallback, openCallback, messageCallback) {

    _WebSocketState.closeCallback = closeCallback
    _WebSocketState.errorCallback = errorCallback
    _WebSocketState.openCallback = openCallback
    _WebSocketState.messageCallback = messageCallback

    // Convert error to a utf8 buffer and call the error callback.
    _WebSocketState.sendError = function(connId, err) {
      const errorBufferPtr = MetaplayUtil.stringToUTF8(err)
      try {
        Module.dynCall_vii(_WebSocketState.errorCallback, connId, errorBufferPtr)
      } finally {
        _free(errorBufferPtr)
      }
    }

    // Allocate a buffer for the message and call the message callback.
    _WebSocketState.receiveMessage = function(connId, dataBuffer) {
      const msgBufferPtr = _malloc(dataBuffer.length)
      try {
        HEAPU8.set(dataBuffer, msgBufferPtr)
        Module.dynCall_viii(_WebSocketState.messageCallback, connId, msgBufferPtr, dataBuffer.length)
      } finally {
        _free(msgBufferPtr)
      }
    }

  },
  WebSocketConnectorJs_Open: function (connId, connectUrl) {
    try {
      _WebSocketState.sockets[connId] = new WebSocket(UTF8ToString(connectUrl))
      _WebSocketState.sockets[connId].binaryType  = 'arraybuffer'
    } catch(err) {
      _WebSocketState.sendError(connId, 'WebSocket initialization error: ' + err.message)
      return
    }

    // Close gets called after the connection has been succesfully closed, or closed due to an error.
    _WebSocketState.sockets[connId].addEventListener('close', (event) => {
      let reasonBufferPtr = null

      if (event.reason instanceof String) {
        reasonBufferPtr = MetaplayUtil.stringToUTF8(event.reason)
      } else {
        reasonBufferPtr = MetaplayUtil.stringToUTF8('<unspecified>')
      }
      
      try {
        Module.dynCall_viiii(_WebSocketState.closeCallback, connId, event.code, reasonBufferPtr, event.wasClean)
      } finally {
        _free(reasonBufferPtr)
      }

      _WebSocketState.sockets[connId] = null
    })

    // Error is called before close event in case of an error.
    _WebSocketState.sockets[connId].addEventListener('error', (event) => { 
      _WebSocketState.sendError(connId, "WebSocket error.")
    })

    _WebSocketState.sockets[connId].addEventListener('open', (event) => {
      Module.dynCall_vi(_WebSocketState.openCallback, connId)
    })

    // Message is called whenever a message is received from the server.
    _WebSocketState.sockets[connId].addEventListener('message', (event) => {
      if(event.data instanceof String) {
        const dataBuffer = (new TextEncoder()).encode(event.data)
        _WebSocketState.receiveMessage(connId, dataBuffer)
      } else if(event.data instanceof ArrayBuffer) {
        const dataBuffer = new Uint8Array(event.data)
        _WebSocketState.receiveMessage(connId, dataBuffer)
      }
    })
  },
  WebSocketConnectorJs_Close: function (connId, code, reasonPtr) {
    const socket = _WebSocketState.sockets[connId]
    if (socket && socket.readyState < 2) {
      try {
        socket.close(code, UTF8ToString(reasonPtr))
      } catch(err) {
        _WebSocketState.sendError(connId, "WebSocket close error: " + err.message)
      }
    }
  },
  WebSocketConnectorJs_Send: function (connId, dataPtr, start, size) {
    const socket = _WebSocketState.sockets[connId]
    if (socket) {
      const array = HEAPU8.subarray(dataPtr + start, dataPtr + start + size)

      try {
        socket.send(array)
      } catch(err) {
        _WebSocketState.sendError(connId, "WebSocket send error: " + err.message)
      }
    } else {
      _WebSocketState.sendError(connId, "WebSocket send error: socket is null!")
    }
  }
}

autoAddDeps(WebSocketConnectorPlugin, '$_WebSocketState')
mergeInto(LibraryManager.library, WebSocketConnectorPlugin)
