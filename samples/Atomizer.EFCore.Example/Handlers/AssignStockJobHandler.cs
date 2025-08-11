using Atomizer.EFCore.Example.Data;

namespace Atomizer.EFCore.Example.Handlers;

public record AssignStock(Guid ProductId, int Quantity);

public class AssignStockJobHandler : IAtomizerJobHandler<AssignStock>
{
    private readonly ExampleDbContext _dbContext;
    private readonly ILogger<AssignStockJobHandler> _logger;

    public AssignStockJobHandler(ExampleDbContext dbContext, ILogger<AssignStockJobHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task HandleAsync(AssignStock payload, JobContext context)
    {
        _logger.LogInformation(
            "Assigning stock for ProductId: {ProductId}, Quantity: {Quantity}",
            payload.ProductId,
            payload.Quantity
        );

        // Simulate stock assignment logic
        var product = await _dbContext.Products.FindAsync(payload.ProductId, context.CancellationToken);
        if (product == null)
        {
            _logger.LogWarning("Product with ID {ProductId} not found", payload.ProductId);
            throw new Exception($"Product with ID {payload.ProductId} not found.");
        }

        product.Quantity += payload.Quantity;
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Stock assigned successfully for ProductId: {ProductId}. New Stock: {NewStock}",
            payload.ProductId,
            product.Quantity
        );
    }
}
