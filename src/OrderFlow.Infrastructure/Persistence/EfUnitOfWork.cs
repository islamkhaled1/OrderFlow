using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.Interfaces;

namespace OrderFlow.Infrastructure.Persistence;

public class EfUnitOfWork : IUnitOfWork
{
    private const int SqlDuplicateKey = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    private readonly OrderFlowDbContext _context;

    public EfUnitOfWork(OrderFlowDbContext context)
    {
        _context = context;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        // With EnableRetryOnFailure, user-initiated transactions must run inside the execution
        // strategy so the whole unit (not a single command) is retried on transient errors.
        var strategy = _context.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async ct =>
            {
                // A retry starts from a clean slate: discard entities tracked by the failed attempt.
                _context.ChangeTracker.Clear();

                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var result = await operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql &&
                                           sql.Number is SqlDuplicateKey or SqlUniqueConstraintViolation)
        {
            throw new ConflictException("The resource already exists.");
        }
    }
}
