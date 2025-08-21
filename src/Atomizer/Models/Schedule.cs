using System;

namespace Atomizer
{
    public sealed class Schedule
    {
        public string Seconds { get; private set; } = "*";
        public string Minutes { get; private set; } = "*";
        public string Hours { get; private set; } = "*";
        public string DayOfMonth { get; private set; } = "*";
        public string Month { get; private set; } = "*";
        public string DayOfWeek { get; private set; } = "*";

        internal Schedule(
            string seconds,
            string minutes,
            string hours,
            string dayOfMonth,
            string month,
            string dayOfWeek
        )
        {
            Seconds = seconds;
            Minutes = minutes;
            Hours = hours;
            DayOfMonth = dayOfMonth;
            Month = month;
            DayOfWeek = dayOfWeek;
        }

        public static Schedule Default => new Schedule("*", "*", "*", "*", "*", "*");
        public static Schedule EverySecond => new Schedule("*", "*", "*", "*", "*", "*");
        public static Schedule EveryMinute => new Schedule("0", "*", "*", "*", "*", "*");
        public static Schedule Hourly => new Schedule("0", "0", "*", "*", "*", "*");
        public static Schedule Daily => new Schedule("0", "0", "0", "*", "*", "*");
        public static Schedule Weekly => new Schedule("0", "0", "0", "*", "*", "*");
        public static Schedule Monthly => new Schedule("0", "0", "0", "*", "*", "?");

        public static Schedule Parse(string cronExpression)
        {
            var parts = cronExpression.Split(' ');

            return parts.Length switch
            {
                6 => new Schedule(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]),
                5 => new Schedule("0", parts[0], parts[1], parts[2], parts[3], parts[4]),
                _ => throw new ArgumentException("Invalid cron expression format. Expected 5 or 6 parts."),
            };
        }

        public override string ToString() => string.Join(" ", Seconds, Minutes, Hours, DayOfMonth, Month, DayOfWeek);
    }
}
