namespace Atomizer.Processing;

internal sealed class JobBatch
{
    public JobBatch(IReadOnlyList<AtomizerJob> jobs)
    {
        if (jobs is null)
        {
            throw new ArgumentNullException(nameof(jobs));
        }

        if (jobs.Count == 0)
        {
            throw new ArgumentException("A job batch must contain at least one job.", nameof(jobs));
        }

        Jobs = jobs;
    }

    public IReadOnlyList<AtomizerJob> Jobs { get; }

    public AtomizerJob FirstJob => Jobs[0];

    public int Count => Jobs.Count;

    public bool IsPartitioned => FirstJob.PartitionKey != null;
}
