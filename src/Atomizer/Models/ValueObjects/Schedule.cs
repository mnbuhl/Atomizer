using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Represents a 6-part cron schedule (seconds-level precision) used for recurring job definitions.
/// </summary>
public sealed class Schedule : ValueObject
{
    /// <summary>
    /// Gets the seconds field of the cron expression.
    /// </summary>
    public string Seconds { get; private set; } = "*";

    /// <summary>
    /// Gets the minutes field of the cron expression.
    /// </summary>
    public string Minutes { get; private set; } = "*";

    /// <summary>
    /// Gets the hours field of the cron expression.
    /// </summary>
    public string Hours { get; private set; } = "*";

    /// <summary>
    /// Gets the day-of-month field of the cron expression.
    /// </summary>
    public string DayOfMonth { get; private set; } = "*";

    /// <summary>
    /// Gets the month field of the cron expression.
    /// </summary>
    public string Month { get; private set; } = "*";

    /// <summary>
    /// Gets the day-of-week field of the cron expression.
    /// </summary>
    public string DayOfWeek { get; private set; } = "*";

    internal Schedule(string seconds, string minutes, string hours, string dayOfMonth, string month, string dayOfWeek)
    {
        Seconds = seconds;
        Minutes = minutes;
        Hours = hours;
        DayOfMonth = dayOfMonth;
        Month = month;
        DayOfWeek = dayOfWeek;
    }

    /// <summary>
    /// Gets a schedule equivalent to every second (* * * * * *).
    /// </summary>
    public static Schedule Default => new Schedule("*", "*", "*", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires every second.
    /// </summary>
    public static Schedule EverySecond => new Schedule("*", "*", "*", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires at the start of every minute.
    /// </summary>
    public static Schedule EveryMinute => new Schedule("0", "*", "*", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires at the top of every hour.
    /// </summary>
    public static Schedule Hourly => new Schedule("0", "0", "*", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires at midnight UTC every day.
    /// </summary>
    public static Schedule Daily => new Schedule("0", "0", "0", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires at midnight UTC every Sunday.
    /// </summary>
    public static Schedule Weekly => new Schedule("0", "0", "0", "*", "*", "0");

    /// <summary>
    /// Gets a schedule that fires at midnight UTC on the 1st of each month.
    /// </summary>
    public static Schedule Monthly => new Schedule("0", "0", "0", "1", "*", "*");

    /// <summary>
    /// Starts a fluent recurring schedule builder for interval-based schedules.
    /// </summary>
    /// <param name="interval">The positive interval between occurrences.</param>
    /// <returns>A builder that can translate the interval to a cron schedule.</returns>
    public static ScheduleBuilder Every(int interval) => new ScheduleBuilder(interval);

    /// <summary>
    /// Creates a schedule that fires daily at the specified UTC time.
    /// </summary>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public static Schedule DailyAt(int hour, int minute = 0, int second = 0)
    {
        ValidateTime(hour, minute, second);

        return new Schedule(second.ToString(), minute.ToString(), hour.ToString(), "*", "*", "*");
    }

    /// <summary>
    /// Creates a schedule that fires weekly on the specified day at the specified UTC time.
    /// </summary>
    /// <param name="dayOfWeek">The day of week on which the schedule fires.</param>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public static Schedule WeeklyOn(DayOfWeek dayOfWeek, int hour = 0, int minute = 0, int second = 0)
    {
        ValidateTime(hour, minute, second);

        return new Schedule(
            second.ToString(),
            minute.ToString(),
            hour.ToString(),
            "*",
            "*",
            ToCronDayOfWeek(dayOfWeek)
        );
    }

    /// <summary>
    /// Creates a schedule that fires monthly on the specified day at the specified UTC time.
    /// </summary>
    /// <param name="dayOfMonth">The day of the month from 1 through 31.</param>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public static Schedule MonthlyOn(int dayOfMonth, int hour = 0, int minute = 0, int second = 0)
    {
        ValidateDayOfMonth(dayOfMonth);
        ValidateTime(hour, minute, second);

        return new Schedule(second.ToString(), minute.ToString(), hour.ToString(), dayOfMonth.ToString(), "*", "*");
    }

    /// <summary>
    /// Creates a <see cref="Schedule"/> from a 5- or 6-part cron expression string.
    /// </summary>
    /// <param name="cronExpression">A standard 5-part or seconds-extended 6-part cron expression.</param>
    /// <returns>A new <see cref="Schedule"/> parsed from the expression.</returns>
    public static Schedule Cron(string cronExpression)
    {
        var parts = cronExpression.Split(' ');

        return parts.Length switch
        {
            6 => new Schedule(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]),
            5 => new Schedule("0", parts[0], parts[1], parts[2], parts[3], parts[4]),
            _ => throw new ArgumentException("Invalid cron expression format. Expected 5 or 6 parts."),
        };
    }

    /// <summary>
    /// Returns the 6-part cron expression string.
    /// </summary>
    public override string ToString() => string.Join(" ", Seconds, Minutes, Hours, DayOfMonth, Month, DayOfWeek);

    /// <summary>
    /// Returns all six cron fields as equality components.
    /// </summary>
    /// <returns>An enumerable of the six cron field strings.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Seconds;
        yield return Minutes;
        yield return Hours;
        yield return DayOfMonth;
        yield return Month;
        yield return DayOfWeek;
    }

    internal static void ValidateTime(int hour, int minute, int second)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), "Hour must be between 0 and 23.");
        }

        if (minute is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), "Minute must be between 0 and 59.");
        }

        if (second is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(second), "Second must be between 0 and 59.");
        }
    }

    internal static void ValidateDayOfMonth(int dayOfMonth)
    {
        if (dayOfMonth is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Day of month must be between 1 and 31.");
        }
    }

    internal static string ToCronDayOfWeek(DayOfWeek dayOfWeek) => ((int)dayOfWeek).ToString();
}
