# Atomizer

**Atomizer** is a lightweight, highly extensible background job scheduler for .NET applications.  
It is designed to work seamlessly with **EF Core** and **PostgreSQL/MySQL/SQL Server** out of the box, while allowing future storage providers (e.g., Redis, DocumentDB, in-memory) with minimal friction.

Atomizer runs jobs on a configurable schedule, supports multiple named queues, batching, parallel processing, retry policies, dead-letter queues, and distributed execution safety (row locking).

---

## Features

- **Multiple queues**: Standard, named queues running side-by-side.
- **Storage-agnostic**: EF Core (Postgres/MySQL/SQL Server) out of the box, plus in-memory driver
- **Distributed-safe**: Uses row-level locking in SQL providers.
- **Configurable batching**: Control how many jobs are pulled per lease.
- **Parallel workers**: Configure workers per queue using TPL for high throughput.
- **Error handling & retries**: Built-in retry strategies with extension points.
- **Dead-letter queue**: Failed jobs can be moved to a persistent dead-letter store.
- **Fully extensible**: Add your own storages, retry strategies, logging providers, IoC containers, and more.

---

## Installation

```bash
dotnet add package Atomizer
```

### Quick Start

```csharp
builder.Services.AddAtomizer(opts =>
{
    options.AddQueue(QueueKey.Default); // Add a queue
    options.AddHandlersFrom<SendEmailHandler>(); // Register job handlers
    options.UseInMemoryStorage(); // Configure the storage provider
});
builder.Services.AddAtomizerProcessing(); // Add processing services
```

### Defining a Job & Handler
```csharp
public record SendEmailJob(string Email, string Subject, string Body);

public class SendEmailJobHandler : IAtomizerJobHandler<SendEmailJob>
{
    private readonly IEmailService _email;
    
    public SendEmailJobHandler(IEmailService email) => _email = email;

    public async Task HandleAsync(SendEmailJob payload, JobContext context)
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

### Enqueuing (or scheduling) a Job
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