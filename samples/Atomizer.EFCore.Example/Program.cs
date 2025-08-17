using Atomizer;
using Atomizer.Configuration;
using Atomizer.EFCore.Example.Data;
using Atomizer.EFCore.Example.Entities;
using Atomizer.EFCore.Example.Handlers;
using Atomizer.EntityFrameworkCore.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "Atomizer EF Core Example API", Version = "v1" })
);
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<ExampleDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("postgresql"))
        .EnableDetailedErrors()
        .EnableSensitiveDataLogging()
);

builder.Services.AddAtomizer(options =>
{
    options.AddHandlersFrom<AssignStockJob>();
    options.UseEntityFrameworkCoreStorage<ExampleDbContext>();
});
builder.Services.AddAtomizerProcessing(options =>
{
    options.StartupDelay = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using var scope = app.Services.CreateScope();
await using var db = scope.ServiceProvider.GetRequiredService<ExampleDbContext>();
await db.Database.MigrateAsync();

app.MapPost(
    "/products",
    async ([FromServices] ExampleDbContext dbContext) =>
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Sample Product",
            Price = 19.99m,
            Quantity = 0,
            CreatedAt = DateTime.UtcNow,
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/products/{product.Id}", product);
    }
);

app.MapPost(
    "/assign-stock",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient, [FromBody] AssignStock assignStock) =>
    {
        var jobId = await atomizerQueueClient.EnqueueAsync(assignStock);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.MapPost(
    "/cleanup-products",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient, [FromBody] CleanupProductsBefore cleanup) =>
    {
        var jobId = await atomizerQueueClient.EnqueueAsync(cleanup);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.MapPost(
    "/long-running-job",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient, [FromBody] LongRunningJobPayload job) =>
    {
        var jobId = await atomizerQueueClient.EnqueueAsync(job);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.MapPost(
    "/generic-payload-job",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient, [FromBody] GenericPayload<string> payload) =>
    {
        var jobId = await atomizerQueueClient.EnqueueAsync(payload);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.MapPost(
    "/exception-job",
    async ([FromServices] IAtomizerQueueClient atomizerQueueClient, [FromBody] ExceptionJob job) =>
    {
        var jobId = await atomizerQueueClient.EnqueueAsync(job);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.Run();
