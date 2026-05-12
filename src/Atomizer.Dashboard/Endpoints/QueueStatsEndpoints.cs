using Atomizer.Dashboard.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Endpoints;

internal static class QueueStatsEndpoints
{
    internal static async Task GetStatsAsync(HttpContext context)
    {
        var storage = context.RequestServices.GetRequiredService<IAtomizerDashboardStorage>();
        var stats = await storage.GetQueueStatsAsync(context.RequestAborted);
        var response = new QueueStatsResponse { Queues = stats.Select(QueueStatsDto.From).ToList() };
        await DashboardJsonResponse.WriteAsync(context, response, context.RequestAborted);
    }
}
