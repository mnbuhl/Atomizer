using Atomizer.Dashboard.Configuration;

namespace Atomizer.Dashboard.Tests.Configuration;

public class DashboardClientOptionsTests
{
    [Fact]
    public void AddRequestHeader_WhenHeaderNameContainsNewLine_ShouldThrowArgumentException()
    {
        var options = new DashboardOptions();

        var act = () => options.Client.RequestHeaders.Add("X-Atomizer\r\nInjected", "value");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddRequestHeader_WhenHeaderValueContainsNewLine_ShouldThrowArgumentException()
    {
        var options = new DashboardOptions();

        var act = () => options.Client.RequestHeaders.Add("X-Atomizer-Token", "value\r\nInjected");

        act.Should().Throw<ArgumentException>();
    }
}
