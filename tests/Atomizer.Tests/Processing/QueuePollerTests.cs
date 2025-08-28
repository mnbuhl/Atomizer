using System.Threading.Channels;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Locking;
using Atomizer.Processing;
using Atomizer.Storage;

namespace Atomizer.Tests.Processing
{
    /// <summary>
    /// Unit tests for <see cref="QueuePoller"/>.
    /// </summary>
    public class QueuePollerTests
    {
        private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
        private readonly IAtomizerServiceScopeFactory _scopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
        private readonly IAtomizerServiceScope _scope = Substitute.For<IAtomizerServiceScope>();
        private readonly IAtomizerStorage _storage = Substitute.For<IAtomizerStorage>();
        private readonly TestableLogger<QueuePoller> _logger = Substitute.For<TestableLogger<QueuePoller>>();
        private readonly QueuePoller _sut;
        private readonly QueueOptions _queueOptions;
        private readonly LeaseToken _leaseToken = new LeaseToken("instance:*:default:*:lease");
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        public QueuePollerTests()
        {
            _clock.UtcNow.Returns(_now);
            _clock.MinValue.Returns(DateTimeOffset.MinValue);
            _scope.Storage.Returns(_storage);
            _scopeFactory.CreateScope().Returns(_scope);
            _queueOptions = new QueueOptions(QueueKey.Default)
            {
                BatchSize = 2,
                DegreeOfParallelism = 4,
                VisibilityTimeout = TimeSpan.FromMinutes(10),
                StorageCheckInterval = TimeSpan.FromSeconds(1),
            };
            _sut = new QueuePoller(_clock, _scopeFactory, _logger);
        }

        [Fact]
        public async Task RunAsync_WhenJobsLeased_ShouldWriteToChannelAndLog()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<AtomizerJob>();
            var jobs = new List<AtomizerJob>
            {
                AtomizerJob.Create(QueueKey.Default, typeof(string), "payload1", _now, _now),
                AtomizerJob.Create(QueueKey.Default, typeof(string), "payload2", _now, _now),
            };
            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(jobs);
            _storage
                .AcquireLockAsync(_queueOptions.QueueKey, _queueOptions.VisibilityTimeout, Arg.Any<CancellationToken>())
                .Returns(new NoopLock());

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100); // short run

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            channel.Reader.TryRead(out var job1).Should().BeTrue();
            channel.Reader.TryRead(out var job2).Should().BeTrue();
            job1.Should().Be(jobs[0]);
            job2.Should().Be(jobs[1]);

            _logger.Received().LogDebug($"Queue '{_queueOptions.QueueKey}' leasing {jobs.Count} job(s)");
        }

        [Fact]
        public async Task RunAsync_WhenNoJobsLeased_ShouldLogNoJobs()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<AtomizerJob>();
            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(new List<AtomizerJob>());
            _storage
                .AcquireLockAsync(_queueOptions.QueueKey, _queueOptions.VisibilityTimeout, Arg.Any<CancellationToken>())
                .Returns(new NoopLock());

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            _logger.Received().LogDebug($"Queue '{_queueOptions.QueueKey}' found no jobs to lease");
        }

        [Fact]
        public async Task RunAsync_WhenExceptionThrown_ShouldLogError()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<AtomizerJob>();
            _scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("fail"));
            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            _logger
                .Received()
                .LogError(Arg.Any<Exception>(), $"Error in poll loop for queue '{_queueOptions.QueueKey}'");
        }

        [Fact]
        public async Task RunAsync_WhenDelayCancelled_ShouldExitLoop()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<AtomizerJob>();
            var cts = new CancellationTokenSource();
            cts.CancelAfter(10); // cancel during delay

            // Act
            var act = async () => await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            // Should exit without throwing
            await act.Should().NotThrowAsync();
            _logger.Received(1).LogDebug($"Poller for queue '{_queueOptions.QueueKey}' stopped");
        }
    }
}
