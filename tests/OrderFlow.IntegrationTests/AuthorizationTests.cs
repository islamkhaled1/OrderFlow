using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using Xunit;

namespace OrderFlow.IntegrationTests;

public class AuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("GET", "/api/orders")]
    [InlineData("GET", "/api/orders/1")]
    [InlineData("POST", "/api/orders")]
    [InlineData("POST", "/api/orders/1/cancel")]
    [InlineData("GET", "/api/dashboard/orders")]
    [InlineData("GET", "/api/admin/outbox/dead-letters")]
    public async Task Anonymous_requests_to_protected_endpoints_return_401(string method, string url)
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Customer_cannot_use_admin_endpoints()
    {
        var customer = await _factory.RegisterCustomerAsync();

        (await customer.Client.GetAsync("/api/dashboard/orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.Client.GetAsync("/api/admin/outbox/dead-letters")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.Client.PostAsync("/api/admin/dashboard/rebuild", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.Client.PostAsync("/api/orders/1/complete", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_can_use_admin_endpoints()
    {
        var admin = await _factory.AdminClientAsync();

        (await admin.GetAsync("/api/dashboard/orders")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/outbox/dead-letters")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_account_cannot_place_orders()
    {
        var admin = await _factory.AdminClientAsync();
        var product = await _factory.CreateProductAsync(5);

        (await admin.PlaceOrderAsync(product, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.StockOfAsync(product)).Should().Be(5);
    }

    [Fact]
    public async Task Login_with_wrong_password_or_unknown_email_returns_the_same_401()
    {
        var customer = await _factory.RegisterCustomerAsync();
        var client = _factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(customer.Email, "not-the-password1"));
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@orderflow.test", "not-the-password1"));

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrongPassword.Content.ReadAsStringAsync()).Should().Be(await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registration_validates_input_and_rejects_duplicates_and_never_returns_admin()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("not-an-email", "Passw0rd-for-tests", "X")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("a@b.test", "short1", "X")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var customer = await _factory.RegisterCustomerAsync();
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(customer.Email, "Passw0rd-for-tests", "Dup"));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // A role field in the payload is ignored: the new account is always a Customer.
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = $"x-{Guid.NewGuid():N}@orderflow.test", password = "Passw0rd-for-tests", name = "Sneaky", role = "Admin" });
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        auth.Role.Should().Be("Customer");
    }

    [Fact]
    public async Task Customer_cannot_read_or_cancel_another_customers_order_even_when_it_is_cached()
    {
        var product = await _factory.CreateProductAsync(10);
        var alice = await _factory.RegisterCustomerAsync("Alice");
        var bob = await _factory.RegisterCustomerAsync("Bob");

        var order = await alice.Client.PlaceOrderOkAsync(product, 2);

        // Alice reads it: now it is in the (shared) cache.
        (await alice.Client.GetAsync($"/api/orders/{order.OrderId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Cache.Contains($"order:{order.OrderId}").Should().BeTrue();

        // Bob must not get it from the cache; the response is 404 so ids cannot be enumerated.
        (await bob.Client.GetAsync($"/api/orders/{order.OrderId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.Client.PostAsync($"/api/orders/{order.OrderId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.StockOfAsync(product)).Should().Be(8, "Bob's failed cancel must not release Alice's stock");

        // An admin can read any order.
        var admin = await _factory.AdminClientAsync();
        (await admin.GetAsync($"/api/orders/{order.OrderId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Order_list_is_scoped_to_the_caller_and_admin_sees_everything()
    {
        var product = await _factory.CreateProductAsync(100);
        var alice = await _factory.RegisterCustomerAsync();
        var bob = await _factory.RegisterCustomerAsync();
        await alice.Client.PlaceOrderOkAsync(product, 1);
        await alice.Client.PlaceOrderOkAsync(product, 1);
        await bob.Client.PlaceOrderOkAsync(product, 1);

        var aliceOrders = (await alice.Client.GetFromJsonAsync<PagedResult<OrderListItemDto>>("/api/orders"))!;
        aliceOrders.TotalCount.Should().Be(2);
        aliceOrders.Items.Should().OnlyContain(o => o.CustomerId == alice.CustomerId);

        // customerId filter is ignored for non-admins.
        var spoofed = (await alice.Client.GetFromJsonAsync<PagedResult<OrderListItemDto>>($"/api/orders?customerId={bob.CustomerId}"))!;
        spoofed.Items.Should().OnlyContain(o => o.CustomerId == alice.CustomerId);

        var admin = await _factory.AdminClientAsync();
        var forBob = (await admin.GetFromJsonAsync<PagedResult<OrderListItemDto>>($"/api/orders?customerId={bob.CustomerId}"))!;
        forBob.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Client_supplied_customer_id_is_ignored_and_the_token_identity_owns_the_order()
    {
        var product = await _factory.CreateProductAsync(10);
        var alice = await _factory.RegisterCustomerAsync();
        var bob = await _factory.RegisterCustomerAsync();

        var response = await alice.Client.PostAsJsonAsync("/api/orders",
            new { customerId = bob.CustomerId, items = new[] { new { productId = product, quantity = 1 } } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await response.Content.ReadFromJsonAsync<CreateOrderResponse>())!;
        created.CustomerId.Should().Be(alice.CustomerId);
    }
}
