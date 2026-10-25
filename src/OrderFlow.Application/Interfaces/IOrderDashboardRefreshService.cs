namespace OrderFlow.Application.Interfaces;

public interface IOrderDashboardRefreshService
{
    /// <summary>Recomputes the read-model row of a single order from the transactional tables (idempotent).</summary>
    Task UpsertOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Full rebuild/reconciliation of the read model from the transactional tables.</summary>
    Task RefreshDashboardAsync(CancellationToken cancellationToken = default);
}
