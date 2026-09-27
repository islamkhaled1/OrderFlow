using System.Diagnostics;

namespace OrderFlow.API.Observability;

/// <summary>
/// ActivitySource for distributed tracing of OrderFlow operations.
/// </summary>
public static class OrderFlowActivitySource
{
    public const string Name = "OrderFlow";

    public static readonly ActivitySource Instance = new(Name, "1.0.0");
}
