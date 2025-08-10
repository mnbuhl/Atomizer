using Atomizer;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Example.Handlers;
using Atomizer.Storage;
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
    options.AddHandlersFrom<LoggerHandler>();
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

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing",
    "Bracing",
    "Chilly",
    "Cool",
    "Mild",
    "Warm",
    "Balmy",
    "Hot",
    "Sweltering",
    "Scorching",
};

app.MapPost(
        "/log",
        async ([FromServices] IAtomizerClient atomizerClient) =>
        {
            await atomizerClient.EnqueueAsync(new LoggerJob("Hello, Atomizer!", LogLevel.Information));
        }
    )
    .WithOpenApi();

app.MapGet(
        "/weatherforecast",
        () =>
        {
            var forecast = Enumerable
                .Range(1, 5)
                .Select(index => new WeatherForecast(
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
                .ToArray();
            return forecast;
        }
    )
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
