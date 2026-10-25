using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Outbox;
using OrderFlow.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderFlow.IntegrationTests;

public class OutboxAndReadModelTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OutboxAndReadModelTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Cache.IsAvailable = true;
    }

    private async Task<int> RunOutboxAsync()
    {
        var total = 0;
        for (var i = 0; i < 20; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var handled = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
            if (handled == 0) break;
            total += handled;
        }
        return total;
    }

    private Task<OutboxMessage> MessageForAsync(int orderId, string eventType) =>
        _factory.WithDbAsync(db => db.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.EventType == eventType && m.Payload.Contains($"\"orderId\":{orderId},")));

    private Task<string?> DashboardStatusAsync(int orderId) =>
        _factory.WithDbAsync(db => db.OrderDashboardReadModels.AsNoTracking()
            .Where(r => r.OrderId == orderId).Select(r => r.Status).SingleOrDefaultAsync());

    [Fact]
    public async Task Dashboard_follows_order_creation_completion_and_cancellation_through_the_outbox()
    {
        var product = await _factory.CreateProductAsync(10, 25m);
        var customer = await _factory.RegisterCustomerAsync("Dash Customer");
        var admin = await _factory.AdminClientAsync();

        var toComplete = await customer.Client.PlaceOrderOkAsync(product, 2);
        var toCancel = await customer.Client.PlaceOrderOkAsync(product, 1);

        // Eventually consistent: nothing in the read model until the outbox runs, but the message is already durable.
        (await DashboardStatusAsync(toComplete.OrderId)).Should().BeNull();
        (await MessageForAsync(toComplete.OrderId, "OrderCreated")).ProcessedAtUtc.Should().BeNull();

        await RunOutboxAsync();
        (await DashboardStatusAsync(toComplete.OrderId)).Should().Be("Pending");

        (await admin.PostAsync($"/api/orders/{toComplete.OrderId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await customer.Client.PostAsync($"/api/orders/{toCancel.OrderId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await RunOutboxAsync();

        (await DashboardStatusAsync(toComplete.OrderId)).Should().Be("Completed");
        (await DashboardStatusAsync(toCancel.OrderId)).Should().Be("Cancelled");

        var page = (await admin.GetFromJsonAsync<PagedResult<OrderDashboardDto>>("/api/dashboard/orders?status=Cancelled&pageSize=100"))!;
        page.Items.Should().Contain(r => r.OrderId == toCancel.OrderId && r.CustomerName == "Dash Customer" && r.ItemCount == 1);
        page.Items.Should().OnlyContain(r => r.Status == "Cancelled");
    }

    [Fact]
    public async Task Redelivered_messages_are_idempotent()
    {
        var product = await _factory.CreateProductAsync(10, 25m);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 2);
        await RunOutboxAsync();

        // Simulate a crash after side effects but before the message was marked processed -> redelivery.
        await _factory.WithDbAsync(async db => await db.OutboxMessages
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, (DateTime?)null)));
        (await RunOutboxAsync()).Should().BeGreaterThan(0);
        await _factory.WithDbAsync(async db => await db.OutboxMessages
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, (DateTime?)null)));
        await RunOutboxAsync();

        (await _factory.WithDbAsync(db => db.OrderDashboardReadModels.CountAsync(r => r.OrderId == order.OrderId))).Should().Be(1);
        (await DashboardStatusAsync(order.OrderId)).Should().Be("Pending");
    }

    [Fact]
    public async Task Cache_outage_causes_bounded_retries_then_recovery_processes_the_message()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);

        _factory.Cache.IsAvailable = false;
        await RunOutboxAsync();

        var failed = await MessageForAsync(order.OrderId, "OrderCreated");
        failed.ProcessedAtUtc.Should().BeNull();
        failed.Attempts.Should().Be(1);
        failed.LastError.Should().Contain("Cache invalidation");
        failed.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow, "backoff delays the retry");
        failed.DeadLetteredAtUtc.Should().BeNull();
        failed.LockedBy.Should().BeNull();

        // Not claimable before its backoff expires.
        (await RunOutboxAsync()).Should().Be(0);

        // Redis recovers and the backoff elapses.
        _factory.Cache.IsAvailable = true;
        await _factory.WithDbAsync(async db => await db.OutboxMessages.Where(m => m.Id == failed.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1))));
        (await RunOutboxAsync()).Should().Be(1);

        var done = await MessageForAsync(order.OrderId, "OrderCreated");
        done.ProcessedAtUtc.Should().NotBeNull();
        done.LastError.Should().BeNull();
        (await DashboardStatusAsync(order.OrderId)).Should().Be("Pending");
    }

    [Fact]
    public async Task Repeatedly_failing_message_is_dead_lettered_and_can_be_investigated_and_requeued()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var admin = await _factory.AdminClientAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);
        var message = await MessageForAsync(order.OrderId, "OrderCreated");

        int maxAttempts;
        using (var scope = _factory.Services.CreateScope())
            maxAttempts = scope.ServiceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value.MaxAttempts;

        _factory.Cache.IsAvailable = false;
        await _factory.WithDbAsync(async db => await db.OutboxMessages.Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Attempts, maxAttempts - 1)));
        await RunOutboxAsync();

        var dead = await MessageForAsync(order.OrderId, "OrderCreated");
        dead.DeadLetteredAtUtc.Should().NotBeNull();
        dead.Attempts.Should().Be(maxAttempts);
        (await RunOutboxAsync()).Should().Be(0, "dead letters are not retried automatically");

        var listed = (await admin.GetFromJsonAsync<List<DeadLetterDto>>("/api/admin/outbox/dead-letters"))!;
        listed.Should().Contain(d => d.Id == message.Id && d.LastError != null);

        _factory.Cache.IsAvailable = true;
        (await admin.PostAsync($"/api/admin/outbox/dead-letters/{message.Id}/requeue", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RunOutboxAsync()).Should().BeGreaterThan(0);
        (await MessageForAsync(order.OrderId, "OrderCreated")).ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Concurrent_processors_never_process_a_message_twice()
    {
        var product = await _factory.CreateProductAsync(1000);
        var customer = await _factory.RegisterCustomerAsync();
        const int orders = 40;
        for (var i = 0; i < orders; i++) await customer.Client.PlaceOrderOkAsync(product, 1);
        await RunOutboxAsync(); // drain anything older than this test's messages

        for (var i = 0; i < orders; i++) await customer.Client.PlaceOrderOkAsync(product, 1);
        _factory.Cache.IsAvailable = true;
        var removesBefore = _factory.Cache.Removes;

        var handled = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            var count = 0;
            for (var i = 0; i < 10; i++)
            {
                using var scope = _factory.Services.CreateScope();
                count += await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
            }
            return count;
        }));

        handled.Sum().Should().Be(orders);
        (_factory.Cache.Removes - removesBefore).Should().Be(orders, "each message is claimed by exactly one worker");
        (await _factory.WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAtUtc == null && m.DeadLetteredAtUtc == null && m.Attempts == 0)))
            .Should().Be(0);
    }

    [Fact]
    public async Task Claims_respect_leases_so_a_crashed_worker_does_not_lose_messages()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);
        var message = await MessageForAsync(order.OrderId, "OrderCreated");

        // Another worker holds a live lease: not claimable.
        await _factory.WithDbAsync(async db => await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.LockedBy, Guid.NewGuid()).SetProperty(m => m.LockedUntilUtc, DateTime.UtcNow.AddMinutes(5))));
        await RunOutboxAsync();
        (await MessageForAsync(order.OrderId, "OrderCreated")).ProcessedAtUtc.Should().BeNull();

        // That worker "crashed": its lease expired, so the message is picked up again after restart.
        await _factory.WithDbAsync(async db => await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.LockedUntilUtc, DateTime.UtcNow.AddMinutes(-1))));
        await RunOutboxAsync();
        (await MessageForAsync(order.OrderId, "OrderCreated")).ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Stale_pending_orders_expire_and_release_stock_exactly_once()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var stale = await customer.Client.PlaceOrderOkAsync(product, 3);
        var fresh = await customer.Client.PlaceOrderOkAsync(product, 1);
        (await _factory.StockOfAsync(product)).Should().Be(6);

        await _factory.WithDbAsync(async db => await db.Orders.Where(o => o.Id == stale.OrderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, DateTime.UtcNow.AddHours(-3))));

        int expired;
        using (var scope = _factory.Services.CreateScope())
            expired = await scope.ServiceProvider.GetRequiredService<IOrderExpirationService>()
                .ExpireStaleOrdersAsync(DateTime.UtcNow.AddHours(-1), 100, CancellationToken.None);
        expired.Should().Be(1);

        (await _factory.StockOfAsync(product)).Should().Be(9);
        (await _factory.WithDbAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == stale.OrderId).Select(o => o.Status).SingleAsync()))
            .Should().Be(OrderStatus.Cancelled);
        (await _factory.WithDbAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == stale.OrderId).Select(o => o.CancellationReason).SingleAsync()))
            .Should().StartWith("Expired");
        (await _factory.WithDbAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == fresh.OrderId).Select(o => o.Status).SingleAsync()))
            .Should().Be(OrderStatus.Pending);

        // Running it again must not release again.
        using (var scope = _factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IOrderExpirationService>()
                .ExpireStaleOrdersAsync(DateTime.UtcNow.AddHours(-1), 100, CancellationToken.None)).Should().Be(0);
        (await _factory.StockOfAsync(product)).Should().Be(9);
    }

    [Fact]
    public async Task Invalid_transitions_are_rejected_with_409_and_do_not_touch_inventory()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var admin = await _factory.AdminClientAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 2);

        (await admin.PostAsync($"/api/orders/{order.OrderId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await customer.Client.PostAsync($"/api/orders/{order.OrderId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/orders/{order.OrderId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.StockOfAsync(product)).Should().Be(8, "a completed order keeps its stock deducted");

        (await admin.PostAsync("/api/orders/999999/complete", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Orders_are_never_completed_automatically()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);
        await RunOutboxAsync();

        var details = (await customer.Client.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{order.OrderId}"))!;
        details.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Dashboard_rebuild_reconciles_a_corrupted_read_model()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();
        var admin = await _factory.AdminClientAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);
        await RunOutboxAsync();

        await _factory.WithDbAsync(async db => await db.OrderDashboardReadModels
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Bogus")));
        (await admin.PostAsync("/api/admin/dashboard/rebuild", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await DashboardStatusAsync(order.OrderId)).Should().Be("Pending");
    }
}
