using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Interfaces;

public interface IProductRepository
{
    Task<List<Product>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically decrements stock only if the product is active and has at least
    /// <paramref name="quantity"/> units (single conditional UPDATE). Returns false and changes nothing otherwise.
    /// </summary>
    Task<bool> TryReserveStockAsync(int productId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Atomically returns previously reserved units to stock.</summary>
    Task ReleaseStockAsync(int productId, int quantity, CancellationToken cancellationToken = default);
}
