namespace Atomizer.Abstractions;

/// <summary>
/// Creates scoped service instances for job dispatch.
/// </summary>
public interface IAtomizerServiceScopeFactory
{
    /// <summary>
    /// Creates a new service scope for resolving scoped dependencies.
    /// </summary>
    /// <returns>A new <see cref="IAtomizerServiceScope"/> instance.</returns>
    IAtomizerServiceScope CreateScope();
}

/// <summary>
/// Represents a scoped service resolution context for a single job dispatch.
/// </summary>
public interface IAtomizerServiceScope : IDisposable
{
    /// <summary>
    /// Gets the storage instance for this scope.
    /// </summary>
    IAtomizerStorage Storage { get; }
}
