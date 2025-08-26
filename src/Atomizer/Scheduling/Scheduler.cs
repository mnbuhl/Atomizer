using Microsoft.Extensions.Logging;

namespace Atomizer.Scheduling;

internal interface IScheduler
{
    void Start(CancellationToken cancellationToken);
    Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken);
}

internal sealed class Scheduler : IScheduler
{
    private readonly ILogger<Scheduler> _logger;
    private readonly ISchedulePoller _schedulePoller;

    private Task _processingTask = Task.CompletedTask;

    private CancellationTokenSource _ioCts = new CancellationTokenSource();
    private CancellationTokenSource _executionCts = new CancellationTokenSource();

    public Scheduler(ILogger<Scheduler> logger, ISchedulePoller schedulePoller)
    {
        _logger = logger;
        _schedulePoller = schedulePoller;
    }

    public void Start(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Atomizer Scheduler");
        _ioCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCts = new CancellationTokenSource();
        _processingTask = Task.Run(
            async () => await _schedulePoller.RunAsync(_ioCts.Token, _executionCts.Token),
            cancellationToken
        );
    }

    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Atomizer Scheduler");

        _ioCts.Cancel();

        await Task.WhenAny(_processingTask, Task.Delay(gracePeriod, cancellationToken));

        try
        {
            _executionCts.Cancel();
        }
        catch
        {
            _logger.LogDebug("Error cancelling execution token for scheduler");
        }

        _ioCts.Dispose();
        _executionCts.Dispose();
        _logger.LogInformation("Atomizer Scheduler stopped");
    }
}
