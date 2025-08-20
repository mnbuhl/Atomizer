# Atomizer

> Breaking complexity into atoms — The name “Atomizer” comes from the concept of taking complex, large-scale job processing and breaking it down into the smallest possible executable units (atoms), making distributed processing simpler, more reliable, and easier to reason about.

## Overview
Atomizer is a lightweight, extensible job scheduling & queueing framework for ASP.NET Core, designed for high throughput and low friction in modern distributed applications.
It supports multiple storage backends, graceful shutdowns, all while being easy to extend.

## Features
- Entity Framework Core driver — First-class support for EF Core 7+ with database-backed queues.
- In-memory driver — Great for testing and development (use at your own risk in production).
- Multiple queues — Configure different processing options per queue.
- Retry policies — Automatic retries with custom retry counts.
- Graceful shutdown — Ensure in-flight jobs complete or are released back for re-processing.

## Planned Features
- Recurring scheduled jobs (cron-like recurring execution).
- Configurable retry policies (backoff strategies and fixed intervals).
- Dashboard (live monitoring and retry/dead-letter management).
- FIFO processing (ensuring jobs are processed in the order they were enqueued without overlap).
- Redis driver (for fast, distributed, in-memory queues).

## Quick Start
### 1. Install the package
```csharp
dotnet add package Atomizer
dotnet add package Atomizer.EntityFrameworkCore
```

### 2. Configure Atomizer
Example with EF Core storage and a single queue:
```csharp
builder.Services.AddAtomizer(options =>
{
    // Configure the default queue
    options.AddQueue(QueueKey.Default, queue => 
    {
        queue.DegreeOfParallelism = 4; // Maximum 4 jobs processed concurrently
        queue.BatchSize = 10; // Retrieve 10 jobs from the storage at a time
        queue.VisibilityTimeout = TimeSpan.FromMinutes(5); // Jobs will be invisible for 5 minutes after being fetched
        queue.StorageCheckInterval = TimeSpan.FromSeconds(15); // Check for new jobs every 30 seconds
    });
    
    // Configure a custom queue for product job processing
    options.AddQueue("product", queue => 
    {
        queue.DegreeOfParallelism = 2; // Maximum 2 jobs processed concurrently
        queue.BatchSize = 5; // Retrieve 5 jobs from the storage at a time
    };

    // Register all job handlers from an assembly
    options.AddHandlersFrom<AssignStockJobHandler>();

    // Use EF Core-backed job storage
    options.UseEntityFrameworkCoreStorage<ExampleDbContext>();
});

// Adds the Atomizer processing services
builder.Services.AddAtomizerProcessing();
```

Inside your `DbContext`:
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddAtomizerEntities();
    ...
}
```

### 3. Define a Job Handler
```csharp
public record SendNewsletterCommand(Product Product);

public class SendNewsletterJob(INewsletterService newsletterService, IEmailService emailService)
    : IAtomizerJob<SendNewsletterCommand>
{
    public async Task HandleAsync(SendNewsletterCommand payload, JobContext context)
    {
        var subscribers = await newsletterService.GetSubscribersAsync(payload.Product.CategoryId);

        var emails = new List<Email>();

        foreach (var subscriber in subscribers)
        {
            emails.Add(
                new Email
                {
                    ...
                }
            );
        }

        await Task.WhenAll(emails.ConvertAll(email => emailService.SendEmailAsync(email)));
    }
}
```

### 4. Enqueue (or schedule) a Job
```csharp
app.MapPost(
    "/products",
    async ([FromServices] IAtomizerClient atomizerClient, [FromServices] ExampleDbContext dbContext) =>
    {
        var product = new Product
        {
            ...,
            CategoryId = Guid.NewGuid(),
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        await atomizerClient.EnqueueAsync(new SendNewsletterCommand(product));

        return Results.Created($"/products/{product.Id}", product);
    }
);
```

## Contributing
1. Fork the repository.
2. Create a new branch (feature/xyz).
3. Commit your changes with clear messages.
4. Submit a PR with details of your changes and test coverage.

## License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details