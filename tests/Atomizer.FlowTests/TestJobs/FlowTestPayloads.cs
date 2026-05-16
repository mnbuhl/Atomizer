namespace Atomizer.FlowTests.TestJobs;

internal sealed record FlowPayload(string Key);

internal sealed record FailingFlowPayload(string Key);

internal sealed record EventuallySuccessfulFlowPayload(string Key, int FailuresBeforeSuccess);

internal sealed record MissingHandlerPayload(string Key);

internal sealed record StopAndResumeFlowPayload(string Key);
