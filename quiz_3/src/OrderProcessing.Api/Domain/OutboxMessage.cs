using System.Text.Json;

namespace OrderProcessing.Domain;

/// <summary>
/// Side effect (email, event) saved in the same transaction as the state change
/// and delivered later by <c>OutboxProcessor</c>, so a failing email can't fail an order.
/// </summary>
public class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public long Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create<T>(T message, DateTime now) where T : notnull => new()
    {
        Type = typeof(T).Name,
        Payload = JsonSerializer.Serialize(message),
        CreatedAt = now,
    };

    public T Deserialize<T>() =>
        JsonSerializer.Deserialize<T>(Payload)
        ?? throw new InvalidOperationException($"Outbox message {Id} has an empty payload.");

    public void MarkProcessed(DateTime now)
    {
        ProcessedAt = now;
        Attempts++;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error;
    }
}
