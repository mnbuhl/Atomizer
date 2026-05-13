namespace Atomizer;

/// <summary>
/// Builds recurring <see cref="Schedule"/> instances and translates them to 6-part Cronos cron expressions.
/// </summary>
public sealed class ScheduleBuilder
{
    private readonly int _interval;

    internal ScheduleBuilder(int interval)
    {
        if (interval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");
        }

        _interval = interval;
    }

    /// <summary>
    /// Builds a schedule that fires every configured number of seconds.
    /// </summary>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Seconds()
    {
        ValidateInterval(59, "seconds");

        return new Schedule($"*/{_interval}", "*", "*", "*", "*", "*");
    }

    /// <summary>
    /// Builds a schedule that fires every configured number of minutes.
    /// </summary>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Minutes()
    {
        ValidateInterval(59, "minutes");

        return new Schedule("0", $"*/{_interval}", "*", "*", "*", "*");
    }

    /// <summary>
    /// Builds a schedule that fires every configured number of hours.
    /// </summary>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Hours()
    {
        ValidateInterval(23, "hours");

        return new Schedule("0", "0", $"*/{_interval}", "*", "*", "*");
    }

    /// <summary>
    /// Builds a schedule that fires daily at midnight UTC.
    /// </summary>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the interval is greater than one because Cronos cron expressions do not represent every-N-days
    /// schedules reliably across month boundaries.
    /// </exception>
    public Schedule Days()
    {
        if (_interval != 1)
        {
            throw new NotSupportedException("Cronos cron expressions do not support intervals greater than 1 day.");
        }

        return Schedule.Daily;
    }

    /// <summary>
    /// Builds a weekly schedule that fires on the specified day at the specified UTC time.
    /// </summary>
    /// <param name="dayOfWeek">The day of week on which the schedule fires.</param>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the interval is greater than one because Cronos cron expressions do not represent every-N-weeks
    /// schedules reliably.
    /// </exception>
    public Schedule Weeks(DayOfWeek dayOfWeek, int hour = 0, int minute = 0, int second = 0)
    {
        if (_interval != 1)
        {
            throw new NotSupportedException("Cronos cron expressions do not support intervals greater than 1 week.");
        }

        return Schedule.WeeklyOn(dayOfWeek, hour, minute, second);
    }

    /// <summary>
    /// Builds a schedule that fires every configured number of months on the specified day at the specified UTC time.
    /// </summary>
    /// <param name="dayOfMonth">The day of the month from 1 through 31.</param>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Months(int dayOfMonth = 1, int hour = 0, int minute = 0, int second = 0)
    {
        ValidateInterval(12, "months");
        Schedule.ValidateDayOfMonth(dayOfMonth);
        Schedule.ValidateTime(hour, minute, second);

        return new Schedule(
            second.ToString(),
            minute.ToString(),
            hour.ToString(),
            dayOfMonth.ToString(),
            $"*/{_interval}",
            "*"
        );
    }

    private void ValidateInterval(int max, string unit)
    {
        if (_interval > max)
        {
            throw new ArgumentOutOfRangeException(
                "interval",
                _interval,
                $"Interval for {unit} must be between 1 and {max}."
            );
        }
    }
}
