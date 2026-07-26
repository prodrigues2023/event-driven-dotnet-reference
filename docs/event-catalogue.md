# Event and command catalogue

Every message on the bus, its schema, who produces it, who consumes it, and who owns the contract.
This is the answer to the question [ADR-0002](./adr/0002-messaging-topology.md) says the broker cannot
answer on its own: *who consumes this event?* Each schema lives in
[`contracts/schemas`](../contracts/schemas) with an example payload.

## Events

Published to a topic exchange per bounded context (`{context}.events`), consumed by any number of
subscribers.

| Event | Routing key | Exchange | Producer | Consumers | Schema | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| Order placed | `order.placed` | `ordering.events` | Ordering | Payments; Ordering (saga) | [order-placed.json](../contracts/schemas/order-placed.json) | Ordering |
| Payment authorized | `payment.authorized` | `payments.events` | Payments | Shipping; Ordering (saga) | [payment-authorized.json](../contracts/schemas/payment-authorized.json) | Payments |
| Payment declined | `payment.declined` | `payments.events` | Payments | Ordering (saga) | [payment-declined.json](../contracts/schemas/payment-declined.json) | Payments |
| Order shipped | `order.shipped` | `shipping.events` | Shipping | Ordering (saga) | [order-shipped.json](../contracts/schemas/order-shipped.json) | Shipping |
| Shipment failed | `shipment.failed` | `shipping.events` | Shipping | Ordering (saga) | [shipment-failed.json](../contracts/schemas/shipment-failed.json) | Shipping |
| Payment refunded | `payment.refunded` | `payments.events` | Payments | Ordering (saga) | [payment-refunded.json](../contracts/schemas/payment-refunded.json) | Payments |

## Commands

Sent to a single queue owned by the handling service; exactly one handler.

| Command | Type | Queue | Sender | Handler | Schema | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| Refund payment | `payment.refund` | `payments.commands` | Ordering (saga) | Payments | [refund-payment.json](../contracts/schemas/refund-payment.json) | Payments |

## The flow at a glance

```
OrderPlaced ─▶ Payments
                 ├─ amount ≤ 0        ▶ (poison) dead-letter
                 ├─ amount > 5000     ▶ PaymentDeclined ─▶ saga: Cancelled
                 └─ otherwise         ▶ PaymentAuthorized ─▶ Shipping
                                                              ├─ amount > 2000 ▶ ShipmentFailed
                                                              │                   └─▶ saga: RefundPayment ▶ Payments ▶ PaymentRefunded ▶ saga: Compensated
                                                              └─ otherwise     ▶ OrderShipped ─▶ saga: Completed
```

See the [saga state machine](./diagrams/saga-order-fulfilment.md) for the compensation path in full,
and the [message contract](./message-contract.md) for the envelope every message shares.
