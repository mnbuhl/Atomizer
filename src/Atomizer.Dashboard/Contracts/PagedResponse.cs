namespace Atomizer.Dashboard.Contracts;

internal sealed class PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = new List<T>();
    public int TotalCount { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
}
