// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Threading;
using SystemTimer = Metaplay.Core.Tasks.MetaTimer;

namespace Metaplay.Core
{
    /// <summary>
    /// An <see cref="IMetaLogger"/> that buffers any input into a bounded buffer. The buffered
    /// events may be flushed manually (forwarded) into another <see cref="IMetaLogger"/>.
    /// If the amount buffer of messages exceeds the limit, the oldest messages are dropped.
    /// If messages were dropped, a warning message is logged to the sink.
    /// If the messages are not flushed in the specified timeout, messages are automatically
    /// flushed into the the sink and a warning is logged.
    /// </summary>
    public sealed class MetaForwardingLogger : MetaLoggerBase
    {
        struct Event
        {
            public LogLevel     Level;
            public Exception    Exception;
            public string       Format;
            public object[]     Args;

            public Event(LogLevel level, Exception exception, string format, object[] args)
            {
                Level = level;
                Exception = exception;
                Format = format;
                Args = args;
            }
        }

        Queue<Event>    _bufferedEvents         = new Queue<Event>();
        Queue<Event>    _flushingBuffer         = new Queue<Event>();
        int             _numDroppedFromBuffer   = 0;
        object          _lock                   = new object();
        IMetaLogger     _sink;
        int             _maxBufferSize;
        TimeSpan        _autoflushAfter;

        enum TimerState
        {
            /// <summary>
            /// Timer will flush the buffer.
            /// </summary>
            Valid,
            /// <summary>
            /// Timer is stale (work was done by normal flush), and timer won't do anything.
            /// </summary>
            Stale,
            /// <summary>
            /// Timer is stale, but there is new work. Try again after the interval.
            /// </summary>
            StaleAndValidWork,
        }
        SystemTimer     _timer;
        TimerState      _timerState;

        public MetaForwardingLogger(IMetaLogger sink, int maxBufferSize, TimeSpan autoflushAfter)
        {
            if (maxBufferSize < 1)
                throw new ArgumentOutOfRangeException(nameof(maxBufferSize));

            _sink = sink;
            _maxBufferSize = maxBufferSize;
            _autoflushAfter = autoflushAfter;
        }

        #region IMetaLogger

        public override sealed bool IsLevelEnabled(LogLevel level) => _sink.IsLevelEnabled(level);

        public override sealed void LogEvent(LogLevel level, Exception ex, string format, params object[] args)
        {
            FreezeArgs(args);

            lock (_lock)
            {
                if (_bufferedEvents.Count >= _maxBufferSize)
                {
                    _bufferedEvents.Dequeue();
                    _numDroppedFromBuffer++;
                }
                _bufferedEvents.Enqueue(new Event(level, ex, format, args));

                // Schedule autoflush
                //  * If there is no timer, create one.
                //  * If there is timer and it's not stale, do nothing
                //  * If there is timer and it's already stale (by a timely flush), retry
                if (_timer == null)
                {
                    _timerState = TimerState.Valid;
                    _timer = new SystemTimer(TimerCallback, state: this, _autoflushAfter, Timeout.InfiniteTimeSpan);
                }
                else if (_timerState == TimerState.Stale)
                {
                    _timerState = TimerState.StaleAndValidWork;
                }
            }
        }

        /// <summary>
        /// Converts mutable object to immutable (string) objects.
        /// </summary>
        void FreezeArgs(object[] args)
        {
            for (int ndx = 0; ndx < args.Length; ndx++)
            {
                object arg = args[ndx];
                if (arg is null)
                    continue;
                if (arg is Exception)
                    continue;

                try
                {
                    args[ndx] = Util.ObjectToStringInvariant(args[ndx]);
                }
                catch
                {
                    args[ndx] = "<ToString() failed>";
                }
            }
        }

        #endregion

        /// <summary>
        /// Flushes events from the buffer to sink. This is not thread safe and may not be called from
        /// multiple threads concurrently or re-entrantly from a sink callback.
        /// </summary>
        public void FlushBufferedToSink()
        {
            int numDroppedFromBuffer;

            // Swap _bufferedEvents to an empty buffer. Since _flushingBuffer is empty here, we swap to it.
            // We also swap the buffered (to-be-emptied list) already into _flushingBuffer. It is not empty
            // but will be by the end of this method. This is safe as nobody else touches it
            lock (_lock)
            {
                Queue<Event> eventsToBeFlushed = _bufferedEvents;
                _bufferedEvents = _flushingBuffer;
                _flushingBuffer = eventsToBeFlushed;
                numDroppedFromBuffer = _numDroppedFromBuffer;
                _numDroppedFromBuffer = 0;

                // Cancel the scheduled autoflush, or more specifically, just make the callback no-op.
                _timerState = TimerState.Stale;
            }

            // Deliver each line to every handler in sink. Maintain the relative order of handlers by first delivering the
            // first line to all handlers, then the next line to all handlers and so on. Not sure if this is necessary but
            // it is a nice behavior.
            try
            {
                if (numDroppedFromBuffer > 0)
                {
                    SendToSink(new Event(LogLevel.Warning, exception: null, "Too many buffered log messages. Dropped {0}", new object[] { numDroppedFromBuffer }));
                }

                foreach(Event logEvent in _flushingBuffer)
                {
                    SendToSink(logEvent);
                }
            }
            finally
            {
                _flushingBuffer.Clear();
            }
        }

        static void TimerCallback(object state)
        {
            MetaForwardingLogger self = (MetaForwardingLogger)state;
            Queue<Event> eventsToBeFlushed;
            int numDroppedFromBuffer;

            lock (self._lock)
            {
                // If cancelled but has new work, retry
                if (self._timerState == TimerState.StaleAndValidWork)
                {
                    self._timerState = TimerState.Valid;
                    self._timer.Change(self._autoflushAfter, Timeout.InfiniteTimeSpan);
                    return;
                }

                // Either this timer is stale (and no work), or this invocation completes all work. Delete in both cases.
                self._timer.Dispose();
                self._timer = null;

                // If cancelled and no work, bail
                if (self._timerState == TimerState.Stale)
                    return;

                // Steal buffered events. Note that we can't use the double buffering trick
                // like in FlushBufferedToSink, since the temporary buffer may be in use by
                // FlushBufferedToSink().
                eventsToBeFlushed = self._bufferedEvents;
                self._bufferedEvents = new Queue<Event>();
                numDroppedFromBuffer = self._numDroppedFromBuffer;
                self._numDroppedFromBuffer = 0;
            }

            // Deliver each line to every handler in sink. Maintain same order as with FlushBufferedToSink()
            try
            {
                self.SendToSink(new Event(LogLevel.Warning, exception: null, "LogBuffer was not flushed by the deadline and was flushed in a background thread.", new object[] { }));

                if (numDroppedFromBuffer > 0)
                {
                    self.SendToSink(new Event(LogLevel.Warning, exception: null, "Too many buffered log messages. Dropped {0}", new object[] { numDroppedFromBuffer }));
                }

                foreach(Event logEvent in eventsToBeFlushed)
                {
                    self.SendToSink(logEvent);
                }
            }
            catch
            {
                // Nobody to report to. Just hide.
            }
        }

        void SendToSink(in Event logEvent)
        {
            try
            {
                _sink.LogEvent(logEvent.Level, logEvent.Exception, logEvent.Format, logEvent.Args);
            }
            catch
            {
            }
        }
    }
}
