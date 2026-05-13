namespace Atomizer.Tests.Models.ValueObjects;

/// <summary>
/// Unit tests for <see cref="Schedule"/>.
/// </summary>
public class ScheduleTests
{
    [Theory]
    [InlineData(15, "*/15 * * * * *")]
    [InlineData(1, "*/1 * * * * *")]
    public void EverySeconds_ShouldReturnCronSchedule(int interval, string expected)
    {
        // Arrange & Act
        var schedule = Schedule.Every(interval).Seconds();

        // Assert
        schedule.ToString().Should().Be(expected);
    }

    [Fact]
    public void EveryMinutes_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every(5).Minutes();

        // Assert
        schedule.ToString().Should().Be("0 */5 * * * *");
    }

    [Fact]
    public void EveryHours_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every(2).Hours();

        // Assert
        schedule.ToString().Should().Be("0 0 */2 * * *");
    }

    [Fact]
    public void EveryDays_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every(3).Days();

        // Assert
        schedule.ToString().Should().Be("0 0 0 */3 * *");
    }

    [Fact]
    public void EveryWeeks_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every(1).Weeks(DayOfWeek.Monday);

        // Assert
        schedule.ToString().Should().Be("0 0 0 * * 1");
    }

    [Fact]
    public void EveryWeeks_WithIntervalGreaterThanOne_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => Schedule.Every(2).Weeks(DayOfWeek.Monday);

        // Assert
        act.Should().Throw<NotSupportedException>().WithMessage("*do not support intervals greater than 1 week*");
    }

    [Fact]
    public void EveryMonths_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every(2).Months(dayOfMonth: 15);

        // Assert
        schedule.ToString().Should().Be("0 0 0 15 */2 *");
    }

    [Fact]
    public void DailyAt_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.DailyAt(hour: 9, minute: 30, second: 15);

        // Assert
        schedule.ToString().Should().Be("15 30 9 * * *");
    }

    [Fact]
    public void WeeklyOn_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.WeeklyOn(DayOfWeek.Friday, hour: 16, minute: 45);

        // Assert
        schedule.ToString().Should().Be("0 45 16 * * 5");
    }

    [Fact]
    public void MonthlyOn_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.MonthlyOn(dayOfMonth: 31, hour: 23, minute: 59, second: 30);

        // Assert
        schedule.ToString().Should().Be("30 59 23 31 * *");
    }

    [Fact]
    public void Every_WithInvalidInterval_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => Schedule.Every(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be("interval");
    }

    [Theory]
    [InlineData(24, 0, 0, "hour")]
    [InlineData(0, 60, 0, "minute")]
    [InlineData(0, 0, 60, "second")]
    public void DailyAt_WithInvalidTimePart_ShouldThrow(int hour, int minute, int second, string parameterName)
    {
        // Arrange & Act
        Action act = () => Schedule.DailyAt(hour, minute, second);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be(parameterName);
    }

    [Fact]
    public void MonthlyOn_WithInvalidDayOfMonth_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => Schedule.MonthlyOn(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be("dayOfMonth");
    }
}
