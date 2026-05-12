using Atomizer.Core;
using Atomizer.Dashboard.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Endpoints;

internal static class ServersEndpoints
{
    internal static async Task ListAsync(HttpContext context)
    {
        var storage = context.RequestServices.GetRequiredService<IAtomizerDashboardStorage>();
        var clock = context.RequestServices.GetRequiredService<IAtomizerClock>();
        var servers = await storage.GetActiveServersAsync(context.RequestAborted);
        var now = clock.UtcNow;
        await DashboardJsonResponse.WriteAsync(
            context,
            servers.Select(s => ServerDto.From(s, now)).ToList(),
            context.RequestAborted
        );
    }
}
