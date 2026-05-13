using System.Threading.Channels;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;

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
            _storage
                .ExecuteInLeaseAsync(
                    Arg.Any<QueueKey>(),
                    Arg.Any<Func<CancellationToken, Task<List<AtomizerJob>>>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(callInfo =>
                    callInfo.ArgAt<Func<CancellationToken, Task<List<AtomizerJob>>>>(1)(CancellationToken.None)
                );
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
            var channel = Channel.CreateUnbounded<JobBatch>();
            var jobs = new List<AtomizerJob>
            {
                AtomizerJob.Create(QueueKey.Default, typeof(string), "payload1", _now, _now),
                AtomizerJob.Create(QueueKey.Default, typeof(string), "payload2", _now, _now),
            };
            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(jobs);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100); // short run

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            channel.Reader.TryRead(out var batch1).Should().BeTrue();
            channel.Reader.TryRead(out var batch2).Should().BeTrue();
            batch1!.Jobs.Should().ContainSingle().Which.Should().Be(jobs[0]);
            batch2!.Jobs.Should().ContainSingle().Which.Should().Be(jobs[1]);

            _logger.Received().LogDebug($"Queue '{_queueOptions.QueueKey}' leasing {jobs.Count} job(s)");
        }

        [Fact]
        public async Task RunAsync_WhenPartitionedJobsLeased_ShouldWriteSingleSequenceOrderedBatch()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<JobBatch>();
            var partitionKey = new PartitionKey("customer-1");
            var first = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "payload1",
                _now,
                _now,
                partitionKey: partitionKey
            );
            var second = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "payload2",
                _now,
                _now,
                partitionKey: partitionKey
            );
            first.SequenceNumber = 1;
            second.SequenceNumber = 2;

            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(new List<AtomizerJob> { second, first });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            channel.Reader.TryRead(out var batch).Should().BeTrue();
            batch!.Jobs.Select(j => j.Id).Should().ContainInOrder(first.Id, second.Id);
            channel.Reader.TryRead(out _).Should().BeFalse();
        }

        [Fact]
        public async Task RunAsync_WhenMixedJobsLeased_ShouldWriteSeparateBatchesForUnpartitionedJobsAndPartitions()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<JobBatch>();
            var firstUnpartitioned = CreateUnpartitionedJob("unpartitioned-1");
            var secondUnpartitioned = CreateUnpartitionedJob("unpartitioned-2");
            var firstPartitionJob = CreatePartitionedJob(QueueKey.Default, "customer-1", 1, "partition-1-first");
            var secondPartitionJob = CreatePartitionedJob(QueueKey.Default, "customer-1", 2, "partition-1-second");
            var otherPartitionJob = CreatePartitionedJob(QueueKey.Default, "customer-2", 1, "partition-2-first");

            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(
                    new List<AtomizerJob>
                    {
                        firstUnpartitioned,
                        secondPartitionJob,
                        otherPartitionJob,
                        firstPartitionJob,
                        secondUnpartitioned,
                    }
                );

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            var batches = ReadAllBatches(channel);
            batches.Should().HaveCount(4);
            batches.Where(batch => batch.FirstJob.PartitionKey == null).Should().HaveCount(2);

            var customerOneBatch = batches.Single(batch => batch.FirstJob.PartitionKey?.Key == "customer-1");
            customerOneBatch
                .Jobs.Select(j => j.Id)
                .Should()
                .ContainInOrder(firstPartitionJob.Id, secondPartitionJob.Id);

            var customerTwoBatch = batches.Single(batch => batch.FirstJob.PartitionKey?.Key == "customer-2");
            customerTwoBatch.Jobs.Should().ContainSingle().Which.Id.Should().Be(otherPartitionJob.Id);
        }

        [Fact]
        public async Task RunAsync_WhenJobsLeased_ShouldLeaseEveryJobWrittenToChannel()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<JobBatch>();
            var jobs = new List<AtomizerJob>
            {
                CreateUnpartitionedJob("unpartitioned"),
                CreatePartitionedJob(QueueKey.Default, "customer-1", 1, "partitioned"),
            };

            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(jobs);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            var writtenJobs = ReadAllBatches(channel).SelectMany(batch => batch.Jobs).ToList();
            writtenJobs.Should().BeEquivalentTo(jobs);
            writtenJobs
                .Should()
                .AllSatisfy(job =>
                {
                    job.Status.Should().Be(AtomizerJobStatus.Processing);
                    job.LeaseToken.Should().Be(_leaseToken);
                    job.VisibleAt.Should().Be(_now.Add(_queueOptions.VisibilityTimeout));
                });
        }

        [Fact]
        public async Task RunAsync_WhenSamePartitionKeyIsReturnedForDifferentQueues_ShouldKeepBatchesSeparate()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<JobBatch>();
            var otherQueue = new QueueKey("secondary");
            var defaultQueueJob = CreatePartitionedJob(QueueKey.Default, "shared-customer", 1, "default-queue");
            var otherQueueJob = CreatePartitionedJob(otherQueue, "shared-customer", 1, "other-queue");

            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(new List<AtomizerJob> { defaultQueueJob, otherQueueJob });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Act
            await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            var batches = ReadAllBatches(channel);
            batches.Should().HaveCount(2);
            batches.Should().OnlyContain(batch => batch.Count == 1);
            batches
                .Select(batch => batch.FirstJob.QueueKey)
                .Should()
                .BeEquivalentTo(new[] { QueueKey.Default, otherQueue });
        }

        [Fact]
        public async Task RunAsync_WhenNoJobsLeased_ShouldLogNoJobs()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<JobBatch>();
            _storage
                .GetDueJobsAsync(_queueOptions.QueueKey, _now, _queueOptions.BatchSize, Arg.Any<CancellationToken>())
                .Returns(new List<AtomizerJob>());

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
            var channel = Channel.CreateUnbounded<JobBatch>();
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
            var channel = Channel.CreateUnbounded<JobBatch>();
            var cts = new CancellationTokenSource();
            cts.CancelAfter(10); // cancel during delay

            // Act
            var act = async () => await _sut.RunAsync(_queueOptions, _leaseToken, channel, cts.Token);

            // Assert
            // Should exit without throwing
            await act.Should().NotThrowAsync();
            _logger.Received(1).LogDebug($"Poller for queue '{_queueOptions.QueueKey}' stopped");
        }

        private AtomizerJob CreateUnpartitionedJob(string payload)
        {
            return AtomizerJob.Create(QueueKey.Default, typeof(string), payload, _now, _now);
        }

        private AtomizerJob CreatePartitionedJob(
            QueueKey queueKey,
            string partitionKey,
            long sequenceNumber,
            string payload
        )
        {
            var job = AtomizerJob.Create(
                queueKey,
                typeof(string),
                payload,
                _now,
                _now,
                partitionKey: new PartitionKey(partitionKey)
            );

            job.SequenceNumber = sequenceNumber;
            return job;
        }

        private static List<JobBatch> ReadAllBatches(Channel<JobBatch> channel)
        {
            var batches = new List<JobBatch>();
            while (channel.Reader.TryRead(out var batch))
            {
                batches.Add(batch);
            }

            return batches;
        }
    }
}
