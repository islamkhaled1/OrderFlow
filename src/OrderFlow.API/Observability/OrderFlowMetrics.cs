using System.Diagnostics.Metrics;

namespace OrderFlow.API.Observability;

/// <summary>
/// Custom application metrics for OrderFlow.
/// Uses System.Diagnostics.Metrics which integrates with OpenTelemetry.
/// </summary>
public sealed class OrderFlowMetrics
{
    public const string MeterName = "OrderFlow";

    private readonly Counter<long> _httpRequestCounter;
    private readonly Counter<long> _ordersCreatedCounter;
    private readonly Counter<long> _httpErrorsCounter;
    private readonly Counter<long> _idempotentReplaysCounter;
    private readonly Counter<long> _orderTransitionsCounter;

    public OrderFlowMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _httpRequestCounter = meter.CreateCounter<long>(
            "orderflow.http.requests",
            unit: "{requests}",
            description: "Total number of HTTP requests received");

        _ordersCreatedCounter = meter.CreateCounter<long>(
            "orderflow.orders.created",
            unit: "{orders}",
            description: "Total number of orders created");

        _httpErrorsCounter = meter.CreateCounter<long>(
            "orderflow.http.errors",
            unit: "{errors}",
            description: "Total number of HTTP error responses");

        _idempotentReplaysCounter = meter.CreateCounter<long>(
            "orderflow.idempotency.replays",
            unit: "{requests}",
            description: "Order creations answered from an idempotency record");

        _orderTransitionsCounter = meter.CreateCounter<long>(
            "orderflow.orders.transitions",
            unit: "{orders}",
            description: "Order state transitions requested through the API");
    }

    public void RecordHttpRequest(string method, string route, int statusCode) =>
        _httpRequestCounter.Add(1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("http.route", route),
            new KeyValuePair<string, object?>("http.response.status_code", statusCode));

    public void RecordOrderCreated() => _ordersCreatedCounter.Add(1);

    public void RecordIdempotentReplay() => _idempotentReplaysCounter.Add(1);

    public void RecordOrderTransition(string to) =>
        _orderTransitionsCounter.Add(1, new KeyValuePair<string, object?>("to", to));

    public void RecordHttpError(int statusCode, string method, string route) =>
        _httpErrorsCounter.Add(1,
            new KeyValuePair<string, object?>("http.response.status_code", statusCode),
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("http.route", route));
}
