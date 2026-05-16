namespace Atomizer.FlowTests.Infrastructure;

internal static class FlowTestTimings
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    public static readonly TimeSpan QueueVisibilityTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ScheduleLeadTime = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);
}
