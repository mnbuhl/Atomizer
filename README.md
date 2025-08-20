# Atomizer

> **Break down complexity. Scale with confidence.**
> Atomizer transforms large-scale job processing into atomic, reliable, and easily managed units—making distributed systems simple, robust, and a joy to work with.

## Overview
Atomizer is a modern, high-performance job scheduling and queueing framework for ASP.NET Core. Built for cloud-native, distributed applications as well as smaller setups and local development, Atomizer provides a powerful yet easy-to-use solution for processing background jobs, handling complex workflows, and scaling your applications effortlessly.

- **Effortless distributed scaling:** Atomizer works seamlessly in clustered setups, letting you process jobs across multiple servers for true horizontal scalability.
- **Flexible architecture:** Plug in your preferred storage backend, configure multiple queues, and extend with custom drivers or handlers.
- **Reliable and robust:** Enjoy graceful shutdowns, automatic retries, and job visibility timeouts to ensure jobs are never lost or duplicated.
- **Developer-friendly:** Atomizer integrates with ASP.NET Core DI, logging, and modern C# features, so you can focus on your business logic.

## Features
- 🚀 **Distributed Processing** — Scale out to as many servers as your storage backend supports; Atomizer coordinates job execution across the cluster.
- 🗄️ **Multiple Storage Backends** — Use Entity Framework Core for durable, database-backed queues; in-memory for fast local development & testing; Redis support coming soon.
- 🔀 **Multiple Queues** — Configure independent queues with custom processing options for each workload.
- 🧩 **Extensible Drivers & Handlers** — Easily add new storage drivers or job handlers; auto-register handlers from assemblies.
- ♻️ **Retry Policies** — Automatic, configurable retries to keep your jobs running smoothly—even when things go wrong.
- 🛑 **Graceful Shutdown** — Ensure in-flight jobs finish and pending batched jobs are safely released for re-processing during shutdowns.
- 📦 **Batch Processing** — Tune throughput with batch size and parallelism settings per queue.
- ⏳ **Visibility Timeout** — Prevent job duplication by locking jobs during processing.
- 🧪 **In-Memory Driver** — Perfect for local development and testing; spin up queues instantly with zero setup.
- 🔔 **ASP.NET Core Integration** — Works with DI, logging, and modern C# idioms.

## Planned Features
- ⏰ **Recurring Scheduled Jobs** — Cron-like recurring execution for time-based workflows.
- 📈 **Dashboard** — Live monitoring, retry/dead-letter management, and operational insights.
- 🕒 **FIFO Processing** — Guarantee jobs are processed in strict order, without overlap.
- ⚡ **Redis Driver** — Lightning-fast, distributed, in-memory queues for massive scale.
- 🛡️ **Advanced Retry Policies** — Backoff strategies, fixed intervals, and more.

## Quick Start
Get up and running in minutes:

### 1. Install the package
```csharp
// Add Atomizer core and EF Core storage support
 dotnet add package Atomizer
 dotnet add package Atomizer.EntityFrameworkCore
```

### 2. Configure Atomizer
Set up Atomizer in your ASP.NET Core project:
```csharp
builder.Services.AddAtomizer(options =>
{
    // Configure the default queue 
    // (optional, a default queue is created automatically with configuration like below)
    options.AddQueue(QueueKey.Default, queue => 
    {
        queue.DegreeOfParallelism = 4; // Max 4 jobs processed concurrently
        queue.BatchSize = 10; // Retrieve 10 jobs at a time
        queue.VisibilityTimeout = TimeSpan.FromMinutes(5); // Prevent job duplication by "hiding" jobs for 5 minutes while processing
        queue.StorageCheckInterval = TimeSpan.FromSeconds(15); // Poll for new jobs every 15 seconds
    });
    
    // Add more queues as needed
    options.AddQueue("product", queue => 
    {
        queue.DegreeOfParallelism = 2;
        queue.BatchSize = 5;
    });
    
    // Register job handlers automatically
    options.AddHandlersFrom<AssignStockJobHandler>();
    
    // Use EF Core-backed job storage
    options.UseEntityFrameworkCoreStorage<ExampleDbContext>();
});

// Add Atomizer processing services
builder.Services.AddAtomizerProcessing(options =>
{
    options.StartupDelay = TimeSpan.FromSeconds(5); // Delay startup to allow other services to initialize
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30); // Allow up to 30 seconds for jobs to finish on shutdown
});
```

Inside your `DbContext`:
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddAtomizerEntities();
    // ...other model config...
}
```
Make sure to run migrations to create the necessary tables.

### 3. Define a Job Handler
Create a handler for your job payload:
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
            emails.Add(new Email { /* ... */ });
        }
        
        await Task.WhenAll(emails.ConvertAll(email => emailService.SendEmailAsync(email)));
    }
}
```

### 4. Enqueue (or schedule) a Job
Add jobs to the queue from your application code:
```csharp
app.MapPost(
    "/products",
    async ([FromServices] IAtomizerClient atomizerClient, [FromServices] ExampleDbContext dbContext) =>
    {
        var product = new Product { /* ... */, CategoryId = Guid.NewGuid() };
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