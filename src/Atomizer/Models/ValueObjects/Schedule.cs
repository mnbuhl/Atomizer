using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Represents a 6-part cron schedule (seconds, minutes, hours, day-of-month, month, day-of-week).
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
    /// Gets the default schedule (every second).
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
    /// Gets a schedule that fires once per hour at minute 0.
    /// </summary>
    public static Schedule Hourly => new Schedule("0", "0", "*", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires once per day at midnight.
    /// </summary>
    public static Schedule Daily => new Schedule("0", "0", "0", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires once per week.
    /// </summary>
    public static Schedule Weekly => new Schedule("0", "0", "0", "*", "*", "*");

    /// <summary>
    /// Gets a schedule that fires once per month.
    /// </summary>
    public static Schedule Monthly => new Schedule("0", "0", "0", "*", "*", "?");

    /// <summary>
    /// Creates a <see cref="Schedule"/> from a 5-part or 6-part cron expression string.
    /// </summary>
    /// <param name="cronExpression">A cron expression with 5 parts (standard) or 6 parts (seconds-level).</param>
    /// <returns>A new <see cref="Schedule"/> instance.</returns>
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
    /// Returns the 6-part cron expression string for this schedule.
    /// </summary>
    /// <returns>A space-separated cron expression string.</returns>
    public override string ToString() => string.Join(" ", Seconds, Minutes, Hours, DayOfMonth, Month, DayOfWeek);

    /// <summary>
    /// Returns the equality components used to compare two <see cref="Schedule"/> instances.
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
}
