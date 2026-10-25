using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.API.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumSigningKeyLength = 32;

    public string Issuer { get; set; } = "OrderFlow";
    public string Audience { get; set; } = "OrderFlow.Clients";

    /// <summary>HMAC signing key. NEVER committed: supply via user-secrets or the Jwt__SigningKey environment variable.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
}

public static class OrderFlowClaims
{
    public const string CustomerId = "customer_id";
    public const string Role = "role";
    public const string Subject = "sub";
}

public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public AuthToken Create(AppUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(OrderFlowClaims.Role, user.Role)
        };
        if (user.CustomerId.HasValue)
            claims.Add(new Claim(OrderFlowClaims.CustomerId, user.CustomerId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/// <summary>Resolves the caller from the validated JWT principal (never from request payloads).</summary>
public class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(OrderFlowClaims.Subject), out var id)
            ? id : null;

    public int? CustomerId =>
        int.TryParse(Principal?.FindFirstValue(OrderFlowClaims.CustomerId), out var id) ? id : null;

    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) == true;
}
