# Event-Driven .NET Reference Architecture

> Reliable messaging between services in .NET — the outbox, idempotent consumers, retry and
> dead-lettering, and sagas. Documented first, implemented in the open.

[![Phase](https://img.shields.io/badge/phase-1%20architecture-blue)](./ROADMAP.md)
[![ADRs](https://img.shields.io/badge/ADRs-5-green)](./docs/adr)
[![License](https://img.shields.io/badge/license-MIT-lightgrey)](./LICENSE)

Publishing a message from .NET takes five lines. Publishing it *reliably* — so that it is
never lost when the database commits and the broker call fails, never processed twice when
the consumer crashes after handling but before acknowledging, and never silently discarded
after a poison message — is the actual work. That gap is where most event-driven systems
fail in production, usually months after launch and always at the worst possible time.

This repository documents the decisions that close that gap, and then builds a working
reference implementation on top of them.

**Português:** [README.pt-BR.md](./README.pt-BR.md)

---

## What is here today

| Area | Status | Link |
| --- | --- | --- |
| Context & scope | Done | [docs/context.md](./docs/context.md) |
| C4 diagrams and message flows | Done | [docs/diagrams](./docs/diagrams) |
| Architecture Decision Records | 5 published | [docs/adr](./docs/adr) |
| Quality attributes & trade-offs | Done | [docs/quality-attributes.md](./docs/quality-attributes.md) |
| Reference implementation | Planned — Phase 3 | [ROADMAP.md](./ROADMAP.md) |

## The four problems this architecture solves

| Problem | What goes wrong | Addressed by |
| --- | --- | --- |
| **Dual write** | The transaction commits, the publish fails, the message is lost forever | [ADR-0003 — Transactional outbox](./docs/adr/0003-transactional-outbox.md) |
| **Duplicate delivery** | At-least-once delivery means every consumer will eventually see the same message twice | [ADR-0004 — Idempotent consumers](./docs/adr/0004-idempotent-consumers.md) |
| **Poison messages** | One unprocessable message blocks the queue, or is dropped without trace | [ADR-0005 — Retry and dead-lettering](./docs/adr/0005-retry-and-dead-lettering.md) |
| **Coupled topology** | Adding a consumer requires changing the producer | [ADR-0002 — Topology](./docs/adr/0002-messaging-topology.md) |

## Why documented first

The failure modes above are cheap to design around and extremely expensive to retrofit. A
system that publishes without an outbox does not fail in testing — it fails under load, in
production, months later, and the loss is silent. Writing the decisions down first makes the
trade-offs reviewable before they are load-bearing.

Every decision is recorded as an ADR with its context, the options considered, and the
consequences accepted.

## Roadmap

Four phases, tracked as GitHub milestones. See [ROADMAP.md](./ROADMAP.md).

1. **Architecture** — context, diagrams, ADRs, quality attributes
2. **Contracts** — message schemas, versioning policy, saga definitions, failure catalogue
3. **Reference implementation** — publisher, consumers, outbox, saga host, Docker Compose
4. **Resilience & operations** — chaos tests, observability, runbooks, load testing

## Related

- [iot-realtime-ingestion](https://github.com/prodrigues2023/iot-realtime-ingestion) — the same messaging patterns at the high-throughput ingest edge: durable buffer, idempotent writes
- [k8s-observability-stack](https://github.com/prodrigues2023/k8s-observability-stack) — the tracing that follows an event across the broker, plus queue-depth and consumer-lag signals
- [rag-reference-architecture](https://github.com/prodrigues2023/rag-reference-architecture) — RAG on enterprise workloads
- [rag-evaluation-toolkit](https://github.com/prodrigues2023/rag-evaluation-toolkit) — measuring whether a RAG system works: metrics, golden datasets, judge calibration
- [ai-solution-architecture-kit](https://github.com/prodrigues2023/ai-solution-architecture-kit) — architecture governance artefacts: risk tiers, model certification, review checklists

## Author

Paulo Roberto Franco Rodrigues — Solutions Architect.
Twenty years in distributed systems; more than a decade designing asynchronous integration
with RabbitMQ and .NET, and Kubernetes platforms with the observability to operate them.
[LinkedIn](https://linkedin.com/in/paulo-roberto-franco-rodrigues)

## License

MIT — see [LICENSE](./LICENSE).
