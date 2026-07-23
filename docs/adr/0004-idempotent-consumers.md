# ADR-0004: Idempotent consumers

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

The outbox in [ADR-0003](./0003-transactional-outbox.md) guarantees at-least-once delivery.
It explicitly does not guarantee exactly-once, because exactly-once delivery over an
unreliable network is not achievable. Duplicates arrive from at least four directions:

- The dispatcher crashes between the broker confirm and marking the row dispatched
- A consumer processes a message and dies before acknowledging it
- The broker redelivers on connection loss
- An operator replays a message deliberately during incident recovery

The last one matters more than teams expect. Replay is the primary recovery tool for a
messaging system, and a consumer that cannot tolerate a replay cannot be recovered safely —
which means the recovery procedure becomes "restore a backup", at 3am.

Options considered:

1. **Naturally idempotent handlers.** Design every operation so re-applying it is harmless —
   `SetStatus(Shipped)` rather than `IncrementAttempts()`. Free when achievable; not always
   achievable, and impossible for anything with an external side effect such as taking a
   payment or sending an email.
2. **Inbox table (deduplication store).** Record every processed message id in the same
   transaction as its effect; skip anything already recorded. Universal, at the cost of a
   table and a lookup.
3. **Optimistic concurrency on the aggregate.** Version checks reject stale writes. Solves
   concurrent updates, but does not by itself detect a duplicate of an already-applied message.
4. **Broker-level deduplication.** Not available in RabbitMQ without plugins, and it would
   move a correctness guarantee outside the application boundary where it cannot be tested.

## Decision

**Every consumer is idempotent. This is a precondition for deploying a consumer, not a
recommendation.**

The mechanism is an **inbox table**, combined with naturally idempotent handlers wherever the
domain allows.

- Every message carries a `MessageId` assigned by the producer at outbox insertion. It is
  stable across retries and replays — a redelivery is the *same* message, not a new one.
- The consumer opens a transaction, attempts to insert the `MessageId` into `inbox_messages`
  with a unique constraint, applies the effect, and commits. Effect and deduplication record
  commit atomically or not at all.
- A unique-constraint violation means the message was already processed: acknowledge it and
  stop. Detecting the duplicate at the database is deliberate — a check-then-act read is a
  race under concurrent redelivery, and this closes it.
- Inbox rows are retained for a bounded window and then purged. The window must exceed the
  outbox retention window, or a legitimate replay would be treated as new work.
- Side effects that cannot participate in the transaction — sending an email, calling a
  payment provider — are performed through the outbox of the consuming service, never inline.

Handlers should still be written to be naturally idempotent where possible. The inbox is a
safety net, not a licence to write handlers that break on re-execution.

## Consequences

**Positive**

- Exactly-once *effect*, which is the property that actually matters, without requiring
  exactly-once delivery, which is not available.
- Replay becomes a safe operational tool. This alone justifies the pattern: incident recovery
  changes from "restore and reconcile" to "clear the flag and let it run".
- Duplicate detection is enforced by a database constraint rather than by handler discipline,
  so it does not decay as teams change.

**Negative**

- One extra insert and one extra transaction participant per message. At high throughput the
  inbox table becomes a hot spot and needs the same purging discipline as the outbox.
- The retention window is a real correctness boundary. Purge too aggressively and a
  legitimate replay is processed twice; purge too slowly and the table grows without bound.
  The relationship to the outbox window must be documented where operators will find it, not
  only here.
- It does not make external side effects idempotent. Calling a payment API twice is prevented
  by the inbox only if the call happens inside the transaction boundary — which it cannot.
  Routing such calls through the outbox moves the problem to the provider's own idempotency
  key, and every integration must be checked for one.
- Producers must assign stable message ids. A producer that generates a fresh id per retry
  silently defeats the entire mechanism, and nothing will detect it until the duplicates
  reach production.
