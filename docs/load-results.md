# Load test results

Reproduce with `make loadtest` (defaults: 500 orders, concurrency 32) against `make up`. The tool
measures two things that matter separately in an asynchronous system:

- **Ingest** — `POST /orders` throughput and latency. This is fast because the outbox decouples it:
  the request commits the order and the event to the database and returns; it never waits on the broker.
- **End-to-end** — the time from placing an order to it being `Shipped`, i.e. the full journey through
  Ordering → Payments → Shipping, including the outbox poll interval at each hop.

## A representative run

Laptop, Docker Desktop, single instance of each service, outbox poll 200 ms.

| Metric | Value |
| --- | --- |
| Orders | 500 (0 failed) |
| Ingest duration | 0.61 s |
| Ingest throughput | **815 orders/s** |
| POST latency | p50 22 ms · p95 138 ms · p99 237 ms |
| End-to-end (sample 60) | p50 5.5 s · p95 6.6 s · p99 6.7 s |

## Reading the numbers

- **Ingest scales with the database, not the broker.** 815 orders/s on a laptop, with a p99 of 237 ms,
  because a publish is a row insert — the outbox's central trade-off ([ADR-0003](./adr/0003-transactional-outbox.md)).
- **End-to-end latency under a burst is drain time, not per-message cost.** Firing 500 orders at once
  builds a backlog that a single consumer with prefetch 10 works through; the ~5.5 s p50 is mostly
  queue wait. At a steady, sustainable rate the end-to-end latency is ~1 s (three hops at a 200 ms poll
  plus processing) — the [trace](./images/trace-order-flow.png) of a single order shows this directly.
- **Nothing failed.** 500/500 ingested and every sampled order reached a terminal state — throughput
  did not come at the cost of the guarantees.

The tunable knobs are the outbox poll interval (latency vs. database load), the consumer prefetch and
instance count (throughput vs. per-queue ordering, [ADR-0002](./adr/0002-messaging-topology.md)), and
the retry ladder ([ADR-0005](./adr/0005-retry-and-dead-lettering.md)).
