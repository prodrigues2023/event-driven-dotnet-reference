# C4 Level 2 — Containers

The deployable units and how messages move between them.

```mermaid
graph TB
    customer["Customer"]

    subgraph platform["Order Fulfilment Platform"]
        api["Order API<br/><i>.NET / ASP.NET Core</i><br/>Accepts commands,<br/>writes state and outbox<br/>in one transaction"]
        orderdb[("Order Database<br/><i>PostgreSQL</i><br/>Orders + outbox")]
        dispatcher["Outbox Dispatcher<br/><i>.NET worker</i><br/>Polls, publishes,<br/>marks confirmed"]

        broker["Message Broker<br/><i>RabbitMQ</i><br/>Topic exchanges,<br/>durable queues,<br/>delay and DLQ"]

        payment["Payment Consumer<br/><i>.NET worker</i><br/>Idempotent handler<br/>+ inbox"]
        paydb[("Payment Database<br/><i>PostgreSQL</i><br/>Payments + inbox + outbox")]

        shipping["Shipping Consumer<br/><i>.NET worker</i><br/>Idempotent handler<br/>+ inbox"]
        shipdb[("Shipping Database<br/><i>PostgreSQL</i><br/>Shipments + inbox + outbox")]

        saga["Saga Host<br/><i>.NET worker</i><br/>Order fulfilment state machine,<br/>compensation"]
        sagadb[("Saga Store<br/><i>PostgreSQL</i><br/>Saga state")]
    end

    psp["Payment Provider<br/><i>External</i>"]
    carrier["Carrier<br/><i>External</i>"]

    customer -->|"HTTPS"| api
    api -->|"state + outbox row,<br/>one transaction"| orderdb
    dispatcher -->|"poll undispatched"| orderdb
    dispatcher -->|"publish<br/>with confirms"| broker

    broker -->|"order.placed"| payment
    broker -->|"payment.captured"| shipping
    broker -->|"all domain events"| saga

    payment -->|"effect + inbox,<br/>one transaction"| paydb
    payment -->|"capture"| psp
    shipping -->|"effect + inbox,<br/>one transaction"| shipdb
    shipping -->|"create shipment"| carrier
    saga --> sagadb
    saga -->|"commands"| broker

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef container fill:#438dd5,stroke:#2e6295,color:#fff
    classDef infra fill:#2d6a4f,stroke:#1b4332,color:#fff
    classDef external fill:#999999,stroke:#6b6b6b,color:#fff

    class customer person
    class api,dispatcher,payment,shipping,saga,orderdb,paydb,shipdb,sagadb container
    class broker infra
    class psp,carrier external
```

## Why these boundaries

**The dispatcher is separate from the API.** They have opposite profiles: the API is
latency-sensitive and scales with user traffic; the dispatcher is a steady background process
whose throughput depends on write volume, not on request volume. Running the dispatcher
inside the API process means every API instance polls the same table, and a scale-out event
multiplies the polling load for no benefit.

**Every service owns its database.** This is what makes the outbox work — the state change
and the outbox row are in the *same* database, so one transaction covers both. A shared
database would remove the dual-write problem by removing the service boundary, and with it the
reason for messaging.

**Every consumer has both an inbox and an outbox.** The inbox deduplicates what arrives; the
outbox reliably publishes what results. A consumer that produces events needs both, and the
symmetry is deliberate — it means one pattern, applied everywhere, rather than special cases.

**The saga is a separate host.** Saga state is long-lived and its failure mode is distinct: a
stuck saga is a business process that has silently stopped, which needs different monitoring
from a failed message. Separating it also keeps the coordination logic out of the services,
so a change to the process does not require redeploying payment and shipping.

**External calls happen inside consumers, not inside the API.** The payment provider is slow
and occasionally unavailable. Behind a queue, that is a backlog; in the request path, it is an
outage.

## Deployment notes

Everything runs locally through Docker Compose in Milestone 3. Managed equivalents — Container
Apps or ECS for workers, Amazon MQ or Azure Service Bus for the broker, managed PostgreSQL for
the stores — are documented alongside the implementation. Where Service Bus differs from
RabbitMQ in a way that affects the design, notably in its native delayed delivery, that is
recorded rather than abstracted away.
