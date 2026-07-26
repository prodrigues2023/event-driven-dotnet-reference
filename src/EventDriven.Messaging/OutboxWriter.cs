using System.Diagnostics;
using System.Text.Json;

namespace EventDriven.Messaging;

/// <summary>
/// Adds messages to the outbox within the caller's existing transaction. The rows are not published
/// here — they are committed atomically with the state change, and the dispatcher publishes them later.
/// This is the whole point of ADR-0003: the publish shares the state change's transaction.
/// </summary>
public sealed class OutboxWriter(IMessagingDbContext db, string eventExchange)
{
    /// <summary>Publish an event to this service's events exchange (ADR-0002), through the outbox.</summary>
    public void Publish<T>(string aggregateId, string routingKey, T payload, MessageEnvelope? causedBy = null) =>
        Add(aggregateId, eventExchange, routingKey, null, payload, causedBy);

    /// <summary>Send a command to a single handler's queue (ADR-0002), through the outbox. Routed by the
    /// default exchange to the named queue; the logical command type travels in the message type.</summary>
    public void SendCommand<T>(string aggregateId, string targetQueue, string commandType, T payload, MessageEnvelope? causedBy = null) =>
        Add(aggregateId, "", targetQueue, commandType, payload, causedBy);

    private void Add<T>(string aggregateId, string exchange, string routingKey, string? messageType, T payload, MessageEnvelope? causedBy)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            Exchange = exchange,
            RoutingKey = routingKey,
            MessageType = messageType,
            Body = JsonSerializer.Serialize(payload),
            CorrelationId = causedBy?.CorrelationId ?? Guid.NewGuid(),
            CausationId = causedBy?.MessageId,
            TraceParent = Activity.Current?.Id, // captures the current trace context to propagate downstream
            OccurredAt = DateTime.UtcNow
        });
    }
}
