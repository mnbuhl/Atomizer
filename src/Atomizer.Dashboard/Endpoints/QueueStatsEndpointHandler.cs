using Atomizer.Abstractions;
using Atomizer.Dashboard.Contracts;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Endpoints;

internal sealed class QueueStatsEndpointHandler
{
    private readonly IAtomizerStorage _storage;

    public QueueStatsEndpointHandler(IAtomizerStorage storage)
    {
        _storage = storage;
    }

    public async Task GetStatsAsync(HttpContext context)
    {
        var stats = await _storage.GetQueueStatsAsync(context.RequestAborted);
        var response = new QueueStatsResponse { Queues = stats.Select(QueueStatsDto.From).ToList() };
        await DashboardJsonResponse.WriteAsync(context, response, context.RequestAborted);
    }
}
