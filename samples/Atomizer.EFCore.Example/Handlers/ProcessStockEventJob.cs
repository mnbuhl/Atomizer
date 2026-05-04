using Atomizer.EFCore.Example.Data.Postgres;

namespace Atomizer.EFCore.Example.Handlers;

public record StockEvent(Guid ProductId, string EventType, int Delta);

public class ProcessStockEventJob(ExamplePostgresContext dbContext, ILogger<ProcessStockEventJob> logger)
    : IAtomizerJob<StockEvent>
{
    public async Task HandleAsync(StockEvent payload, JobContext context)
    {
        var product = await dbContext.Products.FindAsync(
            [payload.ProductId],
            context.CancellationToken
        );

        if (product == null)
        {
            logger.LogWarning("Product {ProductId} not found, skipping stock event", payload.ProductId);
            return;
        }

        product.Quantity += payload.Delta;
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Stock event '{EventType}' applied to product {ProductId}: delta={Delta}, new quantity={Quantity}",
            payload.EventType,
            payload.ProductId,
            payload.Delta,
            product.Quantity
        );
    }
}
