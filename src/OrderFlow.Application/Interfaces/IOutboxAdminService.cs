namespace OrderFlow.Application.Interfaces;

public sealed record DeadLetterDto(
    Guid Id,
    string EventType,
    string Payload,
    int Attempts,
    string? LastError,
    DateTime OccurredAtUtc,
    DateTime DeadLetteredAtUtc);

/// <summary>Operational access to the outbox for administrators.</summary>
public interface IOutboxAdminService
{
    Task<IReadOnlyList<DeadLetterDto>> GetDeadLettersAsync(int take, CancellationToken cancellationToken);

    /// <summary>Resets a dead-lettered message so the processor retries it. Returns false if not found / not dead-lettered.</summary>
    Task<bool> RequeueAsync(Guid id, CancellationToken cancellationToken);
}
