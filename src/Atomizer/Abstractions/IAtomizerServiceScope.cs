namespace Atomizer.Abstractions;

/// <summary>
/// Creates <see cref="IAtomizerServiceScope"/> instances used to resolve scoped services.
/// </summary>
public interface IAtomizerServiceScopeFactory
{
    /// <summary>
    /// Creates a new service scope.
    /// </summary>
    /// <returns>A new <see cref="IAtomizerServiceScope"/> instance.</returns>
    IAtomizerServiceScope CreateScope();
}

/// <summary>
/// Represents a scoped lifetime boundary that exposes the storage instance for the scope.
/// </summary>
public interface IAtomizerServiceScope : IDisposable
{
    /// <summary>
    /// Gets the storage instance available within this scope.
    /// </summary>
    IAtomizerStorage Storage { get; }
}
