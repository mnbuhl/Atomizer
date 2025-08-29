namespace Atomizer.Abstractions;

public interface IAtomizerLeasingScope : IDisposable
#if NETCOREAPP3_0_OR_GREATER
        , IAsyncDisposable
#endif
{
    /// <summary>
    /// Whether the scope has been successfully acquired (if no scope wrapping (locks, transactions, etc) is used,
    /// then it should always be true).
    /// </summary>
    bool Acquired { get; }
}
