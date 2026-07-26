using System.Text.Json;

namespace EventDriven.Messaging;

/// <summary>
/// Adds messages to the outbox within the caller's existing transaction. The rows are not published
/// here — they are committed atomically with the state change, and the dispatcher publishes them later.
/// This is the whole point of ADR-0003: the publish shares the state change's transaction.
/// </summary>
public sealed class OutboxWriter(IMessagingDbContext db, string eventExchange)
{
    public void Publish<T>(string aggregateId, string routingKey, T payload, MessageEnvelope? causedBy = null)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            Exchange = eventExchange,
            RoutingKey = routingKey,
            Body = JsonSerializer.Serialize(payload),
            CorrelationId = causedBy?.CorrelationId ?? Guid.NewGuid(),
            CausationId = causedBy?.MessageId,
            OccurredAt = DateTime.UtcNow
        });
    }
}
