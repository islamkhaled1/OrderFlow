using Microsoft.AspNetCore.Identity;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Infrastructure.Security;

/// <summary>
/// Password hashing via the ASP.NET Core Identity <see cref="PasswordHasher{TUser}"/>
/// (PBKDF2-HMAC-SHA512, per-password random salt, versioned format, iteration count upgrades on verify).
/// </summary>
public class IdentityPasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string passwordHash, string password)
    {
        var result = _hasher.VerifyHashedPassword(null!, passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
