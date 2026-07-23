# Message flows

The four flows worth drawing. The happy path is the one everybody draws; the other three are
where the architecture earns its keep.

---

## 1. Happy path — order to shipment

```mermaid
sequenceDiagram
    participant C as Customer
    participant API as Order API
    participant DB as Order DB
    participant D as Dispatcher
    participant B as Broker
    participant P as Payment
    participant S as Shipping

    C->>API: POST /orders
    activate API
    Note over API,DB: One transaction
    API->>DB: INSERT order + outbox row
    API-->>C: 202 Accepted
    deactivate API

    D->>DB: poll undispatched
    D->>B: publish order.placed
    B-->>D: confirm
    D->>DB: mark dispatched

    B->>P: order.placed
    activate P
    Note over P: INSERT inbox + capture payment<br/>+ outbox row, one transaction
    P-->>B: ack
    deactivate P

    B->>S: payment.captured
    activate S
    Note over S: INSERT inbox + create shipment<br/>+ outbox row, one transaction
    S-->>B: ack
    deactivate S
```

The API returns `202 Accepted`, not `201 Created`. The order exists; the process has not
finished. Returning a status that implies completion is the most common way an event-driven
API lies to its clients.

---

## 2. Duplicate delivery

The dispatcher crashed after the broker confirmed but before marking the row dispatched. On
restart it publishes the same message again.

```mermaid
sequenceDiagram
    participant D as Dispatcher
    participant B as Broker
    participant P as Payment
    participant DB as Payment DB

    D->>B: publish order.placed (id: abc)
    B-->>D: confirm
    Note over D: crash before marking dispatched

    B->>P: order.placed (id: abc)
    P->>DB: INSERT inbox abc → ok
    P->>DB: capture payment, commit
    P-->>B: ack

    Note over D: restarts, row still undispatched
    D->>B: publish order.placed (id: abc) again
    B->>P: order.placed (id: abc)
    P->>DB: INSERT inbox abc → unique violation
    Note over P: already processed — skip
    P-->>B: ack
```

The message id is assigned once, at outbox insertion. A redelivery is the *same* message, not
a new one — which is why a producer that generates a fresh id per attempt silently defeats the
whole mechanism. See [ADR-0004](../adr/0004-idempotent-consumers.md).

---

## 3. Retry and dead-lettering

```mermaid
graph LR
    q["payment.ordering.events<br/><i>work queue</i>"]
    r1["retry.5s"]
    r2["retry.30s"]
    r3["retry.2m"]
    r4["retry.10m"]
    r5["retry.30m"]
    dlq["payment.ordering.events.dlq"]

    q -->|"transient failure<br/>after 3 in-process attempts"| r1
    r1 -->|"TTL expires"| q
    q -->|"fails again"| r2
    r2 -->|"TTL expires"| q
    q -->|"fails again"| r3
    r3 -->|"TTL expires"| q
    q -.->|"levels 4-5"| r4
    r4 -.-> r5
    r5 -.->|"TTL expires"| q
    q -->|"levels exhausted,<br/>or permanent failure"| dlq

    classDef work fill:#438dd5,stroke:#2e6295,color:#fff
    classDef retry fill:#e9a13b,stroke:#b87a26,color:#000
    classDef dead fill:#c8553d,stroke:#8f3c2b,color:#fff
    class q work
    class r1,r2,r3,r4,r5 retry
    class dlq dead
```

A message that fails validation goes straight to the DLQ — no retry ladder. Retrying a
malformed payload for 43 minutes accomplishes nothing except delaying the alert. See
[ADR-0005](../adr/0005-retry-and-dead-lettering.md).

---

## 4. Saga compensation

Payment succeeded, the carrier rejected the shipment. The payment must be reversed.

```mermaid
sequenceDiagram
    participant SG as Saga Host
    participant B as Broker
    participant P as Payment
    participant S as Shipping

    B->>SG: order.placed
    SG->>B: ReserveStock
    B->>SG: stock.reserved
    SG->>B: CapturePayment
    B->>P: CapturePayment
    P->>B: payment.captured
    B->>SG: payment.captured
    SG->>B: CreateShipment
    B->>S: CreateShipment
    S->>B: shipment.rejected
    B->>SG: shipment.rejected

    Note over SG: compensate, in reverse order
    SG->>B: RefundPayment
    B->>P: RefundPayment
    P->>B: payment.refunded
    SG->>B: ReleaseStock
    SG->>B: order.cancelled
```

**Compensation is not rollback.** The payment was really taken and is really refunded — two
transactions, both visible to the customer and to the payment provider, not one undone. This
matters for the design of the business process, not only for the code: some steps cannot be
compensated at all. An email cannot be unsent. Those steps go last, which means the saga
constrains how the business process itself is ordered.

Saga state and the choice between orchestration and choreography are covered in ADR-0007 —
see [ROADMAP.md](../../ROADMAP.md), Milestone 2.
