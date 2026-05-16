using Atomizer.Abstractions;
using Atomizer.FlowTests.Drivers;
using Atomizer.FlowTests.TestJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atomizer.FlowTests.Infrastructure;

internal sealed class FlowTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _stopped;

    private FlowTestHost(IHost host)
    {
        _host = host;
    }

    public IAtomizerClient Client => _host.Services.GetRequiredService<IAtomizerClient>();

    public static async Task<FlowTestHost> CreateAsync(
        FlowTestDriver driver,
        string runId,
        FlowTestRecorder recorder,
        Action<FlowHostOptions>? configure,
        CancellationToken cancellationToken
    )
    {
        var options = new FlowHostOptions();
        configure?.Invoke(options);

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(recorder);
                driver.ConfigureServices(services, runId);

                services.AddAtomizer(atomizer =>
                {
                    atomizer.AddHandlersFrom<FlowTestJobMarker>();
                    atomizer.AddQueue(QueueKey.Default, queue => ConfigureQueue(queue, options));
                    atomizer.AddQueue(FlowQueues.Critical, queue => ConfigureQueue(queue, options));
                    atomizer.ConfigureScheduling(scheduling =>
                    {
                        scheduling.StorageCheckInterval = FlowTestTimings.PollInterval;
                        scheduling.ScheduleLeadTime = FlowTestTimings.ScheduleLeadTime;
                        scheduling.TickInterval = FlowTestTimings.PollInterval;
                    });

                    driver.ConfigureStorage(atomizer, runId);
                    options.ConfigureAtomizer?.Invoke(atomizer);
                });

                if (options.AddProcessing)
                {
                    services.AddAtomizerProcessing(processing =>
                    {
                        processing.GracefulShutdownTimeout = options.GracefulShutdownTimeout;
                        processing.HeartbeatInterval = FlowTestTimings.PollInterval;
                        processing.StaleSweepInterval = FlowTestTimings.PollInterval;
                        processing.StaleServerTimeout = TimeSpan.FromMilliseconds(200);
                        processing.JobRetention = options.JobRetention;
                        processing.JobRetentionSweepInterval = FlowTestTimings.PollInterval;
                        options.ConfigureProcessing?.Invoke(processing);
                    });
                }
            })
            .Build();

        var testHost = new FlowTestHost(host);
        if (options.AutoStart)
        {
            await testHost.StartAsync(cancellationToken);
        }

        return testHost;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _host.StartAsync(cancellationToken);
        _stopped = false;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopped)
            return;

        await _host.StopAsync(cancellationToken);
        _stopped = true;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _host.Dispose();
    }

    public async Task<AtomizerJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var scope = _host.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        return await storage.GetJobByIdAsync(jobId, cancellationToken);
    }

    public async Task<IReadOnlyList<AtomizerJob>> GetJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _host.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        var result = await storage.GetJobsAsync(new JobQuery { Take = 500 }, cancellationToken);
        return result.Items;
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        using var scope = _host.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        return await storage.GetSchedulesAsync(cancellationToken);
    }

    public async Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        using var scope = _host.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        await storage.UpdateJobsAsync(jobs, cancellationToken);
    }

    public async Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
    {
        using var scope = _host.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        await storage.UpdateSchedulesAsync(schedules, cancellationToken);
    }

    public async Task UpsertHeartbeatAsync(AtomizerActiveServer server, CancellationToken cancellationToken)
    {
        using var scope = _host.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        await storage.UpsertHeartbeatAsync(server, cancellationToken);
    }

    public async Task<AtomizerJob> WaitForJobAsync(
        Guid jobId,
        Func<AtomizerJob, bool> predicate,
        CancellationToken cancellationToken
    )
    {
        return await WaitUntilAsync(
                async ct => await GetJobAsync(jobId, ct),
                job => job is not null && predicate(job),
                $"Job {jobId} did not reach the expected state.",
                cancellationToken
            ) ?? throw new InvalidOperationException("Wait returned null despite predicate success.");
    }

    public async Task<IReadOnlyList<AtomizerJob>> WaitForJobsAsync(
        Func<IReadOnlyList<AtomizerJob>, bool> predicate,
        string timeoutMessage,
        CancellationToken cancellationToken
    )
    {
        return await WaitUntilAsync(async ct => await GetJobsAsync(ct), predicate, timeoutMessage, cancellationToken);
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> WaitForSchedulesAsync(
        Func<IReadOnlyList<AtomizerSchedule>, bool> predicate,
        string timeoutMessage,
        CancellationToken cancellationToken
    )
    {
        return await WaitUntilAsync(
            async ct => await GetSchedulesAsync(ct),
            predicate,
            timeoutMessage,
            cancellationToken
        );
    }

    private static async Task<T> WaitUntilAsync<T>(
        Func<CancellationToken, Task<T>> probe,
        Func<T, bool> predicate,
        string timeoutMessage,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FlowTestTimings.WaitTimeout);
        Exception? lastProbeException = null;

        while (true)
        {
            T value;
            try
            {
                value = await probe(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException(timeoutMessage, lastProbeException);
            }
            catch (Exception ex) when (!timeout.IsCancellationRequested)
            {
                lastProbeException = ex;
                await DelayOrTimeoutAsync(timeout.Token, timeoutMessage, lastProbeException);
                continue;
            }

            if (predicate(value))
                return value;

            await DelayOrTimeoutAsync(timeout.Token, timeoutMessage, lastProbeException);
        }
    }

    private static async Task DelayOrTimeoutAsync(
        CancellationToken cancellationToken,
        string timeoutMessage,
        Exception? innerException
    )
    {
        try
        {
            await Task.Delay(FlowTestTimings.PollInterval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage, innerException);
        }
    }

    private static void ConfigureQueue(QueueOptions queue, FlowHostOptions options)
    {
        queue.BatchSize = options.QueueBatchSize;
        queue.DegreeOfParallelism = options.QueueDegreeOfParallelism;
        queue.StorageCheckInterval = FlowTestTimings.PollInterval;
        queue.TickInterval = FlowTestTimings.PollInterval;
        queue.VisibilityTimeout = FlowTestTimings.QueueVisibilityTimeout;
    }
}

internal sealed class FlowHostOptions
{
    public bool AddProcessing { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public int QueueBatchSize { get; set; } = 10;
    public int QueueDegreeOfParallelism { get; set; } = 4;
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan? JobRetention { get; set; }
    public Action<AtomizerOptions>? ConfigureAtomizer { get; set; }
    public Action<AtomizerProcessingOptions>? ConfigureProcessing { get; set; }
}
