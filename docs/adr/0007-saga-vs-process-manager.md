# ADR-0007: Saga vs. process manager, and where state lives

- **Status:** Accepted
- **Date:** 2026-07-26

## Context

The forward path of order fulfilment is choreographed: Payments reacts to `OrderPlaced`, Shipping
reacts to `PaymentAuthorized`, and Ordering updates status from the events it observes. No service
calls another; each knows only the next event. For the happy path this is ideal — maximally decoupled.

Compensation breaks that model. If shipping fails *after* payment succeeded, the payment must be
refunded. No single service in a pure choreography knows both facts: Payments does not know shipping
failed, and Shipping does not know a payment was taken. The knowledge "payment succeeded **and**
shipping failed, therefore refund" exists nowhere — it is a property of the whole process, and a
choreography has no owner of the whole process.

A distributed transaction is not available ([ADR-0003](./0003-transactional-outbox.md)). The
alternative to a two-phase commit across services is a **saga**: a sequence of local transactions
where each step has a compensating action that semantically undoes it.

The question is not whether to use a saga — the failure above forces one — but how it is coordinated
and where its state lives.

Options considered:

1. **Choreographed compensation.** Each service listens for downstream failure events and compensates
   itself — Payments subscribes to `ShipmentFailed` and refunds. Decoupled, but the compensation logic
   is scattered across services, and no place shows the state of a fulfilment as a whole. Debugging
   "why is this order stuck" means correlating logs across services.
2. **Orchestrated saga / process manager.** A single stateful coordinator holds the state of each
   fulfilment, reacts to events, and issues **commands** to drive the next step or to compensate. One
   place owns the process; the trade is that the coordinator must know each service's commands.
3. **A dedicated orchestration service.** Option 2 in its own bounded context. Maximum separation, at
   the cost of another service to run and its own datastore.

## Decision

**Use an orchestrated saga (a process manager) for order fulfilment, with its state owned by the
initiating context — the Ordering service.**

- The saga is a state machine per order (`order_sagas`): `AwaitingPayment → AwaitingShipment →
  Completed`, with `Cancelled` on a decline and a compensation branch `AwaitingShipment →
  Compensating → Compensated` on a shipment failure.
- **The forward path stays choreographed.** Payments and Shipping still react to events; the saga
  observes those events and advances. Orchestration is introduced only where it is needed — the
  failure and compensation path.
- **Compensation is a command, not an event** ([ADR-0002](./0002-messaging-topology.md)). On
  `ShipmentFailed`, the saga sends `RefundPayment` to Payments' own command queue — an instruction to
  exactly one handler, because a refund is a directed action, not a fact for anyone to interpret.
- **State lives in the initiating context.** Ordering starts the process and already tracks the order,
  so it is the natural home. The saga transition and any command it emits are written in one
  transaction through the outbox, so the saga never advances without the message that drives the next
  step, and never emits a command it did not commit to.

A dedicated orchestration service is deferred, not rejected. It becomes worthwhile when a second
process needs orchestrating, or when the saga's coupling to downstream commands starts to weigh on the
Ordering service.

## Consequences

**Positive**

- Compensation has a home. The refund-on-shipping-failure logic is one state transition in one place,
  not a behaviour smeared across services, and the saga row is the answer to "what state is this
  fulfilment in".
- The happy path keeps its decoupling; only recovery pays the cost of orchestration.
- Exactly-once compensation falls out of the existing machinery: the command goes through the outbox,
  and Payments' refund handler is idempotent via the inbox ([ADR-0004](./0004-idempotent-consumers.md)),
  so a redelivered `RefundPayment` refunds once.

**Negative**

- **The saga can stall, and this cut has no timeout.** A saga in `AwaitingShipment` waits forever if
  neither `OrderShipped` nor `ShipmentFailed` ever arrives — a lost message the outbox should prevent,
  but a dead consumer would cause. A production saga needs a deadline that fires a timeout and
  compensates; that scheduler is deliberately out of scope here and is the sharpest edge in this
  design.
- **The coordinator is coupled to its participants' commands.** The saga knows `RefundPayment` exists
  and what it means. That coupling is the definition of orchestration and the price of having one
  place own the process; it is deliberate, but it is real.
- **State ownership is a decision with a cost.** Housing the saga in Ordering makes Ordering depend on
  the event contracts of Payments and Shipping. A separate orchestration service would isolate that,
  at the cost of another deployable and its own store.
- Compensation is *semantic*, not a rollback. A refunded payment and a cancelled order are new facts,
  not an erasure — the customer was charged and refunded, and both are visible. Saga compensation
  restores a consistent business state, it does not pretend the steps never happened.
