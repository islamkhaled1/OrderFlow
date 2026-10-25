using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.DTOs;
using OrderFlow.Domain.Enums;
using Xunit;

namespace OrderFlow.IntegrationTests;

/// <summary>
/// Real concurrency against SQL Server. Every test asserts the resulting DATABASE state, not just HTTP codes.
/// </summary>
public class ConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConcurrencyTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<List<HttpClient>> CustomersAsync(int count)
    {
        var customers = new List<HttpClient>();
        for (var i = 0; i < count; i++)
            customers.Add((await _factory.RegisterCustomerAsync()).Client);
        return customers;
    }

    [Fact]
    public async Task One_hundred_concurrent_orders_for_ten_units_sell_exactly_ten()
    {
        const int stock = 10, requests = 100;
        var product = await _factory.CreateProductAsync(stock);
        var clients = await CustomersAsync(10);

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, requests).Select(async i =>
        {
            await start.Task;
            return (await clients[i % clients.Count].PlaceOrderAsync(product, 1)).StatusCode;
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r == HttpStatusCode.Created).Should().Be(stock);
        results.Count(r => r == HttpStatusCode.Conflict).Should().Be(requests - stock);
        results.Should().OnlyContain(r => r == HttpStatusCode.Created || r == HttpStatusCode.Conflict);

        (await _factory.StockOfAsync(product)).Should().Be(0, "stock must never go negative and every unit must be sold");

        var (orders, units) = await _factory.WithDbAsync(async db => (
            await db.OrderItems.CountAsync(i => i.ProductId == product),
            await db.OrderItems.Where(i => i.ProductId == product).SumAsync(i => i.Quantity)));
        orders.Should().Be(stock);
        units.Should().Be(stock, "successful reservations must not exceed available stock");
    }

    [Fact]
    public async Task Concurrent_multi_unit_orders_never_oversell_and_failures_do_not_reduce_stock()
    {
        const int stock = 10;
        var product = await _factory.CreateProductAsync(stock);
        var clients = await CustomersAsync(5);

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            await start.Task;
            return (await clients[i % clients.Count].PlaceOrderAsync(product, 3)).StatusCode;
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        var sold = results.Count(r => r == HttpStatusCode.Created);
        sold.Should().Be(3); // floor(10 / 3)
        (await _factory.StockOfAsync(product)).Should().Be(stock - sold * 3);
        (await _factory.StockOfAsync(product)).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Concurrent_orders_over_several_products_in_different_line_orders_do_not_deadlock_or_oversell()
    {
        var a = await _factory.CreateProductAsync(15);
        var b = await _factory.CreateProductAsync(15);
        var clients = await CustomersAsync(6);

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 30).Select(async i =>
        {
            await start.Task;
            var lines = i % 2 == 0 ? new[] { (a, 1), (b, 1) } : new[] { (b, 1), (a, 1) };
            return (await clients[i % clients.Count].PlaceOrderAsync(lines)).StatusCode;
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r == HttpStatusCode.Created).Should().Be(15);
        results.Should().OnlyContain(r => r == HttpStatusCode.Created || r == HttpStatusCode.Conflict);
        (await _factory.StockOfAsync(a)).Should().Be(0);
        (await _factory.StockOfAsync(b)).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_cancellations_of_one_order_release_stock_exactly_once()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 4);
        (await _factory.StockOfAsync(product)).Should().Be(6);

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await start.Task;
            return (await customer.Client.PostAsync($"/api/orders/{order.OrderId}/cancel", null)).StatusCode;
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r == HttpStatusCode.NoContent).Should().Be(1);
        results.Where(r => r != HttpStatusCode.NoContent).Should().OnlyContain(r => r == HttpStatusCode.Conflict);
        (await _factory.StockOfAsync(product)).Should().Be(10, "stock is released once: 6 + 4, never 6 + 4 * n");
    }

    [Fact]
    public async Task Cancel_and_complete_racing_leave_a_consistent_order_and_inventory()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var admin = await _factory.AdminClientAsync();

        for (var round = 0; round < 5; round++)
        {
            var order = await customer.Client.PlaceOrderOkAsync(product, 1);
            var before = await _factory.StockOfAsync(product);

            var start = new TaskCompletionSource();
            var cancel = Task.Run(async () => { await start.Task; return await customer.Client.PostAsync($"/api/orders/{order.OrderId}/cancel", null); });
            var complete = Task.Run(async () => { await start.Task; return await admin.PostAsync($"/api/orders/{order.OrderId}/complete", null); });
            start.SetResult();
            var responses = await Task.WhenAll(cancel, complete);

            responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1, "exactly one transition wins");

            var status = await _factory.WithDbAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == order.OrderId).Select(o => o.Status).SingleAsync());
            var stock = await _factory.StockOfAsync(product);
            if (status == OrderStatus.Cancelled) stock.Should().Be(before + 1);
            else { status.Should().Be(OrderStatus.Completed); stock.Should().Be(before); }
        }
    }

    [Fact]
    public async Task Database_constraints_are_a_second_line_of_defence()
    {
        var product = await _factory.CreateProductAsync(1);

        var negativeStock = async () => await _factory.WithDbAsync(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET StockQuantity = -1 WHERE Id = {product}"));
        await negativeStock.Should().ThrowAsync<Exception>();

        (await _factory.StockOfAsync(product)).Should().Be(1);
    }
}

