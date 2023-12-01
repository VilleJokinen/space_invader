// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core
{
    public static class TimeUtils
    {
        /// <summary>
        /// Check whether a given time is within a time window. The window is evaluated as if it was a
        /// daily repeating window. Eg: you can define the window as being from 10am to 11am every day.
        /// Windows that straddle midnight also work, eg: a window that starts at 11pm and lasts two
        /// hours.
        /// </summary>
        /// <param name="time">Time to check</param>
        /// <param name="windowStart">Start time of the window must be +ve and less than a day</param>
        /// <param name="windowLength">Length of the window. The maximum length is 24 hours.</param>
        /// <returns></returns>
        public static bool IsWithinDailyWindow(MetaTime time, TimeSpan windowStart, TimeSpan windowLength)
        {
            if (windowStart.TotalDays < 0 || windowStart.TotalDays >= 1)
                throw new ArgumentException("windowStart must be a positive value between 0 and 24 hours");
            if (windowLength.TotalDays <= 0)
                throw new ArgumentException("windowLength must be greater than 0");
            if (windowLength.TotalDays > 1)
                throw new ArgumentException("windowLength must not be greater than one day");

            DateTime dateTime = time.ToDateTime();
            TimeSpan timeWithoutDate = dateTime.TimeOfDay;
            TimeSpan windowEnd = windowStart + windowLength;

            if (windowEnd < new TimeSpan(24, 0, 0))
            {
                // Window exists entirely within a single day
                if (timeWithoutDate >= windowStart && timeWithoutDate <= windowEnd)
                    return true;
            }
            else
            {
                // Window straddles midnight
                windowEnd = new TimeSpan(0, windowEnd.Hours, windowEnd.Minutes, windowEnd.Seconds, windowEnd.Milliseconds);
                if (timeWithoutDate >= windowStart || timeWithoutDate <= windowEnd)
                    return true;
            }

            return false;
        }
    }
}
