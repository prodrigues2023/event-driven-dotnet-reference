# Context and scope

## The problem

A business process crosses several services. An order is placed, payment is taken, stock is
reserved, a shipment is created, the customer is notified. Each step belongs to a different
service, owned by a different team, with its own database and its own release cycle.

Wiring these together with synchronous HTTP calls produces a system where the availability
of the whole is the product of the availability of every part, where a slow shipping service
makes order placement slow, and where adding a sixth step means changing the first one.

Messaging solves the coupling. It also introduces a category of failure that synchronous
calls do not have — messages lost between a database commit and a broker publish, messages
delivered twice, messages that can never be processed and block everything behind them.

This repository documents how to get the benefits without the failures.

## Users

| User | Need |
| --- | --- |
| Service team | Publish and consume events without knowing who is on the other side |
| Platform team | Operate the broker: monitor queue depth, consumer lag, dead letters |
| On-call engineer | Diagnose a stuck process and replay what failed, at 3am, from a runbook |
| Architect | Understand the guarantees, and adapt the design to their own constraints |

## In scope

- Reliable publishing from a transactional boundary ([ADR-0003](./adr/0003-transactional-outbox.md))
- Idempotent consumption under at-least-once delivery ([ADR-0004](./adr/0004-idempotent-consumers.md))
- Retry, backoff, and dead-lettering with diagnostics ([ADR-0005](./adr/0005-retry-and-dead-lettering.md))
- Topology that lets consumers be added without touching producers ([ADR-0002](./adr/0002-messaging-topology.md))
- Long-running processes with compensation — sagas
- Distributed tracing that survives the hop through the broker

## Explicitly out of scope

Deliberate exclusions, not omissions:

- **Event sourcing.** A different pattern with a different cost, frequently confused with
  event-driven architecture. Messaging is about integration between services; event sourcing
  is about how one service stores its state. This repository covers the first.
- **CQRS.** Same reasoning. Orthogonal, and it deserves its own treatment.
- **Exactly-once delivery.** It does not exist over an unreliable network. What exists is
  at-least-once delivery plus idempotent handling, which produces an exactly-once *effect* —
  and that is what this architecture provides.
- **Streaming analytics.** Kafka-style log processing for high-volume analytical workloads is
  a different problem with different tools.
- **A production UI.** The reference implementation exposes the state of a process through an
  API, not a dashboard.

## Key constraints

1. **Runs on a laptop.** The full reference implementation comes up with `docker compose up`.
2. **No message loss.** A message accepted inside a committed transaction is delivered, or it
   is visible in a dead-letter queue with enough context to act on. It is never silently gone.
3. **Every consumer is idempotent.** Not as a recommendation — as a precondition for
   deploying a consumer at all.
4. **Every failure is observable.** A message that fails leaves a trace. "It disappeared" is
   not an acceptable state.
5. **Broker-portable where it is cheap to be.** The design targets RabbitMQ but avoids
   depending on features with no equivalent elsewhere, except where documented in an ADR.

## Related documents

- [Quality attributes](./quality-attributes.md) — the targets and the trade-offs
- [Diagrams](./diagrams) — C4 views and message flows
- [ADRs](./adr) — the decisions and their reasoning
