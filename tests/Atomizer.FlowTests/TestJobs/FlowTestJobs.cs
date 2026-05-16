using Atomizer.FlowTests.Infrastructure;

namespace Atomizer.FlowTests.TestJobs;

internal sealed class FlowTestJobMarker;

internal sealed class FlowJob(FlowTestRecorder recorder) : IAtomizerJob<FlowPayload>
{
    public Task HandleAsync(FlowPayload payload, JobContext context)
    {
        recorder.Record(payload.Key, context);
        return Task.CompletedTask;
    }
}

internal sealed class FailingFlowJob(FlowTestRecorder recorder) : IAtomizerJob<FailingFlowPayload>
{
    public Task HandleAsync(FailingFlowPayload payload, JobContext context)
    {
        recorder.Record(payload.Key, context);
        throw new InvalidOperationException(payload.Key);
    }
}

internal sealed class EventuallySuccessfulFlowJob(FlowTestRecorder recorder)
    : IAtomizerJob<EventuallySuccessfulFlowPayload>
{
    public Task HandleAsync(EventuallySuccessfulFlowPayload payload, JobContext context)
    {
        var attempt = recorder.Record(payload.Key, context);
        if (attempt.Attempt <= payload.FailuresBeforeSuccess)
        {
            throw new InvalidOperationException($"attempt-{attempt.Attempt}");
        }

        return Task.CompletedTask;
    }
}

internal sealed class StopAndResumeFlowJob(FlowTestRecorder recorder) : IAtomizerJob<StopAndResumeFlowPayload>
{
    public async Task HandleAsync(StopAndResumeFlowPayload payload, JobContext context)
    {
        recorder.Record(payload.Key, context);
        if (recorder.AttemptsFor(payload.Key).Count > 1)
            return;

        await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
    }
}
