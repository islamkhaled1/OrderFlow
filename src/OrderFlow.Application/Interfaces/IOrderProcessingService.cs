namespace OrderFlow.Application.Interfaces;

public interface IOrderProcessingService
{
    Task<List<int>> ProcessPendingOrdersAsync(CancellationToken cancellationToken = default);
}
