using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;
using Atomizer.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Processing
{
    public class QueuePump
    {
        private readonly QueueOptions _queue;
        private readonly DefaultRetryPolicy _retryPolicy;
        private readonly IAtomizerJobDispatcher _dispatcher;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerLogger<QueuePump> _logger;
        private readonly IServiceProvider _serviceProvider;

        private readonly Channel<AtomizerJob> _channel;
        private readonly List<Task> _workers = new List<Task>();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private DateTimeOffset _lastStorageCheck;

        public QueuePump(
            QueueOptions queue,
            DefaultRetryPolicy retryPolicy,
            IAtomizerJobDispatcher dispatcher,
            IAtomizerClock clock,
            IServiceProvider serviceProvider
        )
        {
            _queue = queue;
            _retryPolicy = retryPolicy;
            _dispatcher = dispatcher;
            _clock = clock;
            _logger = serviceProvider.GetRequiredService<IAtomizerLogger<QueuePump>>();
            _serviceProvider = serviceProvider;

            _channel = Channel.CreateBounded<AtomizerJob>(
                new BoundedChannelOptions(_queue.DegreeOfParallelism * _queue.BatchSize)
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

            _logger.LogInformation(
                "Starting queue '{QueueKey}' with {Workers} workers.",
                _queue.QueueKey,
                _queue.DegreeOfParallelism
            );

            _ = Task.Run(() => PollLoop(_cts.Token), _cts.Token);

            var workers = Math.Max(1, _queue.DegreeOfParallelism);
            for (int i = 0; i < workers; i++)
            {
                var workerId = $"{_queue.QueueKey}-{i}";
                var workerTask = Task.Run(() => WorkerLoop(workerId, _cts.Token), _cts.Token);
                _workers.Add(workerTask);
            }
        }

        public async Task StopAsync()
        {
            _logger.LogInformation("Stopping queue '{QueueKey}'...", _queue.QueueKey);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping queue '{QueueKey}'.", _queue.QueueKey);
            }
            finally
            {
                _cts.Dispose();
            }
            _logger.LogInformation("Queue '{QueueKey}' stopped.", _queue.QueueKey);
        }

        private async Task PollLoop(CancellationToken ct)
        {
            var tick = _queue.TickInterval;
            var storageCadence = _queue.StorageCheckInterval;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = _clock.UtcNow;
                    if (now - _lastStorageCheck >= storageCadence)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerJobStorage>();

                        _lastStorageCheck = now;

                        var leased = await storage.TryLeaseBatchAsync(
                            _queue.QueueKey,
                            _queue.BatchSize,
                            now,
                            _queue.VisibilityTimeout,
                            ct
                        );

                        if (leased.Count > 0)
                        {
                            _logger.LogDebug("Queue '{Queue}' leased {Count} job(s)", _queue.QueueKey, leased.Count);

                            foreach (var job in leased)
                            {
                                await _channel.Writer.WriteAsync(job, ct);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Queue '{Queue}' found no jobs to lease", _queue.QueueKey);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested, exit the loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in poll loop for queue '{QueueKey}'", _queue.QueueKey);
                }

                try
                {
                    await Task.Delay(tick, ct);
                }
                catch (TaskCanceledException)
                {
                    // Cancellation requested, exit the loop
                    break;
                }
            }
        }

        private async Task WorkerLoop(string workerId, CancellationToken ct)
        {
            _logger.LogDebug("Worker {Worker} for '{Queue}' started", workerId, _queue.QueueKey);

            while (!ct.IsCancellationRequested)
            {
                AtomizerJob job;
                try
                {
                    job = await _channel.Reader.ReadAsync(ct);
                }
                catch
                {
                    break;
                }

                using var scope = _serviceProvider.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<IAtomizerJobStorage>();

                var swStart = _clock.UtcNow;
                try
                {
                    _logger.LogDebug(
                        "Worker {Worker} executing job {JobId} (attempt {Attempt}) on '{Queue}'",
                        workerId,
                        job.Id,
                        job.Attempt,
                        _queue.QueueKey
                    );
                    await _dispatcher.DispatchAsync(job, ct);
                    await storage.MarkSucceededAsync(job.Id, _clock.UtcNow, ct);
                    _logger.LogInformation(
                        "Job {JobId} succeeded in {Ms}ms on '{Queue}'",
                        job.Id,
                        (int)(_clock.UtcNow - swStart).TotalMilliseconds,
                        _queue.QueueKey
                    );
                }
                catch (TaskCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "Worker {Worker} cancellation requested while processing job {JobId} on '{Queue}'",
                        workerId,
                        job.Id,
                        _queue.QueueKey
                    );
                    // figure out how to handle cancellation gracefully
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Worker {Worker} cancellation requested", workerId);
                    // figure out how to handle cancellation gracefully
                }
                catch (Exception ex)
                {
                    var attempt = job.Attempt;
                    var retryCtx = new AtomizerRetryContext(job);
                    if (_retryPolicy.ShouldRetry(attempt, ex, retryCtx))
                    {
                        var delay = _retryPolicy.GetBackoff(attempt, ex, retryCtx);
                        var nextVisible = _clock.UtcNow + delay;
                        await storage.RescheduleAsync(job.Id, attempt + 1, nextVisible, ct);
                        _logger.LogWarning(
                            "Job {JobId} failed (attempt {Attempt}) on '{Queue}', retrying after {Delay}ms",
                            job.Id,
                            attempt,
                            _queue.QueueKey,
                            (int)delay.TotalMilliseconds
                        );
                    }
                    else
                    {
                        await storage.MoveToDeadLetterAsync(job.Id, ex.Message, ct);
                        await storage.MarkFailedAsync(job.Id, ex, _clock.UtcNow, ct);
                        _logger.LogError(
                            "Job {JobId} exhausted retries and was dead-lettered on '{Queue}'",
                            job.Id,
                            _queue.QueueKey
                        );
                    }
                }
            }
        }
    }
}
