// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if METAPLAY_ENABLE_FIREBASE_ANALYTICS
#if METAPLAY_MOCK_FIREBASE_ANALYTICS

// Mock implementation of FirebaseAnalytics to avoid installing the dependency

using Metaplay.Core;
using System.Linq;
using static System.FormattableString;

namespace Firebase.Analytics
{
    public static class FirebaseAnalytics
    {
        public static void LogEvent(string eventType, Parameter[] parameters)
        {
            string paramsStr = string.Join(", ", parameters.Select(p => p.ToKeyValueString()));
            DebugLog.Info("Mock Firebase event {EventType}: [ {EventParams} ]", eventType, paramsStr);
        }
    }

    public class Parameter
    {
        readonly string  _name;
        readonly long?   _integerValue;
        readonly double? _doubleValue;
        readonly string  _stringValue;

        public Parameter(string name, long value) { _name = name; _integerValue = value; }
        public Parameter(string name, double value) { _name = name; _doubleValue = value; }
        public Parameter(string name, string value) { _name = name; _stringValue = value; }

        string ValueToString()
        {
            if (_integerValue.HasValue)
                return Invariant($"{_integerValue.Value}");
            else if (_doubleValue.HasValue)
                return Invariant($"{_doubleValue.Value}");
            else
                return _stringValue;
        }

        public string ToKeyValueString() => $"{_name} = {ValueToString()}";
    }
}

#endif // METAPLAY_MOCK_FIREBASE_ANALYTICS
#endif // METAPLAY_ENABLE_FIREBASE_ANALYTICS
