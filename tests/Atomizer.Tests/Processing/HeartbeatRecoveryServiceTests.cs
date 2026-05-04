using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Tests.Processing;

public sealed class HeartbeatRecoveryServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenTickRuns_ShouldWriteHeartbeatAndRecoverStaleServer()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IAtomizerClock>();
        clock.UtcNow.Returns(now);

        var storage = Substitute.For<IAtomizerStorage, IAtomizerHeartbeatRecoveryStorage>();
        var recoveryStorage = (IAtomizerHeartbeatRecoveryStorage)storage;
        var heartbeatWritten = new TaskCompletionSource<AtomizerActiveServer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        recoveryStorage
            .UpsertHeartbeatAsync(Arg.Any<AtomizerActiveServer>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                heartbeatWritten.TrySetResult(callInfo.Arg<AtomizerActiveServer>());
                return Task.CompletedTask;
            });

        recoveryStorage
            .GetStaleServersAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new AtomizerActiveServer { InstanceId = "stale", LastHeartbeatAt = now.AddMinutes(-10) }]);

        recoveryStorage
            .TryRecoverStaleServerAsync("stale", Arg.Any<DateTimeOffset>(), now, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                recoveryAttempted.TrySetResult();
                return new AtomizerHeartbeatRecoveryResult("stale", true, 1);
            });

        var service = new AtomizerHeartbeatRecoveryService(
            new TestServiceScopeFactory(storage),
            new AtomizerRuntimeIdentity("local"),
            new AtomizerProcessingOptions { StaleSweepInterval = TimeSpan.FromHours(1) },
            clock,
            Substitute.For<TestableLogger<AtomizerHeartbeatRecoveryService>>()
        );
        var executeAsync = NonPublicSpy.CreateFunc<AtomizerHeartbeatRecoveryService, CancellationToken, Task>(
            "ExecuteAsync"
        );
        using var cts = new CancellationTokenSource();

        var run = executeAsync(service, cts.Token);

        var heartbeat = await heartbeatWritten.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await recoveryAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        cts.Cancel();

        try
        {
            await run;
        }
        catch (OperationCanceledException) { }

        heartbeat.InstanceId.Should().Be("local");
        heartbeat.LastHeartbeatAt.Should().Be(now);
        recoveryStorage.Received(1).ValidateHeartbeatRecoverySupport();
        await recoveryStorage.Received(1).TryRecoverStaleServerAsync(
            "stale",
            now - TimeSpan.FromMinutes(3),
            now,
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_WhenStorageDoesNotSupportRecovery_ShouldFailClearly()
    {
        var service = new AtomizerHeartbeatRecoveryService(
            new TestServiceScopeFactory(Substitute.For<IAtomizerStorage>()),
            new AtomizerRuntimeIdentity("local"),
            new AtomizerProcessingOptions(),
            Substitute.For<IAtomizerClock>(),
            Substitute.For<TestableLogger<AtomizerHeartbeatRecoveryService>>()
        );
        var executeAsync = NonPublicSpy.CreateFunc<AtomizerHeartbeatRecoveryService, CancellationToken, Task>(
            "ExecuteAsync"
        );

        var act = async () => await executeAsync(service, CancellationToken.None);

        await act.Should().ThrowAsync<Exceptions.InvalidAtomizerConfigurationException>().WithMessage("*must implement*");
    }

    private sealed class TestServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IAtomizerStorage _storage;

        public TestServiceScopeFactory(IAtomizerStorage storage)
        {
            _storage = storage;
        }

        public IServiceScope CreateScope() => new TestServiceScope(_storage);
    }

    private sealed class TestServiceScope : IServiceScope
    {
        public TestServiceScope(IAtomizerStorage storage)
        {
            ServiceProvider = new TestServiceProvider(storage);
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose() { }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly IAtomizerStorage _storage;

        public TestServiceProvider(IAtomizerStorage storage)
        {
            _storage = storage;
        }

        public object? GetService(Type serviceType) => serviceType == typeof(IAtomizerStorage) ? _storage : null;
    }
}
