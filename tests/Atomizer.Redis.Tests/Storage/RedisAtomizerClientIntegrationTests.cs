using Atomizer.Abstractions;
using Atomizer.Tests.Utilities.TestJobs;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Redis.Tests.Storage;

[Collection(nameof(RedisStorageFixture))]
public sealed class RedisAtomizerClientIntegrationTests : IAsyncLifetime
{
    private readonly RedisStorageFixture _fixture;
    private ServiceProvider? _provider;

    public RedisAtomizerClientIntegrationTests(RedisStorageFixture fixture)
    {
        _fixture = fixture;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await _fixture.FlushAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerSucceeds_ShouldPersistCompletedJobAndRunHandler()
    {
        var provider = CreateProvider();
        var client = provider.GetRequiredService<IAtomizerClient>();

        var jobId = await client.ExecuteAsync(
            new ClientActionTestPayload("direct execution"),
            cancellation: TestContext.Current.CancellationToken
        );

        var job = await GetJobAsync(provider, jobId);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Completed);
        job.CompletedAt.Should().NotBeNull();
        job.FailedAt.Should().BeNull();
        job.Errors.Should().BeEmpty();
        provider.GetRequiredService<ClientActionTestRecorder>().Messages.Should().ContainSingle("direct execution");
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerThrows_ShouldPersistFailedJobAndRethrow()
    {
        var provider = CreateProvider();
        var client = provider.GetRequiredService<IAtomizerClient>();
        var recorder = provider.GetRequiredService<ClientActionTestRecorder>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ExecuteAsync(
                new FailingClientActionTestPayload("direct execution failed"),
                cancellation: TestContext.Current.CancellationToken
            )
        );

        exception.Message.Should().Be("direct execution failed");
        recorder.FailedJobId.Should().NotBeNull();

        var job = await GetJobAsync(provider, recorder.FailedJobId!.Value);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Failed);
        job.CompletedAt.Should().BeNull();
        job.FailedAt.Should().NotBeNull();
        job.Errors.Should().ContainSingle();
        job.Errors.Single().ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task DequeueAsync_WhenScheduledJobIsPending_ShouldCancelPersistedJob()
    {
        var provider = CreateProvider();
        var client = provider.GetRequiredService<IAtomizerClient>();
        var jobId = await client.ScheduleAsync(
            new ClientActionTestPayload("cancel pending"),
            DateTimeOffset.UtcNow.AddHours(1),
            cancellation: TestContext.Current.CancellationToken
        );

        var dequeued = await client.DequeueAsync(jobId, TestContext.Current.CancellationToken);

        dequeued.Should().BeTrue();
        var job = await GetJobAsync(provider, jobId);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Cancelled);
        job.VisibleAt.Should().BeNull();
    }

    [Fact]
    public async Task DeleteRecurringAsync_WhenScheduleExists_ShouldRemoveScheduleAndBeIdempotent()
    {
        var provider = CreateProvider();
        var client = provider.GetRequiredService<IAtomizerClient>();
        var jobKey = new JobKey($"client-action-{Guid.NewGuid():N}");
        await client.ScheduleRecurringAsync(
            new ClientActionTestPayload("recurring"),
            jobKey,
            Schedule.Every(5).Minutes(),
            cancellation: TestContext.Current.CancellationToken
        );

        var deleted = await client.DeleteRecurringAsync(jobKey, TestContext.Current.CancellationToken);
        var deletedAgain = await client.DeleteRecurringAsync(jobKey, TestContext.Current.CancellationToken);

        deleted.Should().BeTrue();
        deletedAgain.Should().BeFalse();
        var schedules = await GetSchedulesAsync(provider);
        schedules.Should().NotContain(schedule => schedule.JobKey == jobKey);
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ClientActionTestRecorder>();
        services.AddAtomizer(options =>
        {
            options.AddHandlersFrom<ClientActionTestJob>();
            options.UseRedisStorage(
                _fixture.Connection,
                storage => storage.KeyPrefix = $"client-actions:{Guid.NewGuid():N}"
            );
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        return _provider;
    }

    private static async Task<AtomizerJob?> GetJobAsync(IServiceProvider provider, Guid jobId)
    {
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        return await storage.GetJobByIdAsync(jobId, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        return await storage.GetSchedulesAsync(TestContext.Current.CancellationToken);
    }
}