public class IdempotencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public IdempotencyTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Retry_with_same_key_and_payload_returns_the_original_order_without_side_effects()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var key = Guid.NewGuid().ToString();

        var first = await customer.Client.PlaceOrderAsync(product, 3, key);
        var retry = await customer.Client.PlaceOrderAsync(product, 3, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        retry.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle("true");

        var a = (await first.Content.ReadFromJsonAsync<CreateOrderResponse>())!;
        var b = (await retry.Content.ReadFromJsonAsync<CreateOrderResponse>())!;
        b.Should().BeEquivalentTo(a);

        (await _factory.StockOfAsync(product)).Should().Be(7, "stock is reserved once");
        (await _factory.WithDbAsync(db => db.Orders.CountAsync(o => o.CustomerId == customer.CustomerId))).Should().Be(1);
        (await _factory.WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.Payload.Contains($"\"orderId\":{a.OrderId},")))).Should().Be(1);
    }

    [Fact]
    public async Task Same_key_with_a_different_payload_is_rejected_with_422()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var key = Guid.NewGuid().ToString();

        (await customer.Client.PlaceOrderAsync(product, 1, key)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await customer.Client.PlaceOrderAsync(product, 2, key)).StatusCode.Should().Be((HttpStatusCode)422);

        (await _factory.StockOfAsync(product)).Should().Be(9);
    }

    [Fact]
    public async Task Reordered_or_split_lines_are_the_same_canonical_request()
    {
        var a = await _factory.CreateProductAsync(10);
        var b = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var key = Guid.NewGuid().ToString();

        var first = await customer.Client.PlaceOrderAsync(new[] { (a, 1), (b, 2) }, key);
        var retry = await customer.Client.PlaceOrderAsync(new[] { (b, 1), (a, 1), (b, 1) }, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        (await _factory.StockOfAsync(a)).Should().Be(9);
        (await _factory.StockOfAsync(b)).Should().Be(8);
    }

    [Fact]
    public async Task Twenty_simultaneous_requests_with_one_key_create_one_order()
    {
        var product = await _factory.CreateProductAsync(100);
        var customer = await _factory.RegisterCustomerAsync();
        var key = Guid.NewGuid().ToString();

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await start.Task;
            return await customer.Client.PlaceOrderAsync(product, 2, key);
        }).ToList();
        start.SetResult();
        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var ids = new List<int>();
        foreach (var r in responses) ids.Add((await r.Content.ReadFromJsonAsync<CreateOrderResponse>())!.OrderId);
        ids.Distinct().Should().ContainSingle();
        responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).Should().Be(1);

        (await _factory.StockOfAsync(product)).Should().Be(98);
        (await _factory.WithDbAsync(db => db.Orders.CountAsync(o => o.CustomerId == customer.CustomerId))).Should().Be(1);
        (await _factory.WithDbAsync(db => db.IdempotencyRecords.CountAsync(r => r.Key == key))).Should().Be(1);
    }

    [Fact]
    public async Task Keys_are_scoped_per_customer()
    {
        var product = await _factory.CreateProductAsync(10);
        var alice = await _factory.RegisterCustomerAsync();
        var bob = await _factory.RegisterCustomerAsync();
        var key = "shared-key-" + Guid.NewGuid();

        var a = await alice.Client.PlaceOrderOkAsync(product, 1, key);
        var b = await bob.Client.PlaceOrderOkAsync(product, 1, key);

        a.OrderId.Should().NotBe(b.OrderId);
        (await _factory.StockOfAsync(product)).Should().Be(8);
    }

    [Fact]
    public async Task A_failed_attempt_leaves_no_key_behind_so_the_retry_can_succeed()
    {
        var product = await _factory.CreateProductAsync(1);
        var customer = await _factory.RegisterCustomerAsync();
        var key = Guid.NewGuid().ToString();

        (await customer.Client.PlaceOrderAsync(product, 5, key)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.WithDbAsync(db => db.IdempotencyRecords.CountAsync(r => r.Key == key))).Should().Be(0);

        await _factory.WithDbAsync(async db =>
            await db.Products.Where(p => p.Id == product).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, 10)));

        (await customer.Client.PlaceOrderAsync(product, 5, key)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Overlong_key_is_rejected_and_the_unique_index_exists()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        (await customer.Client.PlaceOrderAsync(product, 1, new string('k', 129))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var key = Guid.NewGuid().ToString();
        await customer.Client.PlaceOrderOkAsync(product, 1, key);
        var duplicateInsert = async () => await _factory.WithDbAsync(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO IdempotencyRecords (Scope,[Key],RequestHash,CreatedAtUtc,ExpiresAtUtc) VALUES ({"customer:" + customer.CustomerId},{key},{new string('A', 64)},SYSUTCDATETIME(),SYSUTCDATETIME())"));
        await duplicateInsert.Should().ThrowAsync<Exception>("the database constraint enforces uniqueness");
    }
}
