using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrderFlow.Application.DTOs;
using Xunit;

namespace OrderFlow.IntegrationTests.Controllers;

public class DashboardControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DashboardControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDashboardOrders_Returns200Ok_FromReadModel()
    {
        // Act
        var response = await _client.GetAsync("/api/dashboard/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboardItems = await response.Content.ReadFromJsonAsync<List<OrderDashboardDto>>();
        dashboardItems.Should().NotBeNull();
        dashboardItems!.Count.Should().BeGreaterThan(0);

        var firstItem = dashboardItems[0];
        firstItem.OrderId.Should().BeGreaterThan(0);
        firstItem.CustomerName.Should().NotBeNullOrWhiteSpace();
        firstItem.ItemCount.Should().BeGreaterThan(0);
        firstItem.Total.Should().BeGreaterThan(0);
        firstItem.Status.Should().NotBeNullOrWhiteSpace();
    }
}
