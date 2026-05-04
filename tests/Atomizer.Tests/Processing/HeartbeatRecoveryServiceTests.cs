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

        var storage = Substitute.For<IAtomizerStorage>();
        var heartbeatWritten = new TaskCompletionSource<AtomizerActiveServer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        storage
            .UpsertHeartbeatAsync(Arg.Any<AtomizerActiveServer>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                heartbeatWritten.TrySetResult(callInfo.Arg<AtomizerActiveServer>());
                return Task.CompletedTask;
            });

        storage
            .GetStaleServersAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new AtomizerActiveServer { InstanceId = "stale", LastHeartbeatAt = now.AddMinutes(-10) }]);

        storage
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

        var awaitRun = async () => await run;
        await awaitRun.Should().ThrowAsync<OperationCanceledException>();

        heartbeat.InstanceId.Should().Be("local");
        heartbeat.LastHeartbeatAt.Should().Be(now);
        await storage.Received(1).TryRecoverStaleServerAsync(
            "stale",
            now - TimeSpan.FromMinutes(3),
            now,
            Arg.Any<CancellationToken>()
        );
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
