# C4 Level 1 — System Context

Who uses the system, and what it depends on.

```mermaid
graph TB
    customer["Customer<br/><i>Person</i><br/>Places and tracks orders"]
    ops["Operations Team<br/><i>Person</i><br/>Monitors processes,<br/>triages dead letters"]

    platform["Order Fulfilment Platform<br/><i>Software System</i><br/>Coordinates ordering, payment,<br/>stock and shipping through<br/>asynchronous messaging"]

    psp["Payment Provider<br/><i>External System</i><br/>Authorises and captures payment"]
    carrier["Carrier<br/><i>External System</i><br/>Creates shipments,<br/>returns tracking"]
    notify["Notification Provider<br/><i>External System</i><br/>Email and SMS"]
    obs["Observability Platform<br/><i>External System</i><br/>Traces, metrics, logs"]

    customer -->|"Places orders,<br/>tracks status"| platform
    ops -->|"Monitors queues,<br/>replays failures"| obs

    platform -->|"Authorise and<br/>capture payment"| psp
    platform -->|"Create shipment"| carrier
    platform -->|"Send confirmations"| notify
    platform -->|"Traces and metrics"| obs

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef system fill:#1168bd,stroke:#0b4884,color:#fff
    classDef external fill:#999999,stroke:#6b6b6b,color:#fff

    class customer,ops person
    class platform system
    class psp,carrier,notify,obs external
```

## Notes

**The order fulfilment domain is a vehicle, not the point.** It is used because everyone
recognises it and because it naturally contains the hard cases: a payment that must not be
taken twice, a shipment that cannot be un-created, and a process that spans minutes rather
than milliseconds. The architecture is the deliverable.

**External calls are the reason idempotency matters.** Each of the three providers has an
effect that cannot be rolled back by a database transaction. A duplicate message that reaches
the payment provider takes the money twice, and no amount of transactional discipline inside
the platform prevents that — see [ADR-0004](../adr/0004-idempotent-consumers.md).

**Operations is a first-class user.** A messaging architecture that cannot be diagnosed and
replayed by a person at 3am is not finished, regardless of how correct it is on paper. This is
why the dead-letter queue carries diagnostic headers and why replay is a designed operation
rather than an emergency improvisation.
