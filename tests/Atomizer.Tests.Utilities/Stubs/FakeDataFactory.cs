namespace Atomizer.Tests.Utilities.Stubs;

public static class FakeDataFactory
{
    public static LeaseToken LeaseToken() => new LeaseToken($"instance1:*:{QueueKey.Default}:*:{Guid.NewGuid():N}");
}
