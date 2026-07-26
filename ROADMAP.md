# Roadmap

Four milestones. Each ships something reviewable on its own.

Track these as GitHub Milestones and attach the issues listed under each.

---

## Milestone 1 — Architecture (docs only)

**Goal:** a reader understands the topology, the delivery guarantees, and every significant
decision, before any code exists.

| Issue | Deliverable |
| --- | --- |
| Write context document | Problem, boundaries, scope, explicit non-goals |
| C4 Level 1 — System Context | Mermaid diagram plus narrative |
| C4 Level 2 — Containers | Mermaid diagram plus narrative |
| Message flow diagrams | Happy path, retry, dead-letter, saga compensation |
| ADR-0001 | Record architecture decisions in ADRs |
| ADR-0002 | Messaging topology |
| ADR-0003 | Transactional outbox |
| ADR-0004 | Idempotent consumers |
| ADR-0005 | Retry and dead-lettering |
| Quality attributes | Throughput, latency, durability targets and trade-offs |

**Exit criteria:** for every arrow in the container diagram, the delivery guarantee is stated
and justified by an ADR.

---

## Milestone 2 — Contracts

**Goal:** the system is fully specified. Two teams could implement either side independently
and interoperate.

| Issue | Deliverable | Status |
| --- | --- | --- |
| Message contract standard | Envelope, required headers, correlation and causation ids | Done — [message-contract.md](./docs/message-contract.md) |
| Event catalogue | Every event, its schema, its producer, its consumers | Done — [event-catalogue.md](./docs/event-catalogue.md) |
| ADR-0006 | Schema versioning and backward compatibility policy | Done — [ADR-0006](./docs/adr/0006-schema-versioning.md) |
| ADR-0007 | Saga vs. process manager, and where state lives | Done — [ADR-0007](./docs/adr/0007-saga-vs-process-manager.md) |
| Saga definitions | State machines with compensation steps, as diagrams | Done — [saga diagram](./docs/diagrams/saga-order-fulfilment.md) |
| Failure catalogue | Every failure mode, its detection, and its response | Done — [failure-catalogue.md](./docs/failure-catalogue.md) |

**Exit criteria:** every event has a JSON Schema with an example payload, and a named owner. **Met** —
[contracts/schemas](./contracts/schemas), owners in the [catalogue](./docs/event-catalogue.md).

---

## Milestone 3 — Reference implementation

**Goal:** `docker compose up` and the whole thing runs on a laptop.

| Issue | Deliverable | Status |
| --- | --- | --- |
| Order service | Publishes through the transactional outbox | Done — [`Ordering`](./src/Ordering) |
| Outbox dispatcher | Polling publisher with ordering and backoff | Done — [`OutboxDispatcher`](./src/EventDriven.Messaging/OutboxDispatcher.cs) |
| Payment and shipping consumers | Idempotent handlers with an inbox | Done — [`Payments`](./src/Payments), [`Shipping`](./src/Shipping) |
| Retry and dead-lettering | Three-layer strategy, delay queues, DLQ | Done — [`ConsumerHost`](./src/EventDriven.Messaging/ConsumerHost.cs) |
| Local environment | Docker Compose: services, RabbitMQ, PostgreSQL | Done — `make up` |
| Saga host | Order fulfilment saga with compensation | Done — [ADR-0007](./docs/adr/0007-saga-vs-process-manager.md), refund-on-shipping-failure |
| CI | Build, analysers, unit and integration tests on every pull request | Deferred — Milestone 4 |
| Integration tests | Testcontainers against a real broker, not a mock | Deferred — Milestone 4 |

**Exit criteria:** a first-time reader places an order and watches it flow end to end in
under five minutes. **Met** — `make up && make demo`.

The reliability spine is delivered: an order flows Placed → Paid → Shipped by events, an over-limit
order is declined, a malformed order is dead-lettered, and a replayed message is deduplicated by the
inbox (exactly-once effect). The **saga** (orchestrated compensation) is choreographed here as
services reacting to events; the dedicated saga host is deferred until its ADR is written.

---

## Milestone 4 — Resilience and operations

**Goal:** prove the guarantees hold when things break.

| Issue | Deliverable | Status |
| --- | --- | --- |
| Chaos suite | Kill the broker, the consumer, and the database mid-transaction; assert no loss | Done — [`scripts/chaos.sh`](./scripts/chaos.sh) (`make chaos`) |
| Duplicate delivery test | Force redelivery, assert exactly-once effect | Done — chaos suite |
| Poison message test | Assert dead-lettering with full diagnostic context | Done — chaos suite |
| Runbooks | DLQ triage, replay procedure, backlog recovery | Done — [docs/runbooks.md](./docs/runbooks.md) |
| Observability | Distributed tracing across the broker; queue depth in the console | Done — OpenTelemetry → Jaeger; one order = one trace |
| Load test | Sustained throughput with latency percentiles, published | Done — [docs/load-results.md](./docs/load-results.md) (`make loadtest`) |

**Exit criteria:** published results table, reproducible with one command. **Met** — `make chaos`,
`make loadtest`.

The chaos suite kills the broker, a consumer, and the database while orders are in flight (and even
places orders while the broker is down, exercising outbox durability) and asserts every order reaches
a terminal state with **no loss**, plus a global **exactly-once** check (one payment and one shipment
per order). Distributed tracing propagates the W3C context through message headers so a single order's
flow across all three services is one trace in Jaeger, and the load test reports ingest throughput
(~815 orders/s) with latency percentiles. Milestone 4 is complete.
