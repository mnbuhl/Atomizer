using Atomizer;
using Atomizer.Abstractions;
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
            queue.ConfigureRetryOptions(retry =>
            {
                retry.MaxAttempts = 10;
            });
        }
    );
    options.AddHandlersFrom<LoggerHandler>();
    options.UseInMemoryStorage();
});
builder.Services.AddAtomizerProcessing();

builder.AddServiceDefaults();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost(
    "/log",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new LoggerJob("Hello, Atomizer!", LogLevel.Information));
    }
);

app.MapPost(
    "/exception",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new ExceptionJob("This job will always fail!"));
    }
);

app.MapPost(
    "/empty",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new EmptyPayloadJob());
    }
);

app.MapPost(
    "/schedule",
    async ([FromQuery] int runInSeconds, [FromServices] IAtomizerClient atomizerClient) =>
    {
        var runAt = DateTimeOffset.UtcNow.AddSeconds(runInSeconds);
        await atomizerClient.ScheduleAsync(
            new LoggerJob("This job is scheduled to run in 1 minute.", LogLevel.Information),
            runAt
        );
    }
);

app.MapPost(
    "/log-to-queue",
    async (string queue, [FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(
            new LoggerJob($"Logging to {queue} queue!", LogLevel.Information),
            options => options.Queue = queue
        );
    }
);

app.MapPost(
    "/long-running",
    async ([FromQuery] int durationInSeconds, [FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new LongRunningJob(durationInSeconds));
    }
);

app.MapDefaultEndpoints();

app.Run();
