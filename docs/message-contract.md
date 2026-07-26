# Message contract standard

Every message on the bus — event or command — carries the same envelope. The envelope is transport
metadata; the body is the typed payload defined by that message's [JSON Schema](../contracts/schemas).
This standard is implemented once, in [`EventDriven.Messaging`](../src/EventDriven.Messaging), so every
service produces and consumes it identically.

## The envelope

Carried in AMQP message properties and headers, not in the body.

| Field | AMQP location | Required | Meaning |
| --- | --- | --- | --- |
| **MessageId** | `message-id` | yes | A stable id assigned once at outbox insertion. It does **not** change on retry or replay — a redelivery is the *same* message. This is the key the inbox deduplicates on ([ADR-0004](./adr/0004-idempotent-consumers.md)). |
| **Type** | `type` | yes | The logical message type — the routing key for an event (`order.placed`), or the command name for a command (`payment.refund`). Handlers dispatch on it. |
| **CorrelationId** | `correlation-id` | yes | Groups every message in one business flow. Set once when the flow starts and copied onto every message it causes, so an entire order fulfilment shares one id. |
| **CausationId** | header `x-causation-id` | no | The `MessageId` of the message that directly caused this one. CorrelationId gives the flow; CausationId gives the parent, so the causal tree can be reconstructed. |
| **traceparent** | header `traceparent` | no | W3C trace context, so the flow is one distributed trace across services ([tracing](./../README.md#observability-and-performance)). Captured from the ambient trace at creation. |
| **OccurredAt** | header `x-occurred-at` | yes | When the producing event happened (ISO-8601, UTC). |
| **ContentType** | `content-type` | yes | `application/json`. |

The body is UTF-8 JSON, `persistent` (survives a broker restart), and validated against the message's
schema.

## Identity semantics — the three ids

These are distinct on purpose, and conflating them is a common bug:

- **MessageId** — *this message*. Stable across redelivery and replay. Deduplication key.
- **CorrelationId** — *this flow*. The same across every message from `OrderPlaced` to `OrderShipped`
  (or to `PaymentRefunded`). Answers "show me everything about this order".
- **CausationId** — *the message that caused this one*. Answers "what triggered this". A chain of
  CausationIds is the causal history; the CorrelationId is the bag they all share.

A producer that assigns a fresh MessageId on retry silently defeats the inbox — the redelivery looks
like new work. The outbox assigns the MessageId once, at insertion, precisely to prevent this.

## Events vs. commands

The envelope is identical; the intent and topology differ ([ADR-0002](./adr/0002-messaging-topology.md)).

| | Event | Command |
| --- | --- | --- |
| Means | a fact — something happened | an instruction — do this |
| Example | `OrderPlaced`, `PaymentAuthorized` | `RefundPayment` |
| Routed via | topic exchange `{context}.events` | the handling service's own queue |
| Handlers | any number of subscribers | exactly one |
| Naming | past tense (`order.placed`) | imperative (`payment.refund`) |

## Delivery guarantees (recap)

- **At-least-once**, always — the outbox converts "might lose" into "might duplicate"
  ([ADR-0003](./adr/0003-transactional-outbox.md)).
- **Exactly-once effect** — every consumer is idempotent via the inbox
  ([ADR-0004](./adr/0004-idempotent-consumers.md)).
- **Ordering** is per queue with a single consumer only; consumers must tolerate out-of-order delivery
  ([ADR-0002](./adr/0002-messaging-topology.md)).
