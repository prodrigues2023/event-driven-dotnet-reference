using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EventDriven.Messaging;

/// <summary>
/// A row in the transactional outbox (ADR-0003). Written in the same transaction as the state
/// change; a dispatcher publishes it and stamps <see cref="DispatchedAt"/> only after the broker confirms.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }              // the stable MessageId (ADR-0004)
    public long Seq { get; set; }             // insertion order, preserves per-aggregate causal order
    public string AggregateId { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string RoutingKey { get; set; } = "";
    public string Body { get; set; } = "";    // JSON payload
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
}

/// <summary>
/// A processed-message record (ADR-0004). Inserted in the same transaction as the effect; a unique
/// key on <see cref="MessageId"/> turns a duplicate delivery into a constraint violation, not a re-run.
/// </summary>
public class InboxMessage
{
    public Guid MessageId { get; set; }
    public DateTime ProcessedAt { get; set; }
}

/// <summary>A DbContext that carries the outbox and inbox tables.</summary>
public interface IMessagingDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }
}

/// <summary>Thrown by a handler to classify a failure as permanent — dead-lettered on the first
/// attempt with no retries (ADR-0005). Any other exception is treated as transient.</summary>
public sealed class PoisonMessageException(string message) : Exception(message);

/// <summary>The envelope reconstructed from AMQP properties on the consume side.</summary>
public sealed record MessageEnvelope(
    Guid MessageId, string Type, Guid CorrelationId, Guid? CausationId, DateTime OccurredAt, string Body)
{
    public T Payload<T>() => JsonSerializer.Deserialize<T>(Body)
        ?? throw new PoisonMessageException($"Message {MessageId} of type '{Type}' has an empty body.");
}

public static class MessagingModel
{
    /// <summary>Call from a service's OnModelCreating to map the outbox and inbox tables.</summary>
    public static void ConfigureMessaging(this ModelBuilder mb)
    {
        mb.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Seq).ValueGeneratedOnAdd();
            e.HasIndex(x => x.Seq).IsUnique();
            e.Property(x => x.Body).HasColumnType("jsonb");
            e.HasIndex(x => x.DispatchedAt);
        });
        mb.Entity<InboxMessage>(e =>
        {
            e.ToTable("inbox_messages");
            e.HasKey(x => x.MessageId);
        });
    }
}
