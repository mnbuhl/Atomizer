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
    /// Starts a fluent recurring schedule builder for schedules that run once per unit.
    /// </summary>
    /// <returns>A builder that can translate a recurrence unit to a cron schedule.</returns>
    public static ScheduleBuilder Every() => new ScheduleBuilder();

    /// <summary>
    /// Starts a fluent recurring schedule builder for interval-based schedules.
    /// </summary>
    /// <param name="interval">The positive interval between occurrences.</param>
    /// <returns>A builder that can translate the interval to a cron schedule.</returns>
    public static ScheduleIntervalBuilder Every(int interval) => new ScheduleIntervalBuilder(interval);

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
