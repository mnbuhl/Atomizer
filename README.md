# Atomizer

> Breaking complexity into atoms — The name “Atomizer” comes from the concept of taking complex, large-scale job processing and breaking it down into the smallest possible executable units (atoms), making distributed processing simpler, more reliable, and easier to reason about.

## Overview
Atomizer is a lightweight, extensible job scheduling & queueing framework for ASP.NET Core, designed for high throughput and low friction in modern distributed applications.
It supports multiple storage backends, graceful shutdowns, all while being easy to extend.

## Features
- Entity Framework Core driver — First-class support for EF Core 7 with database-backed queues.
- In-memory driver — Great for testing and development (use at your own risk in production).
- Multiple queues — Configure different processing options per queue.
- Graceful shutdown — Ensure in-flight jobs complete or are released back for re-processing.
- Idempotency support — Avoid double-processing when desired.

## Planned Features
- Recurring scheduled jobs (cron-like recurring execution).
- Dashboard (live monitoring and retry/dead-letter management).
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
    // Add the default queue
    options.AddQueue(QueueKey.Default);

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
public record SendEmailCommand(string Email, string Subject, string Body);

public class SendEmailJobHandler : IAtomizerJobHandler<SendEmailCommand>
{
    private readonly IEmailService _email;
    
    public SendEmailJobHandler(IEmailService email) => _email = email;

    public async Task HandleAsync(SendEmailCommand payload, JobContext context)
    {
        await _email.SendAsync(
            payload.Email,
            payload.Subject,
            payload.Body,
            context.CancellationToken 
        );
    }
}
```

### 4. Enqueue (or schedule) a Job
```csharp
public class MyController : ControllerBase
{
    private readonly IAtomizerClient _atomizerClient;

    public MyController(IAtomizerClient atomizerClient) => _atomizerClient = atomizerClient;

    [HttpPost("send-email")]
    public async Task<IActionResult> SendEmail([FromBody] SendEmailJob job)
    {
        await _atomizerClient.EnqueueAsync(
            new SendEmailJob("someone@example.com, "Welcome!", "Hello and welcome!")
        );
        
        await _atomizerClient.ScheduleAsync(
            new SendEmailJob("someone@example.com, "Onboarding", "Let's get started!"), 
            DateTime.UtcNow.AddMinutes(5)
        );
            
        ....
    }
}
```

## Contributing
1. Fork the repository.
2. Create a new branch (feature/xyz).
3. Commit your changes with clear messages.
4. Submit a PR with details of your changes and test coverage.

## License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details