using Microsoft.EntityFrameworkCore;

namespace EventDriven.Messaging;

/// <summary>What a handler receives: the message, the service's DbContext, and its outbox — all bound
/// to one transaction so the effect, the dedup record, and any new messages commit together (ADR-0004).</summary>
public sealed class InboundContext<TContext> where TContext : DbContext
{
    public required MessageEnvelope Envelope { get; init; }
    public required TContext Db { get; init; }
    public required OutboxWriter Outbox { get; init; }
}

public delegate Task MessageHandler<TContext>(InboundContext<TContext> context, CancellationToken ct)
    where TContext : DbContext;

/// <summary>Declarative consumer setup: one durable queue, its bindings, and a handler per event type.</summary>
public sealed class EventConsumerOptions<TContext> where TContext : DbContext
{
    public string QueueName { get; set; } = "";
    public List<(string Exchange, string Pattern)> Bindings { get; } = new();
    public Dictionary<string, MessageHandler<TContext>> Handlers { get; } = new();

    public EventConsumerOptions<TContext> Bind(string exchange, string pattern)
    {
        Bindings.Add((exchange, pattern));
        return this;
    }

    public EventConsumerOptions<TContext> On(string routingKey, MessageHandler<TContext> handler)
    {
        Handlers[routingKey] = handler;
        return this;
    }
}
