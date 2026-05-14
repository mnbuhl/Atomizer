using Atomizer.Abstractions;
using Atomizer.Dashboard.Contracts;
using Atomizer.Dashboard.Services;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Endpoints;

internal sealed class SchedulesEndpointHandler
{
    private readonly IAtomizerStorage _storage;
    private readonly DashboardCommandService _commands;

    public SchedulesEndpointHandler(IAtomizerStorage storage, DashboardCommandService commands)
    {
        _storage = storage;
        _commands = commands;
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

    public async Task SetEnabledAsync(HttpContext context)
    {
        if (!Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var request = await context.Request.ReadFromJsonAsync<SetScheduleEnabledRequest>(
            DashboardJsonOptions.CamelCase,
            context.RequestAborted
        );

        if (request is null)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var result = await _commands.SetScheduleEnabledAsync(id, request.Enabled, context.RequestAborted);
        await WriteCommandResultAsync(context, result);
    }

    public async Task RunNowAsync(HttpContext context)
    {
        if (!Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var result = await _commands.RunScheduleNowAsync(id, context.RequestAborted);
        await WriteCommandResultAsync(context, result);
    }

    private static async Task WriteCommandResultAsync<T>(HttpContext context, DashboardCommandResult<T> result)
    {
        context.Response.StatusCode = result.StatusCode;
        if (result.Value is not null)
        {
            await DashboardJsonResponse.WriteAsync(context, result.Value, context.RequestAborted);
        }
        else if (result.Message is not null)
        {
            await DashboardJsonResponse.WriteAsync(context, new { error = result.Message }, context.RequestAborted);
        }
    }
}
