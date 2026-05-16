using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Tests.Processing;

public sealed class JobRetentionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenRetentionIsConfigured_ShouldDeleteExpiredJobsUsingRetentionCutoff()
    {
        var now = DateTimeOffset.UtcNow;
        var retention = TimeSpan.FromDays(7);
        var clock = Substitute.For<IAtomizerClock>();
        clock.UtcNow.Returns(now);

        var storage = Substitute.For<IAtomizerStorage>();
        var cleanupAttempted = new TaskCompletionSource<DateTimeOffset>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        storage
            .DeleteExpiredJobsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                cleanupAttempted.TrySetResult(callInfo.Arg<DateTimeOffset>());
                return 2;
            });

        var service = new AtomizerJobRetentionService(
            new TestServiceScopeFactory(storage),
            new AtomizerProcessingOptions
            {
                JobRetention = retention,
                JobRetentionSweepInterval = TimeSpan.FromHours(1),
            },
            clock,
            Substitute.For<TestableLogger<AtomizerJobRetentionService>>()
        );
        var executeAsync = NonPublicSpy.CreateFunc<AtomizerJobRetentionService, CancellationToken, Task>(
            "ExecuteAsync"
        );
        using var cts = new CancellationTokenSource();

        var run = executeAsync(service, cts.Token);

        var cutoff = await cleanupAttempted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        cts.Cancel();

        var awaitRun = async () => await run;
        await awaitRun.Should().ThrowAsync<OperationCanceledException>();

        cutoff.Should().Be(now - retention);
        await storage.Received(1).DeleteExpiredJobsAsync(now - retention, Arg.Any<CancellationToken>());
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
