using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IQueueCoordinator
{
    void Start(CancellationToken cancellationToken);
    Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken);
}

internal sealed class QueueCoordinator : IQueueCoordinator
{
    private readonly AtomizerOptions _options;
    private readonly ILogger<QueueCoordinator> _logger;
    private readonly IQueuePumpFactory _queuePumpFactory;

    private readonly List<IQueuePump> _queuePumps = new List<IQueuePump>();

    public QueueCoordinator(
        AtomizerOptions options,
        ILogger<QueueCoordinator> logger,
        IQueuePumpFactory queuePumpFactory
    )
    {
        _options = options;
        _logger = logger;
        _queuePumpFactory = queuePumpFactory;
    }

    public void Start(CancellationToken ct)
    {
        _logger.LogInformation("Starting {Count} queue pump(s)...", _options.Queues.Count);
        foreach (var queue in _options.Queues)
        {
            var pump = _queuePumpFactory.Create(queue);
            _queuePumps.Add(pump);
            pump.Start(ct);
        }
    }

    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken ct)
    {
        await Task.WhenAll(_queuePumps.ConvertAll(p => p.StopAsync(gracePeriod, ct)));
        _logger.LogInformation("All queue pumps stopped");
    }
}
