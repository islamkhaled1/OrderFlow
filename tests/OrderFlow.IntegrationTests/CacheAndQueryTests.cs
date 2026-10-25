using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using Xunit;

namespace OrderFlow.IntegrationTests;

public class CacheBehaviourTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CacheBehaviourTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Cache.IsAvailable = true;
    }

    [Fact]
    public async Task Cold_read_misses_then_populates_the_cache_and_the_next_read_hits()
    {
        var product = await _factory.CreateProductAsync(5);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);
        _factory.Cache.ResetCounters();

        (await customer.Client.GetAsync($"/api/orders/{order.OrderId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Cache.Misses.Should().Be(1);
        _factory.Cache.Hits.Should().Be(0);

        (await customer.Client.GetAsync($"/api/orders/{order.OrderId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Cache.Hits.Should().Be(1);
    }

    [Fact]
    public async Task State_changes_invalidate_the_cached_order()
    {
        var product = await _factory.CreateProductAsync(5);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);

        (await customer.Client.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{order.OrderId}"))!.Status.Should().Be("Pending");
        _factory.Cache.Contains($"order:{order.OrderId}").Should().BeTrue();

        (await customer.Client.PostAsync($"/api/orders/{order.OrderId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Cache.Contains($"order:{order.OrderId}").Should().BeFalse();

        (await customer.Client.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{order.OrderId}"))!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Cache_outage_degrades_to_the_database_and_recovers()
    {
        var product = await _factory.CreateProductAsync(5);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);

        _factory.Cache.IsAvailable = false;
        try
        {
            // Reads and even writes keep working while Redis is down.
            (await customer.Client.GetAsync($"/api/orders/{order.OrderId}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await customer.Client.PlaceOrderAsync(product, 1)).StatusCode.Should().Be(HttpStatusCode.Created);
            (await customer.Client.PostAsync($"/api/orders/{order.OrderId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            _factory.Cache.IsAvailable = true;
        }

        // Recovery: the cache is used again and shows fresh data, not stale pre-outage data.
        var details = (await customer.Client.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{order.OrderId}"))!;
        details.Status.Should().Be("Cancelled");
        _factory.Cache.Contains($"order:{order.OrderId}").Should().BeTrue();
    }
}

public class QueryAndOperationsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public QueryAndOperationsTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=Bogus")]
    [InlineData("from=2030-01-02&to=2030-01-01")]
    public async Task Invalid_paging_or_filter_parameters_return_400(string query)
    {
        var customer = await _factory.RegisterCustomerAsync();
        (await customer.Client.GetAsync($"/api/orders?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Paging_is_stable_complete_and_filterable()
    {
        var product = await _factory.CreateProductAsync(100);
        var customer = await _factory.RegisterCustomerAsync();
        var ids = new List<int>();
        for (var i = 0; i < 7; i++) ids.Add((await customer.Client.PlaceOrderOkAsync(product, 1)).OrderId);
        (await customer.Client.PostAsync($"/api/orders/{ids[0]}/cancel", null)).EnsureSuccessStatusCode();

        // Force identical timestamps to prove the Id tiebreaker keeps pages disjoint.
        await _factory.WithDbAsync(async db =>
            await db.Orders.Where(o => o.CustomerId == customer.CustomerId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc))));

        var seen = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = (await customer.Client.GetFromJsonAsync<PagedResult<OrderListItemDto>>($"/api/orders?page={page}&pageSize=3"))!;
            result.TotalCount.Should().Be(7);
            result.TotalPages.Should().Be(3);
            result.Items.Count.Should().Be(page < 3 ? 3 : 1);
            seen.AddRange(result.Items.Select(i => i.OrderId));
        }

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ids);
        seen.Should().BeInDescendingOrder();

        var cancelled = (await customer.Client.GetFromJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?status=Cancelled"))!;
        cancelled.Items.Should().ContainSingle().Which.OrderId.Should().Be(ids[0]);
    }

    [Fact]
    public async Task Liveness_and_readiness_report_without_leaking_details()
    {
        var client = _factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        live.StatusCode.Should().Be(HttpStatusCode.OK);

        // Readiness depends on SQL Server only (Redis is optional), so it is healthy without Redis.
        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).Should().Be("Healthy");

        var full = await client.GetAsync("/health");
        var body = await full.Content.ReadAsStringAsync();
        body.Should().NotContain("Password", "connection details must never be exposed");
        body.Should().NotContain("Server=");
    }

    [Fact]
    public async Task Metrics_endpoint_exposes_business_and_infrastructure_metrics()
    {
        var product = await _factory.CreateProductAsync(5);
        var customer = await _factory.RegisterCustomerAsync();
        await customer.Client.PlaceOrderOkAsync(product, 1);

        var body = await _factory.CreateClient().GetStringAsync("/metrics");
        body.Should().Contain("orderflow_orders_created");
        body.Should().Contain("orderflow_outbox_pending");
        body.Should().Contain("orderflow_orders_pending");
    }

    [Fact]
    public async Task Database_matches_the_model_and_has_no_pending_migrations()
    {
        await _factory.CreateClient().GetAsync("/health/live");
        var (pending, hasModelChanges) = await _factory.WithDbAsync(async db =>
            ((await db.Database.GetPendingMigrationsAsync()).ToList(), db.Database.HasPendingModelChanges()));

        pending.Should().BeEmpty();
        hasModelChanges.Should().BeFalse("every model change must ship with a migration");
    }
}
