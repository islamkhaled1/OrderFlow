using FluentValidation;
using MediatR;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Features.Auth.Register;

public record RegisterCommand(string Email, string Password, string Name) : IRequest<AuthResponse>;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128)
            .Must(p => p != null && p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("Password must contain at least one letter and one digit.");
    }
}

/// <summary>
/// Public registration only ever creates a Customer-role account. Administrators cannot be created here.
/// </summary>
public class RegisterHandler : IRequestHandler<RegisterCommand, AuthResponse>
{
    private readonly IUserRepository _users;
    private readonly ICustomerRepository _customers;
    private readonly IPasswordHasherService _hasher;
    private readonly ITokenService _tokens;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterHandler(
        IUserRepository users,
        ICustomerRepository customers,
        IPasswordHasherService hasher,
        ITokenService tokens,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _customers = customers;
        _hasher = hasher;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = AppUser.NormalizeEmail(request.Email);

        if (await _users.EmailExistsAsync(email, cancellationToken))
            throw new ConflictException("An account with this email already exists.");

        var customer = new Customer(request.Name);
        _customers.Add(customer);

        // Customer and user are saved in one SaveChanges (one implicit transaction). The unique email
        // index is the real guard against concurrent duplicate registrations.
        var user = AppUser.CreateCustomerUser(email, _hasher.Hash(request.Password), customer);
        _users.Add(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var token = _tokens.Create(user);
        return new AuthResponse(token.AccessToken, token.ExpiresAtUtc, user.Role, customer.Id);
    }
}
