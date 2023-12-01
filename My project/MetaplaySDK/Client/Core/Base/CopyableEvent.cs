// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core
{
    /// <summary>
    /// A <i>Copyable</i> replacement for C# events.
    ///
    /// <para>
    /// This implements an API similar to C# "events", with the crucial difference of being copyable. This allows
    /// for attaching and detaching the set of registered event handlers from an event slot.
    /// </para>
    /// <example>
    /// Example usage:
    /// <code>
    /// // Using C# events<br/>
    /// public delegate NewMessageDelegate(string title, string message)<br/>
    /// class NewsFeed<br/>
    /// {<br/>
    ///     public event NewMessageDelegate NewMessage; <br/>
    /// }<br/>
    /// <br/>
    /// // Using CopyableEvent<br/>
    /// public sealed class NewMessageEvent : CopyableEvent&lt;NewMessageEvent, string, string&gt; {} <br/>
    /// class NewsFeed<br/>
    /// {<br/>
    ///     public NewMessageEvent NewMessage; <br/>
    /// }<br/>
    /// <br/>
    /// // Attaching listeners is identical:<br/>
    /// Foo foo;<br/>
    /// foo.NewMessage += OnNewMessage<br/>
    /// <br/>
    /// // invoking is identical:<br/>
    /// NewMessage?.Invoke(title, message);<br/>
    /// </code>
    /// </example>
    /// <para>
    /// Note that the with <c>CopyableEvent</c>, the delegate parameter names are lost. This can be improved by adding
    /// a Invoke overload for each event class, but this only helps naming the parameters at the call site:
    /// </para>
    /// <example>
    /// With named parameters:
    /// <code>
    /// public sealed class NewMessageEvent : CopyableEvent&lt;NewMessageEvent, string, string&gt;<br/>
    /// {<br/>
    /// public new void Invoke(string title, string message) => base.Invoke(title, message); <br/>
    /// } <br/>
    /// </code>
    /// </example>
    /// </summary>
    public class CopyableEvent<DerivedEventType> where DerivedEventType : CopyableEvent<DerivedEventType>, new()
    {
        private Action _invoker = null;
        public void Invoke()
        {
            _invoker?.Invoke();
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType> self, Action action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };

    /// <inheritdoc cref="CopyableEvent{DerivedEventType}"/>
    public class CopyableEvent<DerivedEventType, T1> where DerivedEventType : CopyableEvent<DerivedEventType, T1>, new()
    {
        private Action<T1> _invoker = null;
        public void Invoke(T1 arg1)
        {
            _invoker?.Invoke(arg1);
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType, T1> self, Action<T1> action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };

    /// <inheritdoc cref="CopyableEvent{DerivedEventType}"/>
    public class CopyableEvent<DerivedEventType, T1, T2> where DerivedEventType : CopyableEvent<DerivedEventType, T1, T2>, new()
    {
        private Action<T1, T2> _invoker = null;
        public void Invoke(T1 arg1, T2 arg2)
        {
            _invoker?.Invoke(arg1, arg2);
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType, T1, T2> self, Action<T1, T2> action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };

    /// <inheritdoc cref="CopyableEvent{DerivedEventType}"/>
    public class CopyableEvent<DerivedEventType, T1, T2, T3> where DerivedEventType : CopyableEvent<DerivedEventType, T1, T2, T3>, new()
    {
        private Action<T1, T2, T3> _invoker = null;
        public void Invoke(T1 arg1, T2 arg2, T3 arg3)
        {
            _invoker?.Invoke(arg1, arg2, arg3);
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType, T1, T2, T3> self, Action<T1, T2, T3> action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };

    /// <inheritdoc cref="CopyableEvent{DerivedEventType}"/>
    public class CopyableEvent<DerivedEventType, T1, T2, T3, T4> where DerivedEventType : CopyableEvent<DerivedEventType, T1, T2, T3, T4>, new()
    {
        private Action<T1, T2, T3, T4> _invoker = null;
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            _invoker?.Invoke(arg1, arg2, arg3, arg4);
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType, T1, T2, T3, T4> self, Action<T1, T2, T3, T4> action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };

    /// <inheritdoc cref="CopyableEvent{DerivedEventType}"/>
    public class CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5> where DerivedEventType : CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5>, new()
    {
        private Action<T1, T2, T3, T4, T5> _invoker = null;
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            _invoker?.Invoke(arg1, arg2, arg3, arg4, arg5);
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5> self, Action<T1, T2, T3, T4, T5> action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };

    /// <inheritdoc cref="CopyableEvent{DerivedEventType}"/>
    public class CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5, T6> where DerivedEventType : CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5, T6>, new()
    {
        private Action<T1, T2, T3, T4, T5, T6> _invoker = null;
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            _invoker?.Invoke(arg1, arg2, arg3, arg4, arg5, arg6);
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5, T6> self, Action<T1, T2, T3, T4, T5, T6> action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };

    /// <inheritdoc cref="CopyableEvent{DerivedEventType}"/>
    public class CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5, T6, T7> where DerivedEventType : CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5, T6, T7>, new()
    {
        private Action<T1, T2, T3, T4, T5, T6, T7> _invoker = null;
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            _invoker?.Invoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }
        public static DerivedEventType operator +(CopyableEvent<DerivedEventType, T1, T2, T3, T4, T5, T6, T7> self, Action<T1, T2, T3, T4, T5, T6, T7> action)
        {
            var newEvent = new DerivedEventType();
            newEvent._invoker = (self?._invoker ?? null) + action;
            return newEvent;
        }
    };
}
