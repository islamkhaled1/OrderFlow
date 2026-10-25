using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Persistence.Entities;

namespace OrderFlow.Infrastructure.Outbox;

/// <summary>Writes outbox rows through the same DbContext (and therefore the same transaction) as the business change.</summary>
public class EfOutbox : IOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderFlowDbContext _context;

    public EfOutbox(OrderFlowDbContext context)
    {
        _context = context;
    }

    public void Enqueue(string eventType, object payload)
    {
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            OccurredAtUtc = DateTime.UtcNow,
            NextAttemptAtUtc = DateTime.UtcNow
        });
    }
}

public class OutboxAdminService : IOutboxAdminService
{
    private readonly OrderFlowDbContext _context;

    public OutboxAdminService(OrderFlowDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<DeadLetterDto>> GetDeadLettersAsync(int take, CancellationToken cancellationToken)
    {
        return await _context.OutboxMessages
            .AsNoTracking()
            .Where(m => m.DeadLetteredAtUtc != null)
            .OrderByDescending(m => m.DeadLetteredAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .Select(m => new DeadLetterDto(m.Id, m.EventType, m.Payload, m.Attempts, m.LastError, m.OccurredAtUtc, m.DeadLetteredAtUtc!.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RequeueAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var affected = await _context.OutboxMessages
            .Where(m => m.Id == id && m.DeadLetteredAtUtc != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.DeadLetteredAtUtc, (DateTime?)null)
                .SetProperty(m => m.Attempts, 0)
                .SetProperty(m => m.NextAttemptAtUtc, now), cancellationToken);
        return affected == 1;
    }
}
