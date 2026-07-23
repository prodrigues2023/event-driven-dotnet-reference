# Quality attributes and trade-offs

Targets are numbers so they can be measured and, if wrong, corrected. Every figure below is
a hypothesis until the Milestone 4 load tests report against it.

## Targets

| Attribute | Target | How it is measured |
| --- | --- | --- |
| Command acceptance (p95) | < 100 ms | API latency, excluding downstream processing |
| Outbox dispatch delay (p95) | < 200 ms | Insert timestamp to broker confirm |
| End-to-end process latency (p95) | < 2 s | Order placed to shipment created, happy path |
| Sustained throughput | > 2,000 messages/s per consumer instance | Load test |
| Message durability | Zero loss under broker or consumer failure | Chaos suite |
| Duplicate handling | Exactly-once effect under forced redelivery | Integration test |
| Backlog recovery | 100,000 message backlog drained in under 10 min | Load test |
| Availability | 99.9% for command acceptance | Not enforced in the reference implementation |

## Latency budget — order placed to shipment created (p95)

| Stage | Budget |
| --- | --- |
| Command handling and commit | 80 ms |
| Outbox dispatch | 200 ms |
| Broker routing | 20 ms |
| Payment consumer | 400 ms |
| Payment outbox dispatch | 200 ms |
| Shipping consumer | 300 ms |
| Saga coordination overhead | 300 ms |
| Slack | 500 ms |
| **Total** | **2,000 ms** |

The two outbox dispatch windows account for 400 ms — twenty percent of the budget, spent
entirely on durability. That is the price of never losing a message, and it is the single
most important trade-off in this architecture.

## Accepted trade-offs

**Durability over latency.** The outbox adds up to 200 ms per hop. A slower message that
always arrives beats a faster one that occasionally does not, and the loss would be silent.

**At-least-once over exactly-once.** Exactly-once delivery is not available. At-least-once
delivery plus idempotent consumption produces the effect that matters, and pays for it with
an inbox table and a discipline every consumer team must follow.

**Availability over consistency.** Services accept commands even when downstream consumers
are down; messages queue and the process completes when the dependency recovers. The cost is
that the system is visibly inconsistent in the meantime, and the user interface has to say so.

**Per-queue ordering over global ordering.** Global ordering would require a single queue and
a single consumer, which caps throughput at one consumer. Consumers tolerate out-of-order
delivery instead — a real burden pushed onto every consumer team, accepted deliberately.

**Operational overhead over silent failure.** Delay queues, dead-letter queues, outbox and
inbox tables, and their purge jobs are all things to run and monitor. The alternative is a
system that loses work without telling anyone.

## What must be monitored

These are not suggestions. Each one detects a failure that is otherwise invisible:

| Signal | Why | Alert when |
| --- | --- | --- |
| Undispatched outbox rows | A stalled dispatcher looks exactly like an idle system | Count rising, or oldest row > 30 s |
| Consumer lag / queue depth | The earliest signal of a degraded consumer | Depth rising over 5 min |
| Dead-letter queue depth | A DLQ nobody watches is a slow delete | Any message, for critical queues |
| Retry rate by level | Distinguishes a blip from a failing dependency | Level 3+ retries rising |
| Inbox duplicate rate | A spike means redelivery, and something upstream is wrong | Sustained increase |
| Outbox and inbox table size | Purging has stopped | Growth without a matching purge |

## Known limitations

Stated up front so nobody discovers them the hard way:

- No global ordering. Consumers that require strict sequence need partitioned queues and give
  up parallelism within a partition.
- The polling dispatcher becomes a bottleneck at very high write rates. Change data capture
  is the escape hatch, and would supersede [ADR-0003](./adr/0003-transactional-outbox.md).
- Exception classification in [ADR-0005](./adr/0005-retry-and-dead-lettering.md) is a judgement
  call and will misclassify. Misclassifying a transient failure as permanent dead-letters
  recoverable work.
- Sagas make compensation explicit but not free. Some effects cannot be compensated — an email
  is sent, a payment is captured — and those steps must be ordered last in the saga, which
  constrains the design of the business process itself.
- The retention windows for outbox and inbox are a correctness boundary, not a housekeeping
  detail. Getting their relationship wrong produces duplicate processing that no test catches.
