namespace OrderFlow.Application.Interfaces;

public interface IOrderDashboardRefreshService
{
    Task RefreshDashboardAsync(CancellationToken cancellationToken = default);
}
