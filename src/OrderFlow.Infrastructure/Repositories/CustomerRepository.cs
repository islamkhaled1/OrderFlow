using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly OrderFlowDbContext _context;

    public CustomerRepository(OrderFlowDbContext context)
    {
        _context = context;
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers.AnyAsync(c => c.Id == id, cancellationToken);
    }

    public void Add(Customer customer) => _context.Customers.Add(customer);
}

public class UserRepository : IUserRepository
{
    private readonly OrderFlowDbContext _context;

    public UserRepository(OrderFlowDbContext context)
    {
        _context = context;
    }

    public Task<AppUser?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

    public void Add(AppUser user) => _context.Users.Add(user);
}
