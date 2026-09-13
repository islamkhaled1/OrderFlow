using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrderFlow.Application.DTOs;
using Xunit;

namespace OrderFlow.IntegrationTests.Controllers;

public class OrdersControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public OrdersControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_WithValidPayload_Returns201CreatedAndCalculatesTotal()
    {
        // Arrange
        var request = new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<OrderItemRequestDto>
            {
                new(ProductId: 1, Quantity: 2), // 2 * 100 = 200
                new(ProductId: 2, Quantity: 1)  // 1 * 50 = 50
            }
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOrder = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        createdOrder.Should().NotBeNull();
        createdOrder!.OrderId.Should().BeGreaterThan(0);
        createdOrder.CustomerId.Should().Be(1);
        createdOrder.Total.Should().Be(250m);
        createdOrder.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task CreateOrder_WithNonExistentCustomer_Returns404NotFound()
    {
        // Arrange
        var request = new CreateOrderRequest(
            CustomerId: 9999,
            Items: new List<OrderItemRequestDto>
            {
                new(ProductId: 1, Quantity: 1)
            }
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateOrder_WithEmptyItems_Returns400BadRequest()
    {
        // Arrange
        var request = new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<OrderItemRequestDto>()
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOrderById_Returns200Ok_WithOrderDetailsAndSnapshot()
    {
        // Arrange: Create an order first
        var createRequest = new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<OrderItemRequestDto>
            {
                new(ProductId: 1, Quantity: 1)
            }
        );
        var createResponse = await _client.PostAsJsonAsync("/api/orders", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();

        // Act
        var response = await _client.GetAsync($"/api/orders/{created!.OrderId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = await response.Content.ReadFromJsonAsync<OrderDetailsDto>();
        details.Should().NotBeNull();
        details!.OrderId.Should().Be(created.OrderId);
        details.CustomerName.Should().Be("Test Customer 1");
        details.Items.Should().HaveCount(1);
        details.Items[0].ProductName.Should().Be("Test Keyboard");
        details.Items[0].Quantity.Should().Be(1);
        details.Items[0].UnitPrice.Should().Be(100m);
        details.Items[0].LineTotal.Should().Be(100m);
    }

    [Fact]
    public async Task GetOrderById_NonExistentId_Returns404NotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/orders/999999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAllOrders_Returns200Ok_WithOrdersList()
    {
        // Act
        var response = await _client.GetAsync("/api/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var orders = await response.Content.ReadFromJsonAsync<List<OrderListItemDto>>();
        orders.Should().NotBeNull();
        orders!.Count.Should().BeGreaterThan(0);
    }
}
