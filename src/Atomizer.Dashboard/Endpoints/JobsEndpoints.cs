using System.Text.Json;
using Atomizer.Dashboard.Configuration;
using Atomizer.Dashboard.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atomizer.Dashboard.Endpoints;

internal static class JobsEndpoints
{
    internal static async Task ListAsync(HttpContext context)
    {
        var storage = context.RequestServices.GetRequiredService<IAtomizerDashboardStorage>();
        var options = context.RequestServices.GetRequiredService<IOptions<DashboardOptions>>().Value;

        var query = context.Request.Query;
        var statuses = query["status"]
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => Enum.Parse<AtomizerJobStatus>(s!, ignoreCase: true))
            .ToList();

        var queueParam = query["queue"].FirstOrDefault();
        var payloadParam = query["payload"].FirstOrDefault();

        DateTimeOffset? from = DateTimeOffset.TryParse(query["from"].FirstOrDefault(), out var f) ? f : null;
        DateTimeOffset? to = DateTimeOffset.TryParse(query["to"].FirstOrDefault(), out var t) ? t : null;

        int skip = int.TryParse(query["skip"].FirstOrDefault(), out var s2) ? s2 : 0;
        int take = int.TryParse(query["take"].FirstOrDefault(), out var tk) ? tk : options.PageSize;

        var jobQuery = new JobQuery
        {
            Statuses = statuses.Count > 0 ? statuses : null,
            QueueKey = queueParam is not null ? new QueueKey(queueParam) : null,
            PayloadTypeName = payloadParam,
            CreatedFromUtc = from,
            CreatedToUtc = to,
            Skip = skip,
            Take = Math.Min(take, 500),
        };

        var result = await storage.GetJobsAsync(jobQuery, context.RequestAborted);

        var response = new PagedResponse<JobDto>
        {
            Items = result.Items.Select(JobDto.From).ToList(),
            TotalCount = result.TotalCount,
            Skip = result.Skip,
            Take = result.Take,
        };

        await DashboardJsonResponse.WriteAsync(context, response, context.RequestAborted);
    }

    internal static async Task GetByIdAsync(HttpContext context)
    {
        var storage = context.RequestServices.GetRequiredService<IAtomizerDashboardStorage>();

        if (!Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var job = await storage.GetJobByIdAsync(id, context.RequestAborted);
        if (job is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        await DashboardJsonResponse.WriteAsync(context, JobDetailDto.FromDetail(job), context.RequestAborted);
    }
}
