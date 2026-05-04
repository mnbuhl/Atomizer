using Atomizer.Core;

namespace Atomizer.Tests.Processing;

public sealed class HeartbeatConfigurationTests
{
    [Fact]
    public void AtomizerProcessingOptions_WhenDefaultsUsed_ShouldSatisfyHeartbeatRatio()
    {
        var options = new AtomizerProcessingOptions();

        var act = options.Validate;

        act.Should().NotThrow();
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(30));
        options.StaleServerTimeout.Should().Be(TimeSpan.FromMinutes(3));
        options.EffectiveStaleSweepInterval.Should().Be(options.HeartbeatInterval);
    }

    [Fact]
    public void AtomizerProcessingOptions_WhenRatioTooSmall_ShouldThrow()
    {
        var options = new AtomizerProcessingOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(30),
            StaleServerTimeout = TimeSpan.FromSeconds(60),
        };

        var act = options.Validate;

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*six times*");
    }

    [Fact]
    public void AtomizerRuntimeIdentity_WhenInstanceIdContainsLeaseDelimiter_ShouldThrowBeforeRecoveryStarts()
    {
        var act = () => new AtomizerRuntimeIdentity("bad:*:instance");

        act.Should().Throw<ArgumentException>().WithMessage("*delimiter*");
    }
}
