using System;

namespace Atomizer
{
    public sealed class Schedule
    {
        public string Seconds { get; }
        public string Minutes { get; }
        public string Hours { get; }
        public string DayOfMonth { get; }
        public string Month { get; }
        public string DayOfWeek { get; }

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

        public static ScheduleBuilder Create() => new ScheduleBuilder();

        public override string ToString() => string.Join(" ", Seconds, Minutes, Hours, DayOfMonth, Month, DayOfWeek);
    }

    public sealed class ScheduleBuilder
    {
        private string _seconds = "*";
        private string _minutes = "*";
        private string _hours = "*";
        private string _dayOfMonth = "*";
        private string _month = "*";
        private string _dayOfWeek = "*";

        public ScheduleBuilder EverySecond()
        {
            _seconds = "*";
            return this;
        }

        public ScheduleBuilder EveryMinute()
        {
            _seconds = "0";
            _minutes = "*";
            return this;
        }

        public ScheduleBuilder Hourly()
        {
            _seconds = "0";
            _minutes = "0";
            _hours = "*";
            return this;
        }

        public ScheduleBuilder Daily()
        {
            _seconds = "0";
            _minutes = "0";
            _hours = "0";
            _dayOfMonth = "*";
            return this;
        }

        public ScheduleBuilder Weekly()
        {
            _seconds = "0";
            _minutes = "0";
            _hours = "0";
            _dayOfMonth = "*";
            _month = "*";
            _dayOfWeek = "*";
            return this;
        }

        public ScheduleBuilder Monthly()
        {
            _seconds = "0";
            _minutes = "0";
            _hours = "0";
            _dayOfMonth = "*";
            _month = "*";
            _dayOfWeek = "?";
            return this;
        }

        public Schedule Build() => new Schedule(_seconds, _minutes, _hours, _dayOfMonth, _month, _dayOfWeek);
    }
}
