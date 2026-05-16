using Atomizer;
using Atomizer.Core;
using Atomizer.Redis;
using Atomizer.Redis.Example.Handlers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Atomizer Redis Example API", Version = "v1" });
});

builder.Services.AddAtomizer(options =>
{
    options.AddQueue(
        QueueKey.Default,
        queue =>
        {
            queue.BatchSize = 10;
            queue.StorageCheckInterval = TimeSpan.FromSeconds(5);
        }
    );
    options.AddQueue(
        "fifo-events",
        queue =>
        {
            queue.DegreeOfParallelism = 4;
            queue.BatchSize = 10;
            queue.StorageCheckInterval = TimeSpan.FromSeconds(5);
        }
    );
    options.AddHandlersFrom<LoggerJob>();
    options.UseRedisStorage(
        redisConnectionString,
        storage =>
        {
            storage.KeyPrefix = "atomizer:redis-example";
        }
    );
});
builder.Services.AddAtomizerProcessing(options =>
{
    options.StartupDelay = TimeSpan.FromSeconds(5);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddAtomizerDashboard(options =>
{
    options.Title = "Atomizer Redis Example Dashboard";
    options.StatsRefreshInterval = TimeSpan.FromSeconds(5);
    options.JobsRefreshInterval = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var atomizer = app.Services.GetRequiredService<IAtomizerClient>();

await atomizer.ScheduleRecurringAsync(
    new LoggerJobPayload("Redis recurring job started", LogLevel.Information),
    "RedisRecurringLogger",
    Schedule.Cron("0/30 * * * * *")
);

app.MapPost(
    "/log",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(
            new LoggerJobPayload("Hello from the Redis storage sample", LogLevel.Information)
        );
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapPost(
    "/exception",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(
            new ExceptionJobPayload("This Redis-backed sample job fails intentionally")
        );
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapPost(
    "/schedule",
    async (
        [FromQuery] int runInSeconds,
        [FromServices] IAtomizerClock clock,
        [FromServices] IAtomizerClient atomizerClient
    ) =>
    {
        var jobId = await atomizerClient.ScheduleAsync(
            new LoggerJobPayload($"Redis scheduled job delayed by {runInSeconds} seconds", LogLevel.Information),
            clock.UtcNow.AddSeconds(runInSeconds)
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

app.MapPost(
    "/account-events",
    async ([FromBody] AccountEvent accountEvent, [FromServices] IAtomizerClient atomizerClient) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(
            accountEvent,
            options =>
            {
                options.Queue = "fifo-events";
                options.PartitionKey = new PartitionKey(accountEvent.AccountId);
            }
        );
        return Results.Accepted($"/jobs/{jobId}", new { jobId });
    }
);

app.MapAtomizerDashboard();

app.Run();
