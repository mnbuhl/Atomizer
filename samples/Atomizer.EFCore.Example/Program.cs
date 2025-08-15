using Atomizer;
using Atomizer.Configuration;
using Atomizer.EFCore.Example.Data;
using Atomizer.EFCore.Example.Entities;
using Atomizer.EFCore.Example.Handlers;
using Atomizer.EntityFrameworkCore.Extensions;
using Atomizer.Models;
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
    options.AddQueue(QueueKey.Default);
    options.AddHandlersFrom<AssignStockJobHandler>();
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
    async ([FromServices] IAtomizerClient atomizerClient, [FromBody] AssignStock assignStock) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(assignStock);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.MapPost(
    "/cleanup-products",
    async ([FromServices] IAtomizerClient atomizerClient, [FromBody] CleanupProductsBefore cleanup) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(cleanup);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.MapPost(
    "/long-running-job",
    async ([FromServices] IAtomizerClient atomizerClient, [FromBody] LongRunningJob job) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(job);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.MapPost(
    "/generic-payload-job",
    async ([FromServices] IAtomizerClient atomizerClient, [FromBody] GenericPayload<string> payload) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(payload);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.Run();
