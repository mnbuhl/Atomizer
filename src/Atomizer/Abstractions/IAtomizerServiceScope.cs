namespace Atomizer.Abstractions;

public interface IAtomizerServiceScopeFactory
{
    IAtomizerServiceScope CreateScope();
}

public interface IAtomizerServiceScope : IDisposable
{
    IAtomizerStorage Storage { get; }
    IAtomizerLeasingScopeFactory LeasingScopeFactory { get; }
}
