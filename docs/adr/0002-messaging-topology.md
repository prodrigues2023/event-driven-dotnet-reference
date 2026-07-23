# ADR-0002: Messaging topology

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

The topology determines how tightly producers and consumers are coupled. Get it wrong and
every new consumer requires a change to the producer — which defeats the reason for adopting
messaging in the first place.

Two distinct interaction styles must be supported, and conflating them is a common and
expensive mistake:

- **Events** — a statement that something happened. `OrderPlaced`. The producer does not know
  or care who listens. Multiple consumers, each with its own reason to care.
- **Commands** — an instruction to do something. `ReserveStock`. Exactly one logical handler.
  The sender knows the receiver.

Options considered for the exchange strategy:

1. **Direct exchange per consumer.** Producer routes explicitly to each consumer's queue.
   Simple, and completely coupled — a new consumer means a producer change.
2. **Single topic exchange for everything.** One exchange, routing keys carry the message
   type. Decoupled, but events and commands share a namespace and the routing keys become an
   undocumented protocol.
3. **Topic exchange per bounded context, plus direct for commands.** Events published to the
   owning context's exchange; commands sent directly to the handling service's queue.
4. **Fanout per event type.** Maximum decoupling, but the number of exchanges grows with the
   number of event types and operating it becomes tedious.

## Decision

**Events** are published to a **topic exchange per bounded context**, named
`{context}.events` — for example `ordering.events`. The routing key is the event type in
dotted lower case: `order.placed`, `order.cancelled`.

Each consumer declares its **own durable queue**, named `{service}.{context}.events`, and
binds it with the patterns it cares about. Adding a consumer requires no change to the
producer and no coordination beyond agreeing on the contract.

**Commands** are sent to a **direct exchange** bound to a single queue owned by the handling
service, named `{service}.commands`. One sender, one handler, no ambiguity.

Every queue is durable, every message is persistent, and publisher confirms are enabled. The
outbox in [ADR-0003](./0003-transactional-outbox.md) depends on confirms to know whether the
broker accepted a message.

**Ordering** is guaranteed only per queue with a single consumer. The architecture does not
promise global ordering, and consumers must not assume it — see the consequences below.

## Consequences

**Positive**

- Producers are unaware of consumers. A new subscriber is a deployment, not a negotiation.
- Events and commands are visibly different in the topology, which keeps the semantic
  distinction from eroding as the system grows.
- Per-consumer queues mean a slow consumer builds its own backlog rather than blocking others.
- Queue depth per consumer is a directly useful operational metric.

**Negative**

- Routing keys become a public contract. Renaming an event type breaks bindings silently —
  the consumer simply stops receiving, with no error anywhere. The versioning policy in
  Milestone 2 must address this, and it is the sharpest edge in this design.
- A message consumed by five services is stored five times. Storage and broker throughput
  scale with subscriber count.
- Ordering across queues is not guaranteed. A consumer that receives `OrderCancelled` before
  `OrderPlaced` must handle it, which is a real burden pushed onto every consumer team.
  Scaling a consumer to multiple instances gives up per-queue ordering as well; where
  ordering genuinely matters, a single consumer with partitioned queues is the escape hatch.
- Topic exchanges with wildcard bindings make it hard to answer "who consumes this event?"
  from the broker alone. The event catalogue in Milestone 2 exists to answer that question.
