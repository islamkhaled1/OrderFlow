using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.DTOs;
using OrderFlow.Domain.Entities;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.IntegrationTests;

public sealed record TestCustomer(HttpClient Client, int CustomerId, string Email);

public static class TestHelpers
{
    public const string Password = "Passw0rd-for-tests";

    public static async Task<TestCustomer> RegisterCustomerAsync(this CustomWebApplicationFactory factory, string? name = null)
    {
        var email = $"user-{Guid.NewGuid():N}@orderflow.test";
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, name ?? "Test User"));
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new TestCustomer(client, auth.CustomerId!.Value, email);
    }

    public static async Task<HttpClient> AdminClientAsync(this CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(CustomWebApplicationFactory.AdminEmail, CustomWebApplicationFactory.AdminPassword));
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    public static async Task<T> WithDbAsync<T>(this CustomWebApplicationFactory factory, Func<OrderFlowDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>());
    }

    public static async Task<int> CreateProductAsync(
        this CustomWebApplicationFactory factory, int stock, decimal price = 10m, string? name = null, bool active = true)
    {
        return await factory.WithDbAsync(async db =>
        {
            var product = new Product(name ?? "Test product " + Guid.NewGuid().ToString("N")[..8], price, stock) { IsActive = active };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return product.Id;
        });
    }

    public static Task<int> StockOfAsync(this CustomWebApplicationFactory factory, int productId) =>
        factory.WithDbAsync(db => db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());

    public static Task<HttpResponseMessage> PlaceOrderAsync(
        this HttpClient client, int productId, int quantity, string? idempotencyKey = null)
        => client.PlaceOrderAsync(new[] { (productId, quantity) }, idempotencyKey);

    public static Task<HttpResponseMessage> PlaceOrderAsync(
        this HttpClient client, IEnumerable<(int ProductId, int Quantity)> lines, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(lines.Select(l => new OrderItemRequestDto(l.ProductId, l.Quantity)).ToList()))
        };
        if (idempotencyKey != null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    public static async Task<CreateOrderResponse> PlaceOrderOkAsync(this HttpClient client, int productId, int quantity, string? key = null)
    {
        var response = await client.PlaceOrderAsync(productId, quantity, key);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateOrderResponse>())!;
    }
}
