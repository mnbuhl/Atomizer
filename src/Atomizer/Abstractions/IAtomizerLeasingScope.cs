namespace Atomizer.Abstractions;

public interface IAtomizerLeasingScope : IDisposable
#if NETCOREAPP3_0_OR_GREATER
        , IAsyncDisposable
#endif
{
    /// <summary>
    /// Whether the lock has been successfully acquired.
    /// </summary>
    bool Acquired { get; }
}
