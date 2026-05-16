using Atomizer.Abstractions;
using Atomizer.Core;

namespace Atomizer.Tests.Core;

public class AtomizerClientTests
{
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
    private readonly IAtomizerServiceScope _serviceScope = Substitute.For<IAtomizerServiceScope>();
    private readonly IAtomizerStorage _storage = Substitute.For<IAtomizerStorage>();
    private readonly IAtomizerJobSerializer _jobSerializer = Substitute.For<IAtomizerJobSerializer>();
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly IAtomizerJobDispatcher _dispatcher = Substitute.For<IAtomizerJobDispatcher>();
    private readonly AtomizerRuntimeIdentity _identity = new("test-instance");
    private readonly TestableLogger<AtomizerClient> _logger = Substitute.For<TestableLogger<AtomizerClient>>();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public AtomizerClientTests()
    {
        _clock.UtcNow.Returns(_now);
        _serviceScope.Storage.Returns(_storage);
        _serviceScopeFactory.CreateScope().Returns(_serviceScope);
        _storage
            .UpsertHeartbeatAsync(Arg.Any<AtomizerActiveServer>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public void Constructor_WhenDispatcherIsNull_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AtomizerClient(_serviceScopeFactory, _jobSerializer, _clock, null!, _identity, _logger)
        );

        exception.ParamName.Should().Be("dispatcher");
    }

    [Fact]
    public async Task DequeueAsync_WhenJobIsPending_ShouldCancelJobAndPersist()
    {
        var job = AtomizerJob.Create(QueueKey.Default, typeof(DirectPayload), "{}", _now, _now);
        _storage.GetJobByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        var sut = CreateSut();

        var dequeued = await sut.DequeueAsync(job.Id, TestContext.Current.CancellationToken);

        dequeued.Should().BeTrue();
        job.Status.Should().Be(AtomizerJobStatus.Cancelled);
        job.UpdatedAt.Should().Be(_now);
        await _storage
            .Received(1)
            .UpdateJobsAsync(
                Arg.Is<IEnumerable<AtomizerJob>>(jobs => jobs.Single() == job),
                TestContext.Current.CancellationToken
            );
    }

