# Architecture Decision Records

Decisions are numbered, immutable once accepted, and superseded rather than edited.
See [ADR-0001](./0001-record-architecture-decisions.md) for the process itself.

| ADR | Title | Status |
| --- | --- | --- |
| [0001](./0001-record-architecture-decisions.md) | Record architecture decisions in ADRs | Accepted |
| [0002](./0002-messaging-topology.md) | Messaging topology | Accepted |
| [0003](./0003-transactional-outbox.md) | Transactional outbox | Accepted |
| [0004](./0004-idempotent-consumers.md) | Idempotent consumers | Accepted |
| [0005](./0005-retry-and-dead-lettering.md) | Retry and dead-lettering | Accepted |
| 0006 | Schema versioning and compatibility | Planned — Milestone 2 |
| 0007 | Saga vs. process manager | Planned — Milestone 2 |

## How the accepted decisions fit together

They are not independent. Each one exists because of the one before it:

- **0002** establishes at-least-once delivery over durable queues
- **0003** guarantees the message is never lost between the database and the broker — and in
  doing so, guarantees duplicates
- **0004** makes those duplicates harmless, which is what turns replay into a safe operation
- **0005** ensures a message that cannot be processed ends up somewhere visible, and relies on
  0004 to make the eventual replay safe

Adopting 0003 without 0004 is worse than adopting neither, because it converts a rare silent
loss into a routine duplicate that no consumer is prepared for.

## Template

```markdown
# ADR-XXXX: Title

- **Status:** Proposed | Accepted | Superseded by ADR-YYYY
- **Date:** YYYY-MM-DD

## Context

The forces at play: the requirement, the constraints, the options considered and why each
was or was not viable.

## Decision

What was decided, in the active voice. What was deliberately deferred.

## Consequences

**Positive** — what this buys.

**Negative** — what it costs, and what the team will have to live with. An ADR with no
negative consequences has not been thought through.
```

## Disagreeing with a decision

Open an issue titled `ADR-XXXX: <your objection>`. Arguments grounded in a context the ADR
did not consider are the most useful kind — the throughput assumptions behind several of
these decisions are explicitly provisional until the Milestone 4 load tests report.
