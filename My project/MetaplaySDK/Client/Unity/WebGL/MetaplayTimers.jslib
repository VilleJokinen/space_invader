// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

const MetaplayTimerLib = {
  $_timerLibState: {
    callback: null,
    timers: {},
  },

  $_timerLibOnTimerTimeout: function (timerId) {
    const timer = _timerLibState.timers[timerId]
    if (timer) {
      if (timer.period > 0) {
        const timeout = setTimeout(() => _timerLibOnTimerTimeout(timerId), timer.period)
        _timerLibState.timers[timerId].timeout = timeout
      } else {
        clearTimeout(timer.timeout)
        delete _timerLibState.timers[timerId]
      }
      Module.dynCall_vi(_timerLibState.callback, timer.timerId)
    }
  },

  WebGlTimerManagerJs_SetJsCallback: function (callback) {
    _timerLibState.callback = callback
  },

  WebGlTimerManagerJs_SetJsTimer: function (timerId, dueTimeMs, periodMs) {
    let time = dueTimeMs

    if (time < 0) {
      time = periodMs
    }

    if (time < 0) {
      return
    }

    if (_timerLibState.timers[timerId]) {
      clearTimeout(_timerLibState.timers[timerId].timeout)
    }

    const timeout = setTimeout(() => _timerLibOnTimerTimeout(timerId), time)
    _timerLibState.timers[timerId] = {
      timerId: timerId,
      timeout: timeout,
      period: periodMs
    }
  },

  WebGlTimerManagerJs_RemoveJsTimer: function (timerId) {
    const timer = _timerLibState.timers[timerId]
    if (timer) {
      clearTimeout(timer.timeout)
      delete _timerLibState.timers[timerId]
    }
  }
}

autoAddDeps(MetaplayTimerLib, '$_timerLibState')
autoAddDeps(MetaplayTimerLib, '$_timerLibOnTimerTimeout')
mergeInto(LibraryManager.library, MetaplayTimerLib)
