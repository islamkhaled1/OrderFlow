namespace OrderFlow.Infrastructure.ReadModels;

/// <summary>
/// =========================================================================
/// READ MODEL ONLY — NOT THE SOURCE OF TRUTH
/// =========================================================================
/// This entity maps to the 'OrderDashboardReadModels' database table.
/// It represents a SQL-based Materialized View optimized specifically for
/// dashboard reporting queries.
/// 
/// Transactional tables (Customers, Products, Orders, OrderItems) remain the
/// sole source of truth for all business operations.
/// =========================================================================
/// </summary>
public class OrderDashboardReadModel
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
