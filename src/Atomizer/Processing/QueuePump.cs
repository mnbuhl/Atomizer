using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;

namespace Atomizer.Processing
{
    public class QueuePump
    {
        public QueueKey QueueKey { get; }

        private readonly QueueOptions _options;
        private readonly AtomizerOptions _rootOptions;
        private readonly IJobStorage _storage;
        private readonly IJobDispatcher _dispatcher;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerLogger<QueuePump> _logger;

        private readonly Channel<AtomizerJob> _channel;
        private readonly List<Task> _workers = new List<Task>();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private DateTimeOffset _lastStorageCheck;

        private readonly int _workersCount;
        private readonly int _batchSize;

        public QueuePump(
            QueueOptions options,
            AtomizerOptions rootOptions,
            IJobStorage storage,
            IJobDispatcher dispatcher,
            IRetryPolicy retryPolicy,
            IAtomizerClock clock,
            IAtomizerLogger<QueuePump> logger
        )
        {
            _options = options;
            _rootOptions = rootOptions;
            _storage = storage;
            _dispatcher = dispatcher;
            _retryPolicy = retryPolicy;
            _clock = clock;
            _logger = logger;

            QueueKey = options.QueueKey;

            _batchSize = options.BatchSize ?? rootOptions.DefaultBatchSize;
            _workersCount = options.DegreeOfParallelism ?? rootOptions.DefaultDegreeOfParallelism;

            _channel = Channel.CreateBounded<AtomizerJob>(
                new BoundedChannelOptions(Math.Max(4, _batchSize * Math.Max(1, _workersCount)))
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                }
            );

            _lastStorageCheck = _clock.MinValue;
        }

        public void Start(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _ = Task.Run(() => PollLoop(_cts.Token), _cts.Token);

            var workers = Math.Max(1, _workersCount);
            for (int i = 0; i < workers; i++)
            {
                var workerId = $"{QueueKey}-{i}";
                var workerTask = Task.Run(() => WorkerLoop(workerId, _cts.Token), _cts.Token);
                _workers.Add(workerTask);
            }
        }

        public async Task StopAsync()
        {
            _cts.Cancel();
            _channel.Writer.TryComplete();
            try
            {
                await Task.WhenAll(_workers);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation exceptions
            }
            finally
            {
                _cts.Dispose();
            }
        }

        private async Task PollLoop(CancellationToken ct) { }

        private async Task WorkerLoop(string workerId, CancellationToken ct) { }
    }
}
