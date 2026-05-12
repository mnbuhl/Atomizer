using Atomizer.Dashboard.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Endpoints;

internal static class SchedulesEndpoints
{
    internal static async Task ListAsync(HttpContext context)
    {
        var storage = context.RequestServices.GetRequiredService<IAtomizerDashboardStorage>();
        var schedules = await storage.GetSchedulesAsync(context.RequestAborted);
        await context.Response.WriteAsJsonAsync(
            schedules.Select(ScheduleDto.From).ToList(),
            DashboardJsonOptions.CamelCase,
            context.RequestAborted
        );
    }
}
