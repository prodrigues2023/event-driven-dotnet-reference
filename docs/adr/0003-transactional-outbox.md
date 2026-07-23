# ADR-0003: Transactional outbox

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

A service handling a command usually does two things: change its own state, and tell the
world about it. Those are two different systems — a database and a broker — and there is no
transaction spanning both.

```csharp
await _db.SaveChangesAsync();      // committed
await _bus.Publish(orderPlaced);   // process dies here
```

The order exists. Nobody was told. Payment is never taken, and no error was raised anywhere:
the command returned successfully. This is the **dual write problem**, and it is the single
most common source of silent data divergence in event-driven systems.

Reversing the order makes it worse — publish first, and a failed commit means an event about
something that never happened, which consumers have already acted on.

Options considered:

1. **Publish inside the transaction, accept the risk.** The default in most codebases,
   usually not a decision at all. Loses messages at a low but non-zero rate, invisibly.
2. **Distributed transaction (two-phase commit).** Correct in theory. In practice: poor
   support across .NET, PostgreSQL and RabbitMQ, blocking behaviour under coordinator failure,
   and a performance cost nobody accepts twice.
3. **Transactional outbox.** Write the message to a table in the same transaction as the state
   change; a separate dispatcher reads the table and publishes. The commit is atomic because
   it is one database.
4. **Change data capture.** Read the database log and derive events from it. No application
   code needed, but events become a projection of the schema rather than a deliberate contract,
   and schema changes leak into the integration surface.

## Decision

Use the **transactional outbox**.

- The message is inserted into an `outbox_messages` table in the same transaction as the state
  change. Either both happen or neither does.
- A background **dispatcher** polls for undispatched rows, publishes them with publisher
  confirms enabled, and marks them dispatched only after the broker confirms.
- Rows are dispatched in insertion order per aggregate, preserving causal ordering where it
  matters.
- The dispatcher may publish the same message twice — if it crashes between the confirm and
  the mark. That is accepted and handled by [ADR-0004](./0004-idempotent-consumers.md).
- Dispatched rows are retained for a bounded window, then purged. The retention window is the
  replay window: within it, a message can be re-published without archaeology.

Change data capture is deferred rather than rejected. It becomes attractive at a scale where
polling is a bottleneck, and this ADR would then be superseded.

## Consequences

**Positive**

- No message is ever lost. Delivery is guaranteed by the same transaction that guarantees the
  state change, which is the only guarantee the service already had.
- The outbox table is an audit trail: what was published, when, and whether it was confirmed.
- Replay is trivial within the retention window — clear the dispatched flag.
- No distributed transaction, no coordinator, no new failure mode in the critical path.

**Negative**

- **Latency.** Publishing is no longer immediate; it waits for the next poll. A 100 ms poll
  interval keeps this acceptable for most workloads, at the cost of a constant query against
  the database. This is the central trade-off of the pattern.
- **At-least-once, always.** The outbox converts "might lose messages" into "will occasionally
  duplicate messages". That is a strictly better problem, but it is not no problem — every
  consumer must be idempotent, without exception.
- **Another moving part to operate.** A stalled dispatcher is invisible unless monitored.
  Undispatched row count and oldest undispatched age are mandatory alerts, not optional ones.
- **Ordering is per aggregate, not global.** A single dispatcher preserves insertion order;
  scaling to several dispatchers requires partitioning by aggregate id, and gives up ordering
  between partitions.
- **Table growth.** Without purging, the outbox becomes one of the largest tables in the
  database. Purging must ship with the pattern, not after it.
