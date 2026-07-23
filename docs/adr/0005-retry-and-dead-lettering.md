# ADR-0005: Retry and dead-lettering

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

A consumer fails for two very different reasons, and treating them the same is the source of
most messaging incidents.

- **Transient.** The database was briefly unavailable, a dependency timed out, a deadlock was
  detected. Retrying works.
- **Permanent.** The payload is malformed, a referenced entity does not exist, a business rule
  rejects it. Retrying will never work — this is a **poison message**.

Retrying a poison message forever blocks the queue behind it and generates load with no
prospect of success. Discarding a transient failure loses work that would have succeeded a
second later. The system must distinguish them, and it cannot always do so from the exception
alone.

There is also a subtlety specific to RabbitMQ: rejecting a message with `requeue: true` puts
it back at the *head* of the queue, and it is redelivered immediately. That is an unthrottled
retry loop, and it is the default behaviour teams accidentally ship.

Options considered:

1. **In-process retry only.** Catch, `Task.Delay`, retry in the handler. Simple, but the
   message remains unacknowledged, holding a prefetch slot and blocking the consumer. Process
   restart loses the retry state.
2. **Requeue on failure.** Immediate redelivery with no backoff. The unthrottled loop above.
3. **Delay queues with dead-letter routing.** Reject to a delay queue with a per-level TTL;
   expiry dead-letters the message back to the work queue. Backoff without holding a consumer,
   and the delay survives restarts.
4. **Broker delayed-message plugin.** Cleaner to express, but a plugin dependency and a
   portability break with no equivalent elsewhere.

## Decision

A **three-layer** strategy.

**Layer 1 — in-process retry for known transient faults.** Three attempts with a short
exponential backoff and jitter, applied only to an explicit allow-list of exception types:
transient database faults, timeouts, deadlocks. Everything else skips this layer entirely.
The purpose is to absorb a blip without a broker round trip, so the budget is deliberately
small — a handler must not hold a prefetch slot for seconds.

**Layer 2 — delayed retry through the broker.** On exhausting layer 1, the message is
rejected without requeue into a delay queue with a per-level TTL: 5 s, 30 s, 2 min, 10 min,
30 min. On expiry it dead-letters back to the work queue. The attempt count is carried in a
header, and the consumer is released immediately rather than blocking on a delay.

**Layer 3 — dead-letter queue.** After the delay levels are exhausted, or immediately on an
exception classified as permanent, the message goes to `{queue}.dlq` with diagnostic headers:
original queue, first and last failure timestamps, attempt count, exception type and message,
and the trace id.

Rules that hold throughout:

- **Never `requeue: true`.** Always reject without requeue and route deliberately.
- **Fail fast on permanent errors.** A validation failure does not deserve thirty minutes of
  retries — it is dead-lettered on the first attempt.
- **The DLQ is monitored and alerted.** A dead-letter queue nobody watches is a slower way of
  discarding messages.
- **Replay is a supported operation**, safe because of
  [ADR-0004](./0004-idempotent-consumers.md), and documented in a runbook rather than
  improvised during an incident.

## Consequences

**Positive**

- Transient failures recover without intervention; permanent failures surface immediately
  instead of after an hour of retries.
- Backoff happens in the broker, so a retrying message consumes no consumer capacity — a
  degraded dependency does not consume the whole consumer pool.
- Retry state survives a consumer restart, because it lives in the broker rather than in
  process memory.
- Every failure ends somewhere observable. Nothing disappears.

**Negative**

- Five delay queues per work queue is real topology overhead. Declaring them must be
  automated, or environments will drift and messages will vanish into a queue nobody created.
- **Exception classification is the weak point.** It is a judgement call, it will be wrong,
  and being wrong in the permanent direction dead-letters recoverable work. The classification
  list needs review whenever a new dependency is added, and the default for an unrecognised
  exception is transient — the cheaper mistake.
- Delayed retry reorders messages relative to the queue. A message retried for two minutes is
  processed after messages that arrived later. Consumers must already tolerate out-of-order
  delivery per [ADR-0002](./0002-messaging-topology.md), but this makes it routine rather than
  exceptional.
- The DLQ needs an owner and a triage routine. Without one it fills quietly, and the first
  time anyone looks there are forty thousand messages and no memory of what they were.
- Total worst-case latency before dead-lettering is roughly 43 minutes across the delay
  levels. For time-sensitive processes that is too long, and those queues need their own
  shorter ladder — configured per queue, not globally.
