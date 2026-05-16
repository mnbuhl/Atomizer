using Atomizer.FlowTests.Drivers;
using Atomizer.FlowTests.Infrastructure;

namespace Atomizer.FlowTests;

public abstract partial class AtomizerFlowTests : IAsyncLifetime
{
    private readonly FlowTestDriver _driver;
    private readonly List<FlowTestHost> _hosts = new List<FlowTestHost>();
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private FlowTestRecorder _recorder = null!;

    protected AtomizerFlowTests(FlowTestDriver driver)
    {
        _driver = driver;
    }

    public async ValueTask InitializeAsync()
    {
        _recorder = new FlowTestRecorder();
        await _driver.ResetAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts.AsEnumerable().Reverse())
        {
            await host.DisposeAsync();
        }

        await _driver.ResetAsync(CancellationToken.None);
    }

    private async Task<FlowTestHost> StartHostAsync(Action<FlowHostOptions>? configure = null)
    {
        var host = await FlowTestHost.CreateAsync(
            _driver,
            _runId,
            _recorder,
            configure,
            TestContext.Current.CancellationToken
        );
        _hosts.Add(host);
        return host;
    }

    private async Task MoveScheduleIntoPastAsync(
        FlowTestHost host,
        JobKey scheduleKey,
        DateTimeOffset lastEnqueueAt,
        DateTimeOffset nextRunAt
    )
    {
        var schedule = (await host.GetSchedulesAsync(TestContext.Current.CancellationToken)).Single(schedule =>
            schedule.JobKey == scheduleKey
        );
        schedule.LastEnqueueAt = lastEnqueueAt;
        schedule.NextRunAt = nextRunAt;
        schedule.CreatedAt = lastEnqueueAt.AddSeconds(-10);
        await host.UpdateSchedulesAsync(new[] { schedule }, TestContext.Current.CancellationToken);
    }

    private async Task WaitUntilDeletedAsync(FlowTestHost host, Guid jobId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(FlowTestTimings.WaitTimeout);

        while (true)
        {
            if (await host.GetJobAsync(jobId, timeout.Token) is null)
                return;

            try
            {
                await Task.Delay(FlowTestTimings.PollInterval, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Job {jobId} was not deleted by retention.");
            }
        }
    }

    private string NewKey() => $"{_driver.Name.Replace(" ", string.Empty).ToLowerInvariant()}-{Guid.NewGuid():N}";
}

[Collection(nameof(InMemoryFlowTestDriver))]
public sealed class InMemoryAtomizerFlowTests(InMemoryFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(PostgreSqlFlowTestDriver))]
public sealed class PostgreSqlAtomizerFlowTests(PostgreSqlFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(MySqlFlowTestDriver))]
public sealed class MySqlAtomizerFlowTests(MySqlFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(SqlServerFlowTestDriver))]
public sealed class SqlServerAtomizerFlowTests(SqlServerFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(RedisFlowTestDriver))]
public sealed class RedisAtomizerFlowTests(RedisFlowTestDriver driver) : AtomizerFlowTests(driver);
