using Atomizer.Abstractions;
using Atomizer.Dashboard.Contracts;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Endpoints;

internal sealed class SchedulesEndpointHandler
{
    private readonly IAtomizerStorage _storage;

    public SchedulesEndpointHandler(IAtomizerStorage storage)
    {
        _storage = storage;
    }

    public async Task ListAsync(HttpContext context)
    {
        var schedules = await _storage.GetSchedulesAsync(context.RequestAborted);
        await DashboardJsonResponse.WriteAsync(
            context,
            schedules.Select(ScheduleDto.From).ToList(),
            context.RequestAborted
        );
    }
}
