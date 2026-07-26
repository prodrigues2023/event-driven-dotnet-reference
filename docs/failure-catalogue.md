# Failure catalogue

Every failure mode the system is designed to survive, how it is detected, and how it responds. Each is
addressed by an ADR and demonstrated by the [chaos suite](../scripts/chaos.sh) (`make chaos`) or the
[demo](../scripts/demo.sh) (`make demo`).

| Failure | What happens | Detection | Response | Reference |
| --- | --- | --- | --- | --- |
| **Dual write** | State commits, the publish fails — the message is lost | Would be silent; prevented by design | The event is written to the outbox in the state change's transaction; a dispatcher publishes it after | [ADR-0003](./adr/0003-transactional-outbox.md) |
| **Duplicate delivery** | The same message arrives twice (dispatcher crash, redelivery, replay) | Inbox unique-constraint violation on `MessageId` | Skip and acknowledge — the effect already happened (exactly-once effect) | [ADR-0004](./adr/0004-idempotent-consumers.md) |
| **Poison message** | A malformed or business-invalid message can never succeed | Handler throws `PoisonMessageException` | Dead-lettered on the first attempt with diagnostic headers; the queue is not blocked | [ADR-0005](./adr/0005-retry-and-dead-lettering.md) |
| **Transient fault** | A dependency blips (timeout, deadlock, brief DB outage) | Any non-poison exception | In-process retry, then the delayed-retry ladder through the broker; eventually the DLQ | [ADR-0005](./adr/0005-retry-and-dead-lettering.md) |
| **Broker down** | Publishes and deliveries stop | Dispatcher publish fails; consumer disconnects | Producers keep writing to the outbox (durable); dispatcher and consumers reconnect and drain on return | [ADR-0003](./adr/0003-transactional-outbox.md) · chaos |
| **Consumer down** | Work is not processed | Queue depth grows (visible on the console and in the UI) | The durable queue holds the backlog; it drains on restart | [ADR-0002](./adr/0002-messaging-topology.md) · [runbooks](./runbooks.md) |
| **Database down** | In-flight transactions cannot commit | Handler exceptions (transient) | Transactions fail and retry; no partial effect — the inbox and the effect commit together or not at all | [ADR-0004](./adr/0004-idempotent-consumers.md) · chaos |
| **Business decline** | Payment over the limit | Not a failure — a valid outcome | `PaymentDeclined` event; the saga cancels the order | [ADR-0002](./adr/0002-messaging-topology.md) |
| **Shipment failure after payment** | A completed step must be undone | `ShipmentFailed` event | The saga issues `RefundPayment`; the order ends compensated | [ADR-0007](./adr/0007-saga-vs-process-manager.md) |
| **Stalled dispatcher** | The outbox stops draining | Undispatched row count and oldest-undispatched age rise | Alert on those two metrics; restart the producer — nothing is lost | [ADR-0003](./adr/0003-transactional-outbox.md) · [runbooks](./runbooks.md) |
| **Stalled saga** | A saga waits for an event that never comes | A sweep finds sagas held in a waiting state past a deadline | A timeout monitor fires compensation: cancel if unpaid, refund if paid | [ADR-0007](./adr/0007-saga-vs-process-manager.md) |

Every failure mode above now has an automatic response. The saga timeout — the last open item — is
handled by the `SagaTimeoutMonitor`; the remaining hardening (tuning deadlines per process, alerting
on timeout rates) is operational, not a gap in the design.
