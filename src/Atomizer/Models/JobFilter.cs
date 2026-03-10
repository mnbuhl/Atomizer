namespace Atomizer;

/// <summary>
/// Encapsulates optional filter criteria for querying jobs from an
/// <see cref="Atomizer.Abstractions.IAtomizerStorage"/> implementation.
/// All properties are optional; a <see langword="null"/> value disables that
/// particular filter dimension and all jobs are returned for that dimension.
/// </summary>
/// <remarks>
/// <para>
/// This type is defined in the core <c>Atomizer</c> package so that every storage
/// backend can accept it directly, without depending on the
/// <c>Atomizer.Dashboard</c> package.
/// </para>
/// <para>
/// Multiple non-null properties are combined with a logical AND, i.e. a job must
/// satisfy every active criterion to be included in the result.
/// </para>
/// </remarks>
public sealed class JobFilter
{
    /// <summary>
    /// When set, restricts results to jobs whose <see cref="AtomizerJob.QueueKey"/>
    /// matches this queue name (case-insensitive, substring match).
    /// Pass <see langword="null"/> to include jobs from all queues.
    /// </summary>
    public string? QueueName { get; init; }

    /// <summary>
    /// When set, restricts results to jobs that are currently in the specified
    /// <see cref="AtomizerJobStatus"/>.
    /// Pass <see langword="null"/> to include jobs in any status.
    /// </summary>
    public AtomizerJobStatus? Status { get; init; }

    /// <summary>
    /// Gets a value indicating whether no filter criteria are active.
    /// When <see langword="true"/> every job in the store will match this filter.
    /// </summary>
    public bool IsEmpty => QueueName is null && Status is null;
}
