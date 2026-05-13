namespace Atomizer;

/// <summary>
/// Represents a paginated result set.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>
    /// The items in the current page.
    /// </summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>
    /// Total count of matching records across all pages.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// The skip offset used to produce this page.
    /// </summary>
    public int Skip { get; init; }

    /// <summary>
    /// The take limit used to produce this page.
    /// </summary>
    public int Take { get; init; }
}
