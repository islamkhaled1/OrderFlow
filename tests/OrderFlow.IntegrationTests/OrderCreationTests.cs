using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using Xunit;

namespace OrderFlow.IntegrationTests;

public class OrderCreationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OrderCreationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Valid_order_is_priced_on_the_server_and_reserves_stock_atomically()
    {
        var keyboard = await _factory.CreateProductAsync(10, 100m, "Keyboard X");
        var mouse = await _factory.CreateProductAsync(10, 50.55m, "Mouse X");
        var customer = await _factory.RegisterCustomerAsync();

        var response = await customer.Client.PlaceOrderAsync(new[] { (keyboard, 2), (mouse, 3) });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var created = (await response.Content.ReadFromJsonAsync<CreateOrderResponse>())!;
        created.Total.Should().Be(2 * 100m + 3 * 50.55m);
        created.Status.Should().Be("Pending");

        (await _factory.StockOfAsync(keyboard)).Should().Be(8);
        (await _factory.StockOfAsync(mouse)).Should().Be(7);

        var details = (await customer.Client.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{created.OrderId}"))!;
        details.Items.Should().HaveCount(2);
        details.Items.Single(i => i.ProductName == "Mouse X").LineTotal.Should().Be(151.65m);
    }

    [Fact]
    public async Task Order_keeps_a_price_and_name_snapshot_when_the_product_changes_later()
    {
        var product = await _factory.CreateProductAsync(10, 20m, "Original name");
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);

        await _factory.WithDbAsync(async db =>
        {
            await db.Products.Where(p => p.Id == product)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 999m).SetProperty(p => p.Name, "Renamed"));
            return 0;
        });

        var details = (await customer.Client.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{order.OrderId}"))!;
        details.Total.Should().Be(20m);
        details.Items.Single().UnitPrice.Should().Be(20m);
        details.Items.Single().ProductName.Should().Be("Original name");
    }

    [Fact]
    public async Task Duplicate_product_lines_are_merged_into_one_reservation()
    {
        var product = await _factory.CreateProductAsync(10, 5m);
        var customer = await _factory.RegisterCustomerAsync();

        var response = await customer.Client.PlaceOrderAsync(new[] { (product, 2), (product, 3) });
        var created = (await response.Content.ReadFromJsonAsync<CreateOrderResponse>())!;

        created.Total.Should().Be(25m);
        (await _factory.StockOfAsync(product)).Should().Be(5);
        var details = (await customer.Client.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{created.OrderId}"))!;
        details.Items.Should().ContainSingle().Which.Quantity.Should().Be(5);
    }

    [Fact]
    public async Task Invalid_payloads_return_400_and_change_nothing()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();

        (await customer.Client.PlaceOrderAsync(Array.Empty<(int, int)>())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await customer.Client.PlaceOrderAsync(product, 0)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await customer.Client.PlaceOrderAsync(product, -3)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await customer.Client.PlaceOrderAsync(0, 1)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await customer.Client.PlaceOrderAsync(product, 1_000_000)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _factory.StockOfAsync(product)).Should().Be(10);
    }

    [Fact]
    public async Task Unknown_product_returns_404_and_a_partially_reserved_order_is_rolled_back()
    {
        var product = await _factory.CreateProductAsync(10);
        var customer = await _factory.RegisterCustomerAsync();

        var response = await customer.Client.PlaceOrderAsync(new[] { (product, 2), (int.MaxValue - 1, 1) });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.StockOfAsync(product)).Should().Be(10);
    }

    [Fact]
    public async Task Insufficient_stock_returns_409_and_rolls_back_reservations_of_other_lines()
    {
        var plenty = await _factory.CreateProductAsync(10);
        var scarce = await _factory.CreateProductAsync(1);
        var customer = await _factory.RegisterCustomerAsync();

        // Lines are reserved in product-id order: 'plenty' is reserved first, then 'scarce' fails.
        var response = await customer.Client.PlaceOrderAsync(new[] { (plenty, 4), (scarce, 2) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.StockOfAsync(plenty)).Should().Be(10, "the first reservation must roll back with the failed order");
        (await _factory.StockOfAsync(scarce)).Should().Be(1);
        (await customer.Client.GetFromJsonAsync<PagedResult<OrderListItemDto>>("/api/orders"))!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Inactive_product_cannot_be_ordered()
    {
        var product = await _factory.CreateProductAsync(10, active: false);
        var customer = await _factory.RegisterCustomerAsync();

        (await customer.Client.PlaceOrderAsync(product, 1)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.StockOfAsync(product)).Should().Be(10);
    }

    [Fact]
    public async Task Order_and_outbox_message_are_committed_together()
    {
        var product = await _factory.CreateProductAsync(5);
        var customer = await _factory.RegisterCustomerAsync();
        var order = await customer.Client.PlaceOrderOkAsync(product, 1);

        var types = await _factory.WithDbAsync(db => db.OutboxMessages.AsNoTracking()
            .Where(m => m.Payload.Contains($"\"orderId\":{order.OrderId},"))
            .Select(m => m.EventType).ToListAsync());

        types.Should().ContainSingle().Which.Should().Be("OrderCreated");
    }
}
