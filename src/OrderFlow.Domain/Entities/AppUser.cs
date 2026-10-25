namespace OrderFlow.Domain.Entities;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

/// <summary>
/// Authentication principal. Passwords are stored only as salted hashes produced by the ASP.NET Core
/// password hasher. A user with the Customer role is mapped 1:1 to a <see cref="Customer"/>; the
/// order-owning identity is always derived from this mapping, never from client input.
/// </summary>
public class AppUser
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = Roles.Customer;
    public int? CustomerId { get; private set; }
    public Customer? Customer { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // For EF Core
    private AppUser() { }

    public static AppUser CreateCustomerUser(string email, string passwordHash, Customer customer)
    {
        var user = Create(email, passwordHash, Roles.Customer, customer.Id == 0 ? null : customer.Id);
        user.Customer = customer;
        return user;
    }

    public static AppUser CreateAdminUser(string email, string passwordHash) =>
        Create(email, passwordHash, Roles.Admin, null);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static AppUser Create(string email, string passwordHash, string role, int? customerId)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        return new AppUser
        {
            Id = Guid.NewGuid(),
            Email = NormalizeEmail(email),
            PasswordHash = passwordHash,
            Role = role,
            CustomerId = customerId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
