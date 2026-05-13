namespace Atomizer.Tests.Models.ValueObjects;

/// <summary>
/// Unit tests for <see cref="Schedule"/>.
/// </summary>
public class ScheduleTests
{
    [Fact]
    public void EverySecond_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Second();

        // Assert
        schedule.ToString().Should().Be("* * * * * *");
    }

    [Fact]
    public void EveryMinute_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Minute();

        // Assert
        schedule.ToString().Should().Be("0 * * * * *");
    }

    [Fact]
    public void EveryHour_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Hour();

        // Assert
        schedule.ToString().Should().Be("0 0 * * * *");
    }

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
    public void EveryDay_ShouldReturnDailyCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Day();

        // Assert
        schedule.ToString().Should().Be("0 0 0 * * *");
    }

    [Fact]
    public void EveryDays_WithIntervalGreaterThanOne_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => Schedule.Every(3).Days();

        // Assert
        act.Should().Throw<NotSupportedException>().WithMessage("*do not support intervals greater than 1 day*");
    }

    [Fact]
    public void EveryWeek_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Week(DayOfWeek.Monday);

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
    public void EveryMonths_ShouldReturnIntervalCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every(2).Months(dayOfMonth: 15);

        // Assert
        schedule.ToString().Should().Be("0 0 0 15 */2 *");
    }

    [Fact]
    public void EveryDay_WithTime_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Day(hour: 9, minute: 30, second: 15);

        // Assert
        schedule.ToString().Should().Be("15 30 9 * * *");
    }

    [Fact]
    public void EveryWeek_WithTime_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Week(DayOfWeek.Friday, hour: 16, minute: 45);

        // Assert
        schedule.ToString().Should().Be("0 45 16 * * 5");
    }

    [Fact]
    public void EveryMonth_WithTime_ShouldReturnCronSchedule()
    {
        // Arrange & Act
        var schedule = Schedule.Every().Month(dayOfMonth: 31, hour: 23, minute: 59, second: 30);

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
    [InlineData(60, "Seconds")]
    [InlineData(60, "Minutes")]
    [InlineData(24, "Hours")]
    [InlineData(13, "Months")]
    public void EveryUnit_WithIntervalOutsideCronFieldRange_ShouldThrow(int interval, string unit)
    {
        // Arrange
        var builder = Schedule.Every(interval);

        // Act
        Action act = unit switch
        {
            "Seconds" => () => builder.Seconds(),
            "Minutes" => () => builder.Minutes(),
            "Hours" => () => builder.Hours(),
            "Months" => () => builder.Months(),
            _ => throw new InvalidOperationException("Unsupported schedule unit."),
        };

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be("interval");
    }

    [Theory]
    [InlineData(24, 0, 0, "hour")]
    [InlineData(0, 60, 0, "minute")]
    [InlineData(0, 0, 60, "second")]
    public void EveryDay_WithInvalidTimePart_ShouldThrow(int hour, int minute, int second, string parameterName)
    {
        // Arrange & Act
        Action act = () => Schedule.Every().Day(hour, minute, second);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be(parameterName);
    }

    [Fact]
    public void EveryMonth_WithInvalidDayOfMonth_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => Schedule.Every().Month(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be("dayOfMonth");
    }
}
