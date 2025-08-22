using Atomizer;
using Atomizer.EFCore.Example.Data.MySql;
using Atomizer.EFCore.Example.Data.Postgres;
using Atomizer.EFCore.Example.Data.Sqlite;
using Atomizer.EFCore.Example.Data.SqlServer;
using Atomizer.EFCore.Example.Entities;
using Atomizer.EFCore.Example.Handlers;
using Atomizer.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "Atomizer EF Core Example API", Version = "v1" })
);
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddAtomizer(options =>
{
    options.AddHandlersFrom<AssignStockJob>();
    options.UseEntityFrameworkCoreStorage<ExamplePostgresContext>();
});
builder.Services.AddAtomizerProcessing(options =>
{
    options.StartupDelay = TimeSpan.FromSeconds(5);
});

builder.Services.AddDbContext<ExamplePostgresContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("postgresql"))
        .EnableDetailedErrors()
        .EnableSensitiveDataLogging()
);

builder.Services.AddDbContext<ExampleMySqlContext>(o =>
    o.UseMySql(
            builder.Configuration.GetConnectionString("mysql"),
            ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("mysql"))
        )
        .EnableDetailedErrors()
        .EnableSensitiveDataLogging()
);

builder.Services.AddDbContext<ExampleSqlServerContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("mssql"))
        .EnableDetailedErrors()
        .EnableSensitiveDataLogging()
);

builder.Services.AddDbContext<ExampleSqliteContext>(o =>
    o.UseSqlite("Data Source=example.db").EnableDetailedErrors().EnableSensitiveDataLogging()
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using var scope = app.Services.CreateScope();
await using var postgres = scope.ServiceProvider.GetRequiredService<ExamplePostgresContext>();
await using var mysql = scope.ServiceProvider.GetRequiredService<ExampleMySqlContext>();
await using var sqlite = scope.ServiceProvider.GetRequiredService<ExampleSqliteContext>();
await using var sqlServer = scope.ServiceProvider.GetRequiredService<ExampleSqlServerContext>();

await Task.WhenAll(
    postgres.Database.MigrateAsync(),
    mysql.Database.MigrateAsync(),
    sqlite.Database.MigrateAsync(),
    sqlServer.Database.MigrateAsync()
);

var atomizer = app.Services.GetRequiredService<IAtomizerClient>();

await atomizer.ScheduleRecurringAsync(
    new LoggerJobPayload("Recurring job started", LogLevel.Information),
    "LoggerJob",
    Schedule.EveryMinute
);

await atomizer.ScheduleRecurringAsync(
    new LoggerJobPayload("Recurring job started", LogLevel.Information),
    "LoggerJobCatchUp",
    Schedule.Cron("0/5 * * * * *"), // Every 5 seconds,
    options => options.MisfirePolicy = MisfirePolicy.CatchUp
);

app.MapPost(
    "/products",
    async ([FromServices] ExamplePostgresContext dbContext, [FromServices] IAtomizerClient atomizerClient) =>
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
    async ([FromServices] IAtomizerClient atomizerClient, [FromBody] LongRunningJobPayload job) =>
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

app.MapPost(
    "/exception-job",
    async ([FromServices] IAtomizerClient atomizerClient, [FromBody] ExceptionJob job) =>
    {
        var jobId = await atomizerClient.EnqueueAsync(job);
        return Results.Accepted($"/jobs/{jobId}");
    }
);

app.Run();
