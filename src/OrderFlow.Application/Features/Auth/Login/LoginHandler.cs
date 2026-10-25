using FluentValidation;
using MediatR;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Features.Auth.Login;

public record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public class LoginHandler : IRequestHandler<LoginCommand, AuthResponse>
{
    private const string InvalidCredentials = "Invalid email or password.";

    private readonly IUserRepository _users;
    private readonly IPasswordHasherService _hasher;
    private readonly ITokenService _tokens;

    public LoginHandler(IUserRepository users, IPasswordHasherService hasher, ITokenService tokens)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
    }

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(AppUser.NormalizeEmail(request.Email), cancellationToken);

        // Same message for unknown email and wrong password to avoid account enumeration.
        if (user is null || !_hasher.Verify(user.PasswordHash, request.Password))
            throw new UnauthorizedException(InvalidCredentials);

        var token = _tokens.Create(user);
        return new AuthResponse(token.AccessToken, token.ExpiresAtUtc, user.Role, user.CustomerId);
    }
}
