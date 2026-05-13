namespace Atomizer;

/// <summary>
/// Builds recurring <see cref="Schedule"/> instances that run once per selected unit.
/// </summary>
public sealed class ScheduleBuilder
{
    internal ScheduleBuilder() { }

    /// <summary>
    /// Builds a schedule that fires every second.
    /// </summary>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Second() => new Schedule("*", "*", "*", "*", "*", "*");

    /// <summary>
    /// Builds a schedule that fires at the start of every minute.
    /// </summary>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Minute() => new Schedule("0", "*", "*", "*", "*", "*");

    /// <summary>
    /// Builds a schedule that fires at the top of every hour.
    /// </summary>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Hour() => new Schedule("0", "0", "*", "*", "*", "*");

    /// <summary>
    /// Builds a schedule that fires daily at the specified UTC time.
    /// </summary>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Day(int hour = 0, int minute = 0, int second = 0)
    {
        Schedule.ValidateTime(hour, minute, second);

        return new Schedule(second.ToString(), minute.ToString(), hour.ToString(), "*", "*", "*");
    }

    /// <summary>
    /// Builds a schedule that fires weekly on the specified day at the specified UTC time.
    /// </summary>
    /// <param name="dayOfWeek">The day of week on which the schedule fires.</param>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Week(DayOfWeek dayOfWeek = DayOfWeek.Sunday, int hour = 0, int minute = 0, int second = 0)
    {
        Schedule.ValidateTime(hour, minute, second);

        return new Schedule(
            second.ToString(),
            minute.ToString(),
            hour.ToString(),
            "*",
            "*",
            Schedule.ToCronDayOfWeek(dayOfWeek)
        );
    }

    /// <summary>
    /// Builds a schedule that fires monthly on the specified day at the specified UTC time.
    /// </summary>
    /// <param name="dayOfMonth">The day of the month from 1 through 31.</param>
    /// <param name="hour">The UTC hour from 0 through 23.</param>
    /// <param name="minute">The minute from 0 through 59.</param>
    /// <param name="second">The second from 0 through 59.</param>
    /// <returns>A schedule translated to a 6-part cron expression.</returns>
    public Schedule Month(int dayOfMonth = 1, int hour = 0, int minute = 0, int second = 0)
    {
        Schedule.ValidateDayOfMonth(dayOfMonth);
        Schedule.ValidateTime(hour, minute, second);

        return new Schedule(second.ToString(), minute.ToString(), hour.ToString(), dayOfMonth.ToString(), "*", "*");
    }
}
