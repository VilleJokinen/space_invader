// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using System.Linq;

namespace Metaplay.Core.Analytics
{
    /// <summary>
    /// Declare a class as an analytics event.
    /// </summary>
    // \todo [petri] more metadata: server-vs-client? core vs userland? logged vs external? search tag/group?
    [AttributeUsage(AttributeTargets.Class)]
    public class AnalyticsEventAttribute : Attribute, ISerializableTypeCodeProvider
    {
        public          int      TypeCode { get; }
        public readonly string   DisplayName;
        public readonly int      SchemaVersion;
        public readonly string   DocString;
        public readonly bool     IncludeInEventLog;
        public readonly bool     SendToAnalytics;
        public readonly bool     CanTrigger;

        public AnalyticsEventAttribute(int typeCode, string displayName = null, int schemaVersion = 1, string docString = null, bool includeInEventLog = true, bool sendToAnalytics = true, bool canTrigger = false) // \todo [petri] require displayName or always deduce it?
        {
            TypeCode          = typeCode;
            DisplayName       = displayName;
            SchemaVersion     = schemaVersion;
            DocString         = docString;
            IncludeInEventLog = includeInEventLog;
            SendToAnalytics   = sendToAnalytics;
            CanTrigger        = canTrigger;
        }
    }

    /// <summary>
    /// Specify category for an analytics event. Intended to be specified on a base class
    /// for a given category from which the individual event classes can derive.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AnalyticsEventCategoryAttribute : Attribute
    {
        public string CategoryName { get; private set; }

        public AnalyticsEventCategoryAttribute(string categoryName)
        {
            CategoryName = categoryName;
        }
    }

    /// <summary>
    ///
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class AnalyticsEventKeywordsAttribute : Attribute
    {
        public string[] Keywords { get; private set; }

        public AnalyticsEventKeywordsAttribute(params string[] keywords)
        {
            Keywords = keywords;
        }
    }

    /// <summary>
    /// Analytics event handler with configurable handler callback.
    ///
    /// Each event payload is accompanied by a context object
    /// whose meaning is use-case-specific.
    /// In practice, the context object may be used to differentiate
    /// between separate sources the events are associated with,
    /// in case the same event handler instance is used with multiple
    /// different sources.
    /// </summary>
    public class AnalyticsEventHandler<TContext, TEvent> where TEvent : AnalyticsEventBase
    {
        public static readonly AnalyticsEventHandler<TContext, TEvent> NopHandler = new AnalyticsEventHandler<TContext, TEvent>(eventHandler: null);

        Action<TContext, TEvent> _eventHandler;

        public AnalyticsEventHandler(Action<TContext, TEvent> eventHandler)
        {
            _eventHandler = eventHandler;
        }

        public void Event(TContext context, TEvent payload)
        {
            // Invoke handler immediately
            _eventHandler?.Invoke(context, payload);
        }
    }

    /// <summary>
    /// A helper for <see cref="AnalyticsEventHandler{TContext, TEvent}"/>,
    /// wrapping the context object in order to provide an implicit-context
    /// <see cref="Event(TEvent)"/> method.
    /// </summary>
    public readonly ref struct ContextWrappingAnalyticsEventHandler<TContext, TEvent> where TEvent : AnalyticsEventBase
    {
        readonly TContext                                _context;
        readonly AnalyticsEventHandler<TContext, TEvent> _handler;

        public ContextWrappingAnalyticsEventHandler(TContext context, AnalyticsEventHandler<TContext, TEvent> handler)
        {
            _context = context;
            _handler = handler;
        }

        public void Event(TEvent payload)
        {
            _handler?.Event(_context, payload);
        }
    }
}
