# ADR-0001: Record architecture decisions in ADRs

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

This repository is a reference architecture. Its value is the reasoning, not the code —
readers need to know why each guarantee was chosen so they can judge which choices transfer
to their own system.

Messaging architectures are particularly prone to losing this reasoning. The decisions that
matter most — delivery semantics, ordering guarantees, where idempotency is enforced — leave
almost no trace in the code. A consumer that deduplicates looks like a consumer that does
not, until a duplicate arrives.

## Decision

Every architecturally significant decision is recorded as a numbered Architecture Decision
Record in `docs/adr/`, using Michael Nygard's lightweight format: context, decision,
consequences.

A decision is architecturally significant if reversing it would require changing more than
one service, or would change the system's delivery, ordering, or durability guarantees.

ADRs are immutable once accepted. A decision that changes is superseded by a new ADR, and the
original is marked `Superseded by ADR-XXXX`.

## Consequences

**Positive**

- The guarantees are written down where a reader can find them, instead of being implied by
  configuration spread across services.
- A new consumer team can read the ADRs and know what they are required to implement before
  they are allowed to subscribe.
- Disagreement becomes reviewable: an issue can target a specific ADR.

**Negative**

- An ADR costs perhaps thirty minutes. Some decisions will be made informally and documented
  late, and a retroactive ADR loses the alternatives that were the valuable part.
- Superseded ADRs accumulate. The index must state status clearly so readers do not act on
  outdated records.
