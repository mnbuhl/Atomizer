using Atomizer;
using Atomizer.Configuration;
using Atomizer.Example.Handlers;
using Atomizer.Models;
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
    options.AddQueue(QueueKey.Default);
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost(
    "/log",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient) =>
    {
        await atomizerQueueClient.EnqueueAsync(new LoggerJobPayload("Hello, Atomizer!", LogLevel.Information));
    }
);

app.MapPost(
    "/exception",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient) =>
    {
        await atomizerQueueClient.EnqueueAsync(new ExceptionJobPayload("This job will always fail!"));
    }
);

app.MapPost(
    "/empty",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient) =>
    {
        await atomizerQueueClient.EnqueueAsync(new EmptyPayload());
    }
);

app.MapPost(
    "/schedule",
    async ([FromQuery] int runInSeconds, [FromServices] IAtomizerQueueClient atomizerQueueClient) =>
    {
        var runAt = DateTimeOffset.UtcNow.AddSeconds(runInSeconds);
        await atomizerQueueClient.ScheduleAsync(
            new LoggerJobPayload("This job is scheduled to run in 1 minute.", LogLevel.Information),
            runAt
        );
    }
);

app.MapPost(
    "/log-to-queue",
    async (string queue, [FromServices] IAtomizerQueueClient atomizerQueueClient) =>
    {
        await atomizerQueueClient.EnqueueAsync(
            new LoggerJobPayload($"Logging to {queue} queue!", LogLevel.Information),
            options => options.Queue = queue
        );
    }
);

app.MapPost(
    "/long-running",
    async ([FromQuery] int durationInSeconds, [FromServices] IAtomizerQueueClient atomizerQueueClient) =>
    {
        await atomizerQueueClient.EnqueueAsync(new LongRunningJobPayload(durationInSeconds));
    }
);

app.Run();
