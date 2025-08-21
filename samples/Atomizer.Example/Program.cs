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

using var scope = app.Services.CreateScope();
var atomizer = scope.ServiceProvider.GetRequiredService<IAtomizerClient>();

await atomizer.ScheduleRecurringAsync(
    new LoggerJobPayload("Recurring job started", LogLevel.Information),
    "LoggerJob",
    Schedule.Create().EveryMinute().Build()
);

await atomizer.ScheduleRecurringAsync(
    new LoggerJobPayload("Recurring job started", LogLevel.Information),
    "LoggerJobCatchUp",
    Schedule.Parse("0/5 * * * * *"), // Every 5 seconds,
    options => options.MisfirePolicy = MisfirePolicy.CatchUp
);

app.MapPost(
    "/log",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new LoggerJobPayload("Hello, Atomizer!", LogLevel.Information));
    }
);

app.MapPost(
    "/exception",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new ExceptionJobPayload("This job will always fail!"));
    }
);

app.MapPost(
    "/empty",
    async ([FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new EmptyPayload());
    }
);

app.MapPost(
    "/schedule",
    async ([FromQuery] int runInSeconds, [FromServices] IAtomizerClient atomizerClient) =>
    {
        var runAt = DateTimeOffset.UtcNow.AddSeconds(runInSeconds);
        await atomizerClient.ScheduleAsync(
            new LoggerJobPayload("This job is scheduled to run in 1 minute.", LogLevel.Information),
            runAt
        );
    }
);

app.MapPost(
    "/log-to-queue",
    async (string queue, [FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(
            new LoggerJobPayload($"Logging to {queue} queue!", LogLevel.Information),
            options => options.Queue = queue
        );
    }
);

app.MapPost(
    "/long-running",
    async ([FromQuery] int durationInSeconds, [FromServices] IAtomizerClient atomizerClient) =>
    {
        await atomizerClient.EnqueueAsync(new LongRunningJobPayload(durationInSeconds));
    }
);

app.Run();