    [Fact]
    public async Task DequeueAsync_WhenJobIsNotPending_ShouldReturnFalseWithoutPersisting()
    {
        var job = AtomizerJob.Create(QueueKey.Default, typeof(DirectPayload), "{}", _now, _now);
        job.Lease(
            new LeaseToken($"worker{LeaseToken.Delimiter}{QueueKey.Default}{LeaseToken.Delimiter}{Guid.NewGuid():N}"),
            _now,
            TimeSpan.FromMinutes(1)
        );
        job.Attempt();
        job.MarkAsCompleted(_now);
        _storage.GetJobByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        var sut = CreateSut();

        var dequeued = await sut.DequeueAsync(job.Id, TestContext.Current.CancellationToken);

        dequeued.Should().BeFalse();
        await _storage
            .DidNotReceive()
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRecurringAsync_WhenScheduleExists_ShouldDeleteSchedule()
    {
        var jobKey = new JobKey("daily-report");
        _storage.DeleteScheduleAsync(jobKey, Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();

        var deleted = await sut.DeleteRecurringAsync(jobKey, TestContext.Current.CancellationToken);

        deleted.Should().BeTrue();
        await _storage.Received(1).DeleteScheduleAsync(jobKey, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeleteRecurringAsync_WhenScheduleDoesNotExist_ShouldReturnFalse()
    {
        var jobKey = new JobKey("missing-report");
        _storage.DeleteScheduleAsync(jobKey, Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateSut();

        var deleted = await sut.DeleteRecurringAsync(jobKey, TestContext.Current.CancellationToken);

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerSucceeds_ShouldStoreCompletedJob()
    {
        var payload = new DirectPayload("send");
        var queue = new QueueKey("critical");
        AtomizerJob? finalJob = null;
        AtomizerJobStatus? statusAtInsert = null;
        int? attemptsAtInsert = null;
        AtomizerJobStatus? statusAtDispatch = null;
        _jobSerializer.Serialize(payload).Returns("{\"Message\":\"send\"}");
        _dispatcher
            .DispatchAsync(Arg.Do<AtomizerJob>(job => statusAtDispatch = job.Status), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _storage
            .InsertAsync(
                Arg.Do<AtomizerJob>(job =>
                {
                    statusAtInsert = job.Status;
                    attemptsAtInsert = job.Attempts;
                }),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => ((AtomizerJob)call[0]!).Id);
        _storage
            .UpdateJobsAsync(
                Arg.Do<IEnumerable<AtomizerJob>>(jobs => finalJob = jobs.Single()),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        var jobId = await sut.ExecuteAsync(
            payload,
            options => options.Queue = queue,
            TestContext.Current.CancellationToken
        );

        finalJob.Should().NotBeNull();
        jobId.Should().Be(finalJob!.Id);
        statusAtInsert.Should().Be(AtomizerJobStatus.Processing);
        attemptsAtInsert.Should().Be(0);
        statusAtDispatch.Should().Be(AtomizerJobStatus.Processing);
        finalJob.QueueKey.Should().Be(queue);
        finalJob.PayloadType.Should().Be(typeof(DirectPayload));
        finalJob.Payload.Should().Be("{\"Message\":\"send\"}");
        finalJob.Attempts.Should().Be(1);
        finalJob.Status.Should().Be(AtomizerJobStatus.Completed);
        finalJob.CompletedAt.Should().Be(_now);
        finalJob.FailedAt.Should().BeNull();
        finalJob.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerFails_ShouldStoreFailedJobAndRethrow()
    {
        var payload = new DirectPayload("send");
        var exception = new InvalidOperationException("boom");
        AtomizerJob? finalJob = null;
        AtomizerJobStatus? statusAtInsert = null;
        _jobSerializer.Serialize(payload).Returns("{\"Message\":\"send\"}");
        _dispatcher.DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(_ => throw exception);
        _storage
            .InsertAsync(Arg.Do<AtomizerJob>(job => statusAtInsert = job.Status), Arg.Any<CancellationToken>())
            .Returns(call => ((AtomizerJob)call[0]!).Id);
        _storage
            .UpdateJobsAsync(
                Arg.Do<IEnumerable<AtomizerJob>>(jobs => finalJob = jobs.Single()),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(payload, cancellation: TestContext.Current.CancellationToken)
        );

        thrown.Should().BeSameAs(exception);
        finalJob.Should().NotBeNull();
        statusAtInsert.Should().Be(AtomizerJobStatus.Processing);
        finalJob!.Status.Should().Be(AtomizerJobStatus.Failed);
        finalJob.Attempts.Should().Be(1);
        finalJob.FailedAt.Should().Be(_now);
        finalJob.CompletedAt.Should().BeNull();
        finalJob.Errors.Should().ContainSingle();
        finalJob.Errors.Single().ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancellationIsRequested_ShouldReleaseJobWithoutFailure()
    {
        using var cts = new CancellationTokenSource();
        var payload = new DirectPayload("send");
        AtomizerJob? finalJob = null;
        _jobSerializer.Serialize(payload).Returns("{\"Message\":\"send\"}");
        _dispatcher
            .DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromException(new OperationCanceledException(cts.Token));
            });
        _storage
            .InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(call => ((AtomizerJob)call[0]!).Id);
        _storage
            .UpdateJobsAsync(
                Arg.Do<IEnumerable<AtomizerJob>>(jobs => finalJob = jobs.Single()),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ExecuteAsync(payload, cancellation: cts.Token));

        finalJob.Should().NotBeNull();
        finalJob!.Status.Should().Be(AtomizerJobStatus.Pending);
        finalJob.Attempts.Should().Be(0);
        finalJob.LeaseToken.Should().BeNull();
        finalJob.VisibleAt.Should().BeNull();
        finalJob.FailedAt.Should().BeNull();
        finalJob.Errors.Should().BeEmpty();
        await _storage
            .Received(1)
            .UpdateJobsAsync(
                Arg.Any<IEnumerable<AtomizerJob>>(),
                Arg.Is<CancellationToken>(token => token == CancellationToken.None)
            );
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobIsInserted_ShouldRegisterHeartbeatBeforeInsert()
    {
        var payload = new DirectPayload("send");
        _jobSerializer.Serialize(payload).Returns("{\"Message\":\"send\"}");
        _dispatcher.DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _storage
            .InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(call => ((AtomizerJob)call[0]!).Id);
        _storage
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        await sut.ExecuteAsync(payload, cancellation: TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _storage.UpsertHeartbeatAsync(
                Arg.Is<AtomizerActiveServer>(server =>
                    server.InstanceId == _identity.InstanceId && server.LastHeartbeatAt == _now
                ),
                TestContext.Current.CancellationToken
            );
            _storage.InsertAsync(Arg.Any<AtomizerJob>(), TestContext.Current.CancellationToken);
        });
    }

    private AtomizerClient CreateSut() =>
        new AtomizerClient(_serviceScopeFactory, _jobSerializer, _clock, _dispatcher, _identity, _logger);

    private sealed record DirectPayload(string Message);
}
