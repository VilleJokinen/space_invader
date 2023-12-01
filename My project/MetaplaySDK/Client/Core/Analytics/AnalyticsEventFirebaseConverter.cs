// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if METAPLAY_ENABLE_FIREBASE_ANALYTICS

using Firebase.Analytics;
using System.Collections.Generic;
using static System.FormattableString;
using System.Text;

namespace Metaplay.Core.Analytics
{
    /// <summary>
    /// Helper class for converting Metaplay analytics event payloads to Firebase Analytics.
    /// </summary>
    public static class AnalyticsEventFirebaseConverter
    {
        public static Parameter[] ToParameters(List<(string, FirebaseAnalyticsFormatter.ParameterValue)> eventParameters)
        {
            List<Parameter> parameters = new List<Parameter>(capacity: eventParameters.Count);
            foreach ((string name, FirebaseAnalyticsFormatter.ParameterValue value) in eventParameters)
            {
                Parameter param;

                if (value.IntegerValue != null)
                    param = new Parameter(name, value.IntegerValue.Value);
                else if (value.DoubleValue != null)
                    param = new Parameter(name, value.DoubleValue.Value);
                else if (value.StringValue != null)
                    param = new Parameter(name, value.StringValue);
                else
                {
                    // null values are not supported in Firebase and they would get converted into (Int)0. This
                    // is suprising so lets rather drop them than produce garbage.
                    continue;
                }

                parameters.Add(param);
            }

            return parameters.ToArray();
        }
        public static string ToString(List<(string, FirebaseAnalyticsFormatter.ParameterValue)> eventParameters)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("[");
            bool isFirst = true;
            foreach ((string name, FirebaseAnalyticsFormatter.ParameterValue value) in eventParameters)
            {
                if (!isFirst)
                    builder.Append(", ");
                isFirst = false;

                if (value.IntegerValue != null)
                    builder.Append(Invariant($"{name} = {value.IntegerValue.Value}"));
                else if (value.DoubleValue != null)
                    builder.Append(Invariant($"{name} = {value.DoubleValue.Value}"));
                else if (value.StringValue != null)
                    builder.Append(Invariant($"{name} = {value.StringValue}"));
                else
                {
                    // Mark nulls specially to emphasis special handling in ToParameters
                    builder.Append(Invariant($"<{name} is null>"));
                }
            }
            builder.Append("]");
            return builder.ToString();
        }
    }
}

#endif // METAPLAY_ENABLE_FIREBASE_ANALYTICS
