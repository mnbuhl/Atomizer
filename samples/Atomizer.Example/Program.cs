using Atomizer;
using Atomizer.Example.Handlers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Atomizer Example API", Version = "v1" });
});

builder.Services.AddAtomizer(options =>
{
    options.AddQueue(
        QueueKey.Default,
        queue =>
        {
            queue.StorageCheckInterval = TimeSpan.FromSeconds(5);
        }
    );
    options.AddQueue(
        "single-worker-queue",
        queue =>
        {
            queue.DegreeOfParallelism = 1;
        }
    );
    options.AddQueue("many-workers-queue", queue => queue.DegreeOfParallelism = 10);
    options.AddQueue(
        "priority-queue",
        queue =>
        {
            queue.DegreeOfParallelism = 5;
            queue.StorageCheckInterval = TimeSpan.FromSeconds(5);
        }
    );
    options.AddHandlersFrom<LoggerJob>();
    options.UseInMemoryStorage();
});
builder.Services.AddAtomizerProcessing();
builder.Services.AddAtomizerDashboard(options =>
{
    options.Title = "Atomizer Example Dashboard";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var atomizer = app.Services.GetRequiredService<IAtomizerClient>();
const string recurringLoggerJob = "LoggerJob";
const string recurringLoggerCatchUpJob = "LoggerJobCatchUp";

await atomizer.ScheduleRecurringAsync(
    new LoggerJobPayload("Recurring job started", LogLevel.Information),
    recurringLoggerJob,
    Schedule.Every(2).Minutes()
);

await atomizer.ScheduleRecurringAsync(
    new LoggerJobPayload("Recurring job started", LogLevel.Information),
    recurringLoggerCatchUpJob,
    Schedule.Cron("0/5 * * * * *"), // Every 5 seconds,
    options => options.MisfirePolicy = MisfirePolicy.CatchUp
);

app.MapPost(
    "/log",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(new LoggerJobPayload("Hello, Atomizer!", LogLevel.Information));
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapPost(
    "/execute/log",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.ExecuteAsync(
            new LoggerJobPayload("Executed immediately from the sample API", LogLevel.Information)
        );
        return Results.Ok(new { jobId });
    }
);

app.MapPost(
    "/exception",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(new ExceptionJobPayload("This job will always fail!"));
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapPost(
    "/empty",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(new EmptyPayload());
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapPost(
    "/schedule",
    async ([FromQuery] int runInSeconds, [FromServices] IAtomizerClient atomizerClient) =>
    {
        var runAt = DateTimeOffset.UtcNow.AddSeconds(runInSeconds);
        var jobId = await atomizerClient.ScheduleAsync(
            new LoggerJobPayload("This job is scheduled to run in 1 minute.", LogLevel.Information),
            runAt
        );
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapPost(
    "/log-to-queue",
    async (string queue, [FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(
            new LoggerJobPayload($"Logging to {queue} queue!", LogLevel.Information),
            options => options.Queue = queue
        );
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapPost(
    "/long-running",
    async ([FromQuery] int durationInSeconds, [FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(new LongRunningJobPayload(durationInSeconds));
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapDelete(
    "/jobs/{jobId:guid}",
    async (Guid jobId, [FromServices] IAtomizerClient atomizerClient) =>
    {
        var dequeued = await atomizerClient.DequeueAsync(jobId);
        return dequeued
            ? Results.NoContent()
            : Results.Conflict(new { message = "The job was not pending or was not found." });
    }
);

app.MapDelete(
    "/recurring/{name}",
    async (string name, [FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.DeleteRecurringAsync(name);
        return Results.NoContent();
    }
);

app.MapAtomizerDashboard();

app.Run();
