using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly OrderFlowDbContext _context;

    public ProductRepository(OrderFlowDbContext context)
    {
        _context = context;
    }

    public async Task<List<Product>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return await _context.Products
            .AsNoTracking()
            .Where(p => idList.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryReserveStockAsync(int productId, int quantity, CancellationToken cancellationToken = default)
    {
        // Single atomic statement: UPDATE ... SET Stock = Stock - @q WHERE Stock >= @q AND IsActive = 1.
        // SQL Server serializes concurrent updates of the row, so two requests can never both consume
        // the last unit, and the CHECK constraint is a second line of defence.
        var affected = await _context.Products
            .Where(p => p.Id == productId && p.IsActive && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity), cancellationToken);

        return affected == 1;
    }

    public async Task ReleaseStockAsync(int productId, int quantity, CancellationToken cancellationToken = default)
    {
        await _context.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity), cancellationToken);
    }
}
