namespace Atomizer.Tests.Utilities.TestJobs;

public sealed record ClientActionTestPayload(string Message);

public sealed record FailingClientActionTestPayload(string Message);

public sealed class ClientActionTestRecorder
{
    private readonly object _sync = new object();
    private readonly List<string> _messages = new List<string>();

    public Guid? FailedJobId { get; private set; }

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_sync)
            {
                return _messages.ToArray();
            }
        }
    }

    public void RecordMessage(string message)
    {
        lock (_sync)
        {
            _messages.Add(message);
        }
    }

    public void RecordFailedJob(Guid jobId)
    {
        lock (_sync)
        {
            FailedJobId = jobId;
        }
    }
}

public sealed class ClientActionTestJob(ClientActionTestRecorder recorder) : IAtomizerJob<ClientActionTestPayload>
{
    public Task HandleAsync(ClientActionTestPayload payload, JobContext context)
    {
        recorder.RecordMessage(payload.Message);
        return Task.CompletedTask;
    }
}

public sealed class FailingClientActionTestJob(ClientActionTestRecorder recorder)
    : IAtomizerJob<FailingClientActionTestPayload>
{
    public Task HandleAsync(FailingClientActionTestPayload payload, JobContext context)
    {
        recorder.RecordFailedJob(context.Job.Id);
        throw new InvalidOperationException(payload.Message);
    }
}
