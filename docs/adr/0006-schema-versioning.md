# ADR-0006: Schema versioning and backward compatibility

- **Status:** Accepted
- **Date:** 2026-07-26

## Context

[ADR-0002](./0002-messaging-topology.md) established that a routing key is a public contract, and
that renaming an event type breaks bindings silently. The event *payload* is the other half of that
contract. Once one service consumes `OrderPlaced`, its shape is depended upon by code the producing
team cannot see, deployed on a schedule they do not control. Producer and consumer are never
redeployed at the same instant, so at every deploy there is a window where an old consumer reads a new
message, or a new consumer reads an old one. A versioning policy is the rule that keeps that window
safe.

Options considered:

1. **No policy — change payloads freely.** Works until the first field is renamed or removed, which
   breaks every consumer that read it, with no compile-time warning and no error until production.
2. **Version in the routing key** (`order.placed.v2`). Explicit, but every change — even adding an
   optional field — forces every consumer to rebind, so it makes cheap changes as expensive as
   breaking ones and the version numbers climb fast.
3. **Tolerant reader plus additive-only evolution.** Consumers ignore unknown fields and tolerate
   missing optional ones; producers only ever add optional fields. A genuinely breaking change is a
   new event type, run in parallel during migration.
4. **A shared schema registry that enforces compatibility.** The robust answer at scale, and real
   infrastructure to run. Deferred: the discipline below is what a registry would enforce anyway.

## Decision

**Within an event type, only backward-compatible (additive) changes are allowed. A breaking change is
a new event type — a new routing key — run in parallel until every consumer has migrated.**

- **Consumers are tolerant readers.** Deserialization ignores fields it does not know, and treats any
  field not present in the original contract as optional. A new field never breaks an old consumer.
- **Producers evolve additively.** New fields are optional with a sensible default. A field is never
  removed, renamed, or repurposed — its meaning is frozen the moment it ships. Removing a field is a
  breaking change even if "nobody uses it", because the producer cannot prove that.
- **Breaking changes fork the type.** Changing a field's meaning or type, or removing one, means a new
  routing key (`order.placed` → `order.placed.v2`). Both are published during the migration; consumers
  move at their own pace; the old one is retired only once its bindings are gone.
- **Every event has a JSON Schema and an owner** ([event catalogue](../event-catalogue.md)). The
  schema is the contract of record; the owner is who you talk to before changing it.

## Consequences

**Positive**

- The common case — adding a field — is free and safe, with no coordination.
- Breaking changes are visible and deliberate: a new routing key is a code change a reviewer sees,
  not a payload tweak that slips through.
- Tolerant readers make the fleet resilient to deploy ordering — new and old can coexist by design.

**Negative**

- **Discipline substitutes for enforcement.** Nothing in the build stops a developer from removing a
  field; only review and the schema catch it. Until a schema registry validates compatibility in CI,
  the policy is a convention, and conventions decay.
- **Parallel-run windows are real work.** A breaking change means dual-emitting two event types and
  tracking down every consumer of the old one — which is exactly the cost that discourages breaking
  changes, but it is a cost paid in calendar time.
- **Tolerant reading hides mistakes.** A consumer that silently ignores a field it should have read
  looks healthy while doing the wrong thing. "Ignore unknown fields" must not become "ignore fields I
  forgot to handle".
