using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Interfaces;

public interface IProductRepository
{
    Task<List<Product>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
}
