namespace Atomizer.Storage;

public class InMemoryJobStorageOptions
{
    public int AmountOfJobsToRetainInMemory { get; set; } = 1000;
}
