# Runbooks

Operational procedures for the reference system. They assume the local stack (`make up`); in a real
deployment the URLs and queue names are the same, the access path differs.

The premise that makes all of these safe is [ADR-0004](./adr/0004-idempotent-consumers.md): every
consumer is idempotent, so **replay is a normal operation, not a gamble**.

---

## 1. Dead-letter queue triage

A message in `{queue}.dlq` failed permanently or exhausted its retries ([ADR-0005](./adr/0005-retry-and-dead-lettering.md)).

**Inspect it** — RabbitMQ UI at [localhost:15672](http://localhost:15672) (guest/guest) → Queues →
`payments.ordering.events.dlq` → *Get messages*. Every dead-lettered message carries diagnostic headers:

| Header | Meaning |
| --- | --- |
| `x-death-reason` | `permanent` (poison), `retries-exhausted`, or `unparseable` |
| `x-exception` | the exception type and message that classified it |
| `x-original-queue` | the work queue it came from |
| `x-attempts` | how many delayed retries it survived |
| `x-dead-lettered-at` | timestamp |

**Decide:**

- `permanent` (e.g. a malformed payload, a business rule rejection) — the message will never succeed
  as-is. Fix the producer or the data, then discard the dead-lettered copy. Do **not** replay it unchanged.
- `retries-exhausted` — a dependency was down longer than the retry ladder. Once it is healthy, the
  message is safe to replay (see below).

---

## 2. Replay a message

Replay re-delivers a message with its **original `MessageId`**. Because the consumer records processed
ids in its inbox, a replay of something already applied is skipped — the effect happens once.

**From the source (preferred), within the outbox retention window:** clear the dispatched flag so the
dispatcher re-publishes. For an order:

```bash
curl -X POST http://localhost:8080/orders/<order-id>/replay
```

**From the DLQ:** move the message back to its work queue (RabbitMQ UI → *Move messages*, or shovel).
Keep the `MessageId`; the inbox handles any duplicate.

Verify it did not double-apply:

```bash
docker compose exec postgres psql -U postgres -d payments \
  -c "select \"OrderId\", count(*) from payments group by \"OrderId\" having count(*) > 1;"
```

An empty result means exactly-once held.

---

## 3. Backlog recovery (a consumer was down)

Queues are durable and messages persistent, so a stopped consumer builds a backlog rather than losing
work — the depth is visible on the console and in the RabbitMQ UI.

1. Bring the consumer back: `docker compose start payments` (or scale it — per-queue ordering is given up
   when you do, which consumers already tolerate per [ADR-0002](./adr/0002-messaging-topology.md)).
2. Watch the depth drain: console *Event flow* panel, or the queue's message count in the UI.
3. If the backlog was caused by a downstream dependency, confirm it is healthy first, or the messages
   will just march through the retry ladder into the DLQ.

**Do not** purge a backlogged queue to "catch up" — that is silent data loss. Let it drain, or move it
aside for controlled replay.

---

## 4. A stalled outbox dispatcher

The outbox only guarantees delivery if the dispatcher is running ([ADR-0003](./adr/0003-transactional-outbox.md)).
A stall is invisible unless watched.

- Symptom: `outbox_messages` with `DispatchedAt IS NULL` and an increasing oldest age.
- Check:

  ```bash
  docker compose exec postgres psql -U postgres -d ordering \
    -c "select count(*) filter (where \"DispatchedAt\" is null) as pending,
               min(\"OccurredAt\") filter (where \"DispatchedAt\" is null) as oldest
        from outbox_messages;"
  ```

- These two numbers (undispatched count, oldest undispatched age) are the mandatory alerts named in
  ADR-0003. Restart the producing service to restore the dispatcher; the rows are still there, nothing
  is lost.
